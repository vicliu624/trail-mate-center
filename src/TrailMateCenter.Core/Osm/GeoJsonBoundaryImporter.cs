using System.Globalization;
using System.Text.Json;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record BoundaryImportResult
{
    public bool Success { get; init; }
    public string Name { get; init; } = "Imported boundary";
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public static BoundaryImportResult Fail(string message)
    {
        return new BoundaryImportResult
        {
            Success = false,
            ErrorMessage = message,
        };
    }
}

public sealed class GeoJsonBoundaryImporter
{
    public async Task<BoundaryImportResult> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BoundaryImportResult.Fail("Boundary file path is empty.");
        if (!File.Exists(path))
            return BoundaryImportResult.Fail("Boundary file missing.");

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Import(document.RootElement, Path.GetFileNameWithoutExtension(path));
    }

    public BoundaryImportResult Import(string geoJson, string fallbackName = "Imported boundary")
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            return BoundaryImportResult.Fail("Boundary GeoJSON is empty.");

        using var document = JsonDocument.Parse(geoJson);
        return Import(document.RootElement, fallbackName);
    }

    public BoundaryImportResult Import(JsonElement root, string fallbackName = "Imported boundary")
    {
        var points = new List<(double Lon, double Lat)>();
        var name = ExtractName(root) ?? fallbackName;
        CollectCoordinates(root, points);
        if (points.Count == 0)
            return BoundaryImportResult.Fail("No polygon coordinates found in boundary GeoJSON.");

        var west = points.Min(static p => p.Lon);
        var east = points.Max(static p => p.Lon);
        var south = points.Min(static p => p.Lat);
        var north = points.Max(static p => p.Lat);

        return new BoundaryImportResult
        {
            Success = true,
            Name = string.IsNullOrWhiteSpace(name) ? "Imported boundary" : name.Trim(),
            Bounds = new GeoBounds(west, south, east, north).Normalize(),
            BoundaryGeoJson = ExtractGeometryJson(root),
        };
    }

    private static string? ExtractName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "name", "display_name", "NAME", "Name" })
                {
                    if (properties.TryGetProperty(key, out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        var text = value.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }
            }

            if (element.TryGetProperty("features", out var features) &&
                features.ValueKind == JsonValueKind.Array)
            {
                foreach (var feature in features.EnumerateArray())
                {
                    var name = ExtractName(feature);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
        }

        return null;
    }

    private static string ExtractGeometryJson(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("type", out var typeElement) &&
            typeElement.ValueKind == JsonValueKind.String)
        {
            var type = typeElement.GetString();
            if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("geometry", out var geometry))
            {
                return geometry.GetRawText();
            }

            if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
            {
                return root.GetRawText();
            }
        }

        return root.GetRawText();
    }

    private static void CollectCoordinates(JsonElement element, List<(double Lon, double Lat)> output)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("type", out var typeElement) &&
            typeElement.ValueKind == JsonValueKind.String)
        {
            var type = typeElement.GetString();
            if (string.Equals(type, "FeatureCollection", StringComparison.OrdinalIgnoreCase) &&
                element.TryGetProperty("features", out var features) &&
                features.ValueKind == JsonValueKind.Array)
            {
                foreach (var feature in features.EnumerateArray())
                    CollectCoordinates(feature, output);
                return;
            }

            if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) &&
                element.TryGetProperty("geometry", out var geometry))
            {
                CollectCoordinates(geometry, output);
                return;
            }

            if ((string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase)) &&
                element.TryGetProperty("coordinates", out var coordinates))
            {
                CollectCoordinateArrays(coordinates, output);
            }
        }
    }

    private static void CollectCoordinateArrays(JsonElement element, List<(double Lon, double Lat)> output)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return;

        if (TryReadPosition(element, out var lon, out var lat))
        {
            output.Add((lon, lat));
            return;
        }

        foreach (var child in element.EnumerateArray())
        {
            CollectCoordinateArrays(child, output);
        }
    }

    private static bool TryReadPosition(JsonElement element, out double lon, out double lat)
    {
        lon = 0;
        lat = 0;
        if (element.ValueKind != JsonValueKind.Array)
            return false;

        var values = element.EnumerateArray().Take(2).ToArray();
        if (values.Length < 2)
            return false;
        if (!TryReadDouble(values[0], out lon))
            return false;
        if (!TryReadDouble(values[1], out lat))
            return false;

        return lon >= -180 &&
               lon <= 180 &&
               lat >= -90 &&
               lat <= 90;
    }

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);
        if (element.ValueKind == JsonValueKind.String)
            return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        value = 0;
        return false;
    }
}
