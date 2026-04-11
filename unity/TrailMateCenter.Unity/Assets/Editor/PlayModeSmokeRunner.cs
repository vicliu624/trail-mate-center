#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace TrailMateCenter.Unity.EditorTools
{
    [InitializeOnLoad]
    public static class PlayModeSmokeRunner
    {
        private const string ActiveKey = "TrailMate.PlaySmoke.Active";
        private const string StartAtKey = "TrailMate.PlaySmoke.StartAt";
        private const string DurationKey = "TrailMate.PlaySmoke.Duration";
        private const string SawErrorKey = "TrailMate.PlaySmoke.SawError";

        private static bool _hooksInstalled;

        static PlayModeSmokeRunner()
        {
            EnsureHooks();
        }

        public static void Run()
        {
            if (SessionState.GetBool(ActiveKey, false))
                return;

            var duration = ResolveDurationSeconds();
            SessionState.SetBool(ActiveKey, true);
            SessionState.EraseFloat(StartAtKey);
            SessionState.SetString(StartAtKey, string.Empty);
            SessionState.SetFloat(DurationKey, (float)duration);
            SessionState.SetBool(SawErrorKey, false);

            EnsureHooks();
            Debug.Log($"[PlaySmoke] Starting play mode smoke test. Duration={duration:F1}s");
            EditorApplication.isPlaying = true;
        }

        private static void EnsureHooks()
        {
            if (_hooksInstalled)
                return;

            _hooksInstalled = true;
            Application.logMessageReceived += OnLogMessage;
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void RemoveHooks()
        {
            if (!_hooksInstalled)
                return;

            _hooksInstalled = false;
            Application.logMessageReceived -= OnLogMessage;
            EditorApplication.update -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            if (!EditorApplication.isPlaying)
                return;

            var startedAt = ResolveStartAtUtc();
            if (startedAt == null)
                return;

            var duration = SessionState.GetFloat(DurationKey, 8f);
            var elapsed = (DateTimeOffset.UtcNow - startedAt.Value).TotalSeconds;
            if (elapsed < duration)
                return;

            Debug.Log("[PlaySmoke] Duration reached. Leaving play mode.");
            EditorApplication.isPlaying = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetString(
                    StartAtKey,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            var code = SessionState.GetBool(SawErrorKey, false) ? 1 : 0;
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseBool(SawErrorKey);
            SessionState.EraseString(StartAtKey);
            SessionState.EraseFloat(DurationKey);

            RemoveHooks();
            Debug.Log($"[PlaySmoke] Completed. ExitCode={code}");
            EditorApplication.Exit(code);
        }

        private static DateTimeOffset? ResolveStartAtUtc()
        {
            var raw = SessionState.GetString(StartAtKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs))
                return null;

            try
            {
                var parsed = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
                if (parsed.Year < 2000 || parsed > DateTimeOffset.UtcNow.AddHours(1))
                    return null;
                return parsed;
            }
            catch
            {
                return null;
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                SessionState.SetBool(SawErrorKey, true);
            }
        }

        private static double ResolveDurationSeconds()
        {
            var raw = Environment.GetEnvironmentVariable("TRAILMATE_UNITY_SMOKE_SECONDS");
            if (!double.TryParse(raw, out var parsed))
                return 8;

            return Math.Clamp(parsed, 2, 60);
        }
    }
}
#endif
