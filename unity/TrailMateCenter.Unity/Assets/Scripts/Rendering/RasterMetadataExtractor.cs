using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
namespace TrailMateCenter.Unity.Rendering
{
public static class RasterMetadataExtractor
{
    public static RasterMetadata Extract(string sourcePath, RasterConversionConfig config)
    {
        var metadata = new RasterMetadata();

        try
        {
            if (TryReadSidecar(sourcePath, out var sidecar))
                Merge(metadata, sidecar);

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is ".tif" or ".tiff")
            {
                if (TryReadGdalInfo(sourcePath, config, out var gdal))
                    Merge(metadata, gdal);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Raster] metadata extraction failed: {ex.Message}");
        }

        return metadata;
    }

    private static bool TryReadSidecar(string sourcePath, out RasterMetadata metadata)
    {
        metadata = new RasterMetadata();
        var candidates = new[]
        {
            $"{sourcePath}.json",
            $"{sourcePath}.meta.json",
            $"{Path.ChangeExtension(sourcePath, null)}.json",
            $"{Path.ChangeExtension(sourcePath, null)}.meta.json",
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                var parsed = ParseGenericMetadata(root);
                if (parsed != null)
                {
                    metadata = parsed;
                    return true;
                }
            }
            catch
            {
                // ignore per candidate
            }
        }

        return false;
    }

    private static bool TryReadGdalInfo(string sourcePath, RasterConversionConfig config, out RasterMetadata metadata)
    {
        metadata = new RasterMetadata();
        var gdalInfoPath = ResolveGdalInfoPath(config);
        if (string.IsNullOrWhiteSpace(gdalInfoPath))
            gdalInfoPath = "gdalinfo";

        var args = $"-json \"{sourcePath}\"";
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = gdalInfoPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(start);
            if (process == null)
                return false;

            if (!process.WaitForExit(config.TimeoutMs))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return false;
            }

            if (process.ExitCode != 0)
                return false;

            var stdout = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(stdout))
                return false;

            var root = JObject.Parse(stdout);
            var parsed = ParseGdalInfo(root);
            if (parsed == null)
                return false;

            metadata = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveGdalInfoPath(RasterConversionConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.GdalInfoPath))
            return config.GdalInfoPath;

        var gdalTranslatePath = config.GdalTranslatePath;
        if (string.IsNullOrWhiteSpace(gdalTranslatePath))
            return "gdalinfo";

        var lower = gdalTranslatePath.ToLowerInvariant();
        if (lower.EndsWith("gdal_translate.exe"))
            return gdalTranslatePath[..^"gdal_translate.exe".Length] + "gdalinfo.exe";
        if (lower.EndsWith("gdal_translate"))
            return gdalTranslatePath[..^"gdal_translate".Length] + "gdalinfo";

        return "gdalinfo";
    }

    private static RasterMetadata? ParseGenericMetadata(JObject root)
    {
        var metadata = new RasterMetadata();
        var boundsToken = root["bounds"] as JObject;
        if (boundsToken != null)
        {
            if (TryParseBounds(boundsToken, out var bounds))
                metadata.Bounds = bounds;
        }
        else
        {
            if (TryParseBounds(root, out var bounds))
                metadata.Bounds = bounds;
        }

        metadata.Crs = root.Value<string>("crs") ?? root.Value<string>("srs") ?? string.Empty;
        metadata.TileTemplateUri =
            root.Value<string>("tileTemplateUri")
            ?? root.Value<string>("tile_template_uri")
            ?? string.Empty;
        metadata.MinZoom = (int?)TryGetDouble(root, "minZoom") ?? (int?)TryGetDouble(root, "min_zoom");
        metadata.MaxZoom = (int?)TryGetDouble(root, "maxZoom") ?? (int?)TryGetDouble(root, "max_zoom");
        metadata.TileSize = (int?)TryGetDouble(root, "tileSize") ?? (int?)TryGetDouble(root, "tile_size");
        metadata.MinValue = TryGetDouble(root, "minValue") ?? TryGetDouble(root, "min") ?? TryGetDouble(root, "valueMin");
        metadata.MaxValue = TryGetDouble(root, "maxValue") ?? TryGetDouble(root, "max") ?? TryGetDouble(root, "valueMax");
        metadata.NoDataValue = TryGetDouble(root, "noDataValue") ?? TryGetDouble(root, "nodata");
        metadata.ValueScale = TryGetDouble(root, "valueScale") ?? TryGetDouble(root, "scale");
        metadata.ValueOffset = TryGetDouble(root, "valueOffset") ?? TryGetDouble(root, "offset");
        metadata.ClassBreaks = ParseClassBreaks(root);
        metadata.Unit = root.Value<string>("unit") ?? string.Empty;
        metadata.Palette = root.Value<string>("palette") ?? string.Empty;

        if (metadata.Bounds == null && metadata.MinValue == null && metadata.MaxValue == null && string.IsNullOrWhiteSpace(metadata.Crs))
            return null;

        return metadata;
    }

    private static RasterMetadata? ParseGdalInfo(JObject root)
    {
        var metadata = new RasterMetadata();
        var corners = root["cornerCoordinates"] as JObject;
        if (corners != null &&
            TryParsePoint(corners["lowerLeft"], out var llx, out var lly) &&
            TryParsePoint(corners["upperRight"], out var urx, out var ury))
        {
            var minX = Math.Min(llx, urx);
            var maxX = Math.Max(llx, urx);
            var minZ = Math.Min(lly, ury);
            var maxZ = Math.Max(lly, ury);
            metadata.Bounds = new RasterBounds(minX, minZ, maxX, maxZ);
        }

        metadata.Crs =
            root["coordinateSystem"]?["wkt"]?.Value<string>()
            ?? root["coordinateSystem"]?["projjson"]?["name"]?.Value<string>()
            ?? string.Empty;

        var bands = root["bands"] as JArray;
        if (bands != null && bands.Count > 0)
        {
            var first = bands[0] as JObject;
            var stats = first?["metadata"]?[""] as JObject;
            metadata.MinValue =
                TryGetDouble(stats, "STATISTICS_MINIMUM")
                ?? TryGetDouble(first, "minimum")
                ?? TryGetDouble(first, "min");
            metadata.MaxValue =
                TryGetDouble(stats, "STATISTICS_MAXIMUM")
                ?? TryGetDouble(first, "maximum")
                ?? TryGetDouble(first, "max");
            metadata.NoDataValue = TryGetDouble(first, "noDataValue");
            metadata.ValueScale = TryGetDouble(first, "scale");
            metadata.ValueOffset = TryGetDouble(first, "offset");
        }

        if (metadata.Bounds == null && metadata.MinValue == null && metadata.MaxValue == null && string.IsNullOrWhiteSpace(metadata.Crs))
            return null;
        return metadata;
    }

    private static bool TryParseBounds(JObject token, out RasterBounds bounds)
    {
        bounds = default;
        var minX = TryGetDouble(token, "minX") ?? TryGetDouble(token, "xmin") ?? TryGetDouble(token, "left");
        var maxX = TryGetDouble(token, "maxX") ?? TryGetDouble(token, "xmax") ?? TryGetDouble(token, "right");
        var minZ = TryGetDouble(token, "minZ") ?? TryGetDouble(token, "minY") ?? TryGetDouble(token, "ymin") ?? TryGetDouble(token, "bottom");
        var maxZ = TryGetDouble(token, "maxZ") ?? TryGetDouble(token, "maxY") ?? TryGetDouble(token, "ymax") ?? TryGetDouble(token, "top");

        if (minX == null || maxX == null || minZ == null || maxZ == null)
            return false;

        var parsed = new RasterBounds(minX.Value, minZ.Value, maxX.Value, maxZ.Value);
        if (!parsed.IsValid)
            return false;

        bounds = parsed;
        return true;
    }

    private static bool TryParsePoint(JToken? token, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (token is not JArray arr || arr.Count < 2)
            return false;

        if (!TryTokenDouble(arr[0], out x))
            return false;
        if (!TryTokenDouble(arr[1], out y))
            return false;
        return true;
    }

    private static double? TryGetDouble(JObject? obj, string key)
    {
        if (obj == null)
            return null;
        if (!obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token))
            return null;
        return TryTokenDouble(token, out var value) ? value : (double?)null;
    }

    private static bool TryTokenDouble(JToken? token, out double value)
    {
        value = 0;
        if (token == null)
            return false;
        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
        {
            value = token.Value<double>();
            return true;
        }
        if (token.Type == JTokenType.String)
        {
            return double.TryParse(
                token.Value<string>(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value);
        }
        return false;
    }

    private static void Merge(RasterMetadata target, RasterMetadata source)
    {
        if (source.Bounds != null && source.Bounds.Value.IsValid)
            target.Bounds = source.Bounds;

        if (!string.IsNullOrWhiteSpace(source.Crs))
            target.Crs = source.Crs;

        if (source.MinValue != null)
            target.MinValue = source.MinValue;
        if (source.MaxValue != null)
            target.MaxValue = source.MaxValue;
        if (source.NoDataValue != null)
            target.NoDataValue = source.NoDataValue;
        if (source.ValueScale != null)
            target.ValueScale = source.ValueScale;
        if (source.ValueOffset != null)
            target.ValueOffset = source.ValueOffset;
        if (source.ClassBreaks.Count > 0)
            target.ClassBreaks = source.ClassBreaks.ToArray();
        if (!string.IsNullOrWhiteSpace(source.Unit))
            target.Unit = source.Unit;
        if (!string.IsNullOrWhiteSpace(source.Palette))
            target.Palette = source.Palette;
        if (!string.IsNullOrWhiteSpace(source.TileTemplateUri))
            target.TileTemplateUri = source.TileTemplateUri;
        if (source.MinZoom.HasValue)
            target.MinZoom = source.MinZoom;
        if (source.MaxZoom.HasValue)
            target.MaxZoom = source.MaxZoom;
        if (source.TileSize.HasValue)
            target.TileSize = source.TileSize;
    }

    private static IReadOnlyList<double> ParseClassBreaks(JObject root)
    {
        var token = (root["classBreaks"] as JArray) ?? (root["class_breaks"] as JArray);
        if (token == null || token.Count == 0)
            return Array.Empty<double>();

        var values = new List<double>(token.Count);
        foreach (var item in token)
        {
            if (TryTokenDouble(item, out var parsed))
                values.Add(parsed);
        }

        if (values.Count == 0)
            return Array.Empty<double>();

        values.Sort();
        return values.ToArray();
    }
}
}

