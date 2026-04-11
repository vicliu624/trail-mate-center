#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrailMateCenter.Unity.EditorTools
{
    public static class WindowsPlayerBuilder
    {
        private const string DefaultOutputRelativePath = "Builds/Windows64/TrailMateCenter.Unity.exe";
        private const string BootstrapSceneAssetPath = "Assets/__Generated/BuildBootstrap.unity";

        [MenuItem("Tools/TrailMateCenter/Build Windows Player")]
        public static void BuildFromMenu()
        {
            RunBuild(revealOutput: true, forceDevelopment: false);
        }

        [MenuItem("Tools/TrailMateCenter/Build Windows Player (Development)")]
        public static void BuildDevelopmentFromMenu()
        {
            RunBuild(revealOutput: true, forceDevelopment: true);
        }

        public static void Build()
        {
            RunBuild(revealOutput: false, forceDevelopment: false);
        }

        public static void BuildDevelopment()
        {
            RunBuild(revealOutput: false, forceDevelopment: true);
        }

        private static void RunBuild(bool revealOutput, bool forceDevelopment)
        {
            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Environment.CurrentDirectory;
                var outputPath = ResolveOutputPath(projectRoot);
                var development = forceDevelopment || ParseBooleanEnvironmentVariable("TRAILMATE_UNITY_PLAYER_DEVELOPMENT", defaultValue: false);
                var clean = ParseBooleanEnvironmentVariable("TRAILMATE_UNITY_PLAYER_CLEAN", defaultValue: false);
                var previousSetup = EditorSceneManager.GetSceneManagerSetup();
                var createdBootstrapScene = false;

                try
                {
                    var scenes = ResolveBuildScenes(ref createdBootstrapScene);
                    PrepareOutputDirectory(outputPath, clean);
                    EnsureWindowsBuildTarget();

                    var buildOptions = BuildOptions.None;
                    if (development)
                    {
                        buildOptions |= BuildOptions.Development;
                        buildOptions |= BuildOptions.AllowDebugging;
                    }

                    Debug.Log($"[WindowsPlayerBuilder] Building Windows player to {outputPath}");
                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = outputPath,
                        target = BuildTarget.StandaloneWindows64,
                        options = buildOptions,
                    });

                    if (report.summary.result != BuildResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Unity player build failed. Result={report.summary.result}, Errors={report.summary.totalErrors}, Output={outputPath}");
                    }

                    Debug.Log($"[WindowsPlayerBuilder] Build succeeded. Output={outputPath}");
                    if (revealOutput)
                    {
                        EditorUtility.RevealInFinder(outputPath);
                    }

                    if (Application.isBatchMode)
                    {
                        EditorApplication.Exit(0);
                    }
                }
                finally
                {
                    if (!Application.isBatchMode && previousSetup is { Length: > 0 })
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                    }

                    if (createdBootstrapScene)
                    {
                        AssetDatabase.DeleteAsset(BootstrapSceneAssetPath);
                        AssetDatabase.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WindowsPlayerBuilder] {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

        private static string[] ResolveBuildScenes(ref bool createdBootstrapScene)
        {
            var configuredScenes = EditorBuildSettings.scenes
                .Where(static scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path) && File.Exists(scene.path))
                .Select(static scene => scene.path)
                .ToArray();

            if (configuredScenes.Length > 0)
                return configuredScenes;

            createdBootstrapScene = true;
            return new[] { EnsureBootstrapSceneExists() };
        }

        private static string EnsureBootstrapSceneExists()
        {
            EnsureAssetFolderExists("Assets/__Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "BuildBootstrap";

            if (!EditorSceneManager.SaveScene(scene, BootstrapSceneAssetPath, saveAsCopy: false))
            {
                throw new InvalidOperationException($"Failed to save bootstrap scene to {BootstrapSceneAssetPath}.");
            }

            AssetDatabase.Refresh();
            return BootstrapSceneAssetPath;
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            var segments = assetFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Asset folder must be under Assets/: {assetFolderPath}");
            }

            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }
                current = next;
            }
        }

        private static void PrepareOutputDirectory(string outputPath, bool clean)
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new InvalidOperationException($"Cannot resolve output directory for {outputPath}");

            Directory.CreateDirectory(outputDirectory);
            if (!clean)
                return;

            TryDeleteIfExists(outputPath);
            TryDeleteDirectoryIfExists(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputPath) + "_Data"));
            TryDeleteDirectoryIfExists(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputPath) + "_BackUpThisFolder_ButDontShipItWithYourGame"));
            TryDeleteDirectoryIfExists(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputPath) + "_BurstDebugInformation_DoNotShip"));
        }

        private static void EnsureWindowsBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
                return;

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException("Failed to switch active build target to StandaloneWindows64.");
            }
        }

        private static string ResolveOutputPath(string projectRoot)
        {
            var raw = Environment.GetEnvironmentVariable("TRAILMATE_UNITY_PLAYER_OUTPUT");
            var configured = string.IsNullOrWhiteSpace(raw)
                ? Path.Combine(projectRoot, DefaultOutputRelativePath)
                : raw.Trim();

            var expanded = Environment.ExpandEnvironmentVariables(configured);
            return Path.GetFullPath(Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(projectRoot, expanded));
        }

        private static bool ParseBooleanEnvironmentVariable(string name, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
                return defaultValue;
            if (bool.TryParse(raw, out var parsed))
                return parsed;

            return raw.Trim() switch
            {
                "1" => true,
                "yes" => true,
                "on" => true,
                "0" => false,
                "no" => false,
                "off" => false,
                _ => defaultValue,
            };
        }

        private static void TryDeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WindowsPlayerBuilder] Failed to delete file {path}: {ex.Message}");
            }
        }

        private static void TryDeleteDirectoryIfExists(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WindowsPlayerBuilder] Failed to delete directory {path}: {ex.Message}");
            }
        }
    }
}
#endif
