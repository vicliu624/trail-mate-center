using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
namespace TrailMateCenter.Unity.Rendering
{
public static class RasterConverter
{
    public static bool TryConvert(string inputPath, RasterConversionConfig config, out string outputPath)
    {
        outputPath = string.Empty;
        if (!config.Enable)
            return false;

        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            return false;

        var cacheDir = ResolveCacheDirectory(config);
        if (string.IsNullOrWhiteSpace(cacheDir))
            return false;

        Directory.CreateDirectory(cacheDir);
        var format = string.IsNullOrWhiteSpace(config.OutputFormat) ? "png" : config.OutputFormat;
        var hash = ComputeHash($"{inputPath}|{config.PreferredBand}|{config.TargetCrs}|{config.AutoScaleToByte}");
        var outputFile = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(inputPath)}_{hash}.{format}");
        var sourceTime = File.GetLastWriteTimeUtc(inputPath);

        if (File.Exists(outputFile))
        {
            var outputTime = File.GetLastWriteTimeUtc(outputFile);
            if (outputTime >= sourceTime)
            {
                outputPath = outputFile;
                return true;
            }
        }

        var workPath = inputPath;
        if (config.EnableReprojection && IsGdalCrs(config.TargetCrs))
        {
            var srcMeta = RasterMetadataExtractor.Extract(inputPath, config);
            if (!string.IsNullOrWhiteSpace(srcMeta.Crs) &&
                !string.Equals(srcMeta.Crs.Trim(), config.TargetCrs.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var warpedPath = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(inputPath)}_{hash}_warped.tif");
                if (!TryRunGdalWarp(inputPath, warpedPath, config))
                    return false;
                workPath = warpedPath;
            }
        }

        var meta = RasterMetadataExtractor.Extract(workPath, config);
        if (!TryRunGdalTranslate(workPath, outputFile, config, meta))
            return false;

        WriteSidecarMetadata(outputFile, meta, config);
        outputPath = outputFile;
        return true;
    }

    private static string ResolveCacheDirectory(RasterConversionConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.CacheDirectory))
            return config.CacheDirectory;

        return Path.Combine(Application.persistentDataPath, "raster_cache");
    }

    private static string ComputeHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(12);
        for (var i = 0; i < 6; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    private static bool TryRunGdalWarp(string inputPath, string outputPath, RasterConversionConfig config)
    {
        var gdalWarp = string.IsNullOrWhiteSpace(config.GdalWarpPath) ? "gdalwarp" : config.GdalWarpPath;
        var args = $"-overwrite -t_srs \"{config.TargetCrs}\" \"{inputPath}\" \"{outputPath}\"";
        return TryRunGdalProcess(gdalWarp, args, config.TimeoutMs, "gdalwarp");
    }

    private static bool IsGdalCrs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.StartsWith("EPSG:") || normalized.Contains("+PROJ") || normalized.Contains("PROJCS") || normalized.Contains("GEOGCS");
    }

    private static bool TryRunGdalTranslate(string inputPath, string outputPath, RasterConversionConfig config, RasterMetadata meta)
    {
        var gdalTranslate = string.IsNullOrWhiteSpace(config.GdalTranslatePath) ? "gdal_translate" : config.GdalTranslatePath;
        var args = new StringBuilder();
        args.Append($"-of {config.OutputFormat} ");
        args.Append($"-b {Math.Max(1, config.PreferredBand)} ");
        if (config.AutoScaleToByte && meta.MinValue.HasValue && meta.MaxValue.HasValue && meta.MaxValue.Value > meta.MinValue.Value)
        {
            args.Append($"-scale {meta.MinValue.Value} {meta.MaxValue.Value} 0 255 ");
            args.Append("-ot Byte ");
            meta.ValueScale = meta.MaxValue.Value - meta.MinValue.Value;
            meta.ValueOffset = meta.MinValue.Value;
        }
        args.Append($"\"{inputPath}\" \"{outputPath}\"");

        return TryRunGdalProcess(gdalTranslate, args.ToString(), config.TimeoutMs, "gdal_translate");
    }

    private static bool TryRunGdalProcess(string executable, string args, int timeoutMs, string action)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { /* ignore */ }
                Debug.LogWarning($"[Raster] {action} timeout");
                return false;
            }

            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd();
                Debug.LogWarning($"[Raster] {action} failed: {err}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Raster] {action} failed: {ex.Message}");
            return false;
        }
    }

    private static void WriteSidecarMetadata(string texturePath, RasterMetadata meta, RasterConversionConfig config)
    {
        try
        {
            var root = new JObject();
            if (meta.Bounds != null && meta.Bounds.Value.IsValid)
            {
                root["bounds"] = new JObject
                {
                    ["minX"] = meta.Bounds.Value.MinX,
                    ["minZ"] = meta.Bounds.Value.MinZ,
                    ["maxX"] = meta.Bounds.Value.MaxX,
                    ["maxZ"] = meta.Bounds.Value.MaxZ,
                };
            }
            root["crs"] = string.IsNullOrWhiteSpace(meta.Crs) ? config.TargetCrs : meta.Crs;
            if (!string.IsNullOrWhiteSpace(meta.TileTemplateUri))
                root["tileTemplateUri"] = meta.TileTemplateUri;
            if (meta.MinZoom.HasValue) root["minZoom"] = meta.MinZoom.Value;
            if (meta.MaxZoom.HasValue) root["maxZoom"] = meta.MaxZoom.Value;
            if (meta.TileSize.HasValue) root["tileSize"] = meta.TileSize.Value;
            if (meta.MinValue.HasValue) root["minValue"] = meta.MinValue.Value;
            if (meta.MaxValue.HasValue) root["maxValue"] = meta.MaxValue.Value;
            if (meta.NoDataValue.HasValue) root["noDataValue"] = meta.NoDataValue.Value;
            if (meta.ValueScale.HasValue) root["valueScale"] = meta.ValueScale.Value;
            if (meta.ValueOffset.HasValue) root["valueOffset"] = meta.ValueOffset.Value;
            if (meta.ClassBreaks.Count > 0)
                root["classBreaks"] = new JArray(meta.ClassBreaks);
            if (!string.IsNullOrWhiteSpace(meta.Unit)) root["unit"] = meta.Unit;
            File.WriteAllText($"{texturePath}.meta.json", root.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Raster] writing sidecar failed: {ex.Message}");
        }
    }
}
}

