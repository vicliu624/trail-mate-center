using System.Globalization;
using System.Text.Json;

namespace TrailMateCenter.Osm;

public sealed class GeoJsonPointInPolygonFilter
{
    private readonly IReadOnlyList<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>> _polygons;

    private GeoJsonPointInPolygonFilter(
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>> polygons)
    {
        _polygons = polygons;
    }

    public static GeoJsonPointInPolygonFilter? TryCreate(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(geoJson);
            var polygons = new List<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>>();
            CollectPolygons(document.RootElement, polygons);
            return polygons.Count == 0 ? null : new GeoJsonPointInPolygonFilter(polygons);
        }
        catch
        {
            return null;
        }
    }

    public bool Contains(double latitude, double longitude)
    {
        foreach (var polygon in _polygons)
        {
            if (polygon.Count == 0)
                continue;

            if (!IsInsideRing(longitude, latitude, polygon[0]))
                continue;

            var insideHole = false;
            for (var i = 1; i < polygon.Count; i++)
            {
                if (IsInsideRing(longitude, latitude, polygon[i]))
                {
                    insideHole = true;
                    break;
                }
            }

            if (!insideHole)
                return true;
        }

        return false;
    }

    private static void CollectPolygons(
        JsonElement element,
        List<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>> output)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (!element.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var type = typeElement.GetString();
        if (string.Equals(type, "FeatureCollection", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("features", out var features) &&
            features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
                CollectPolygons(feature, output);
            return;
        }

        if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("geometry", out var geometry))
        {
            CollectPolygons(geometry, output);
            return;
        }

        if (!element.TryGetProperty("coordinates", out var coordinates))
            return;

        if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
        {
            var polygon = ParsePolygon(coordinates);
            if (polygon.Count > 0)
                output.Add(polygon);
            return;
        }

        if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
            coordinates.ValueKind == JsonValueKind.Array)
        {
            foreach (var polygonElement in coordinates.EnumerateArray())
            {
                var polygon = ParsePolygon(polygonElement);
                if (polygon.Count > 0)
                    output.Add(polygon);
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> ParsePolygon(JsonElement polygonElement)
    {
        var rings = new List<IReadOnlyList<(double Lon, double Lat)>>();
        if (polygonElement.ValueKind != JsonValueKind.Array)
            return rings;

        foreach (var ringElement in polygonElement.EnumerateArray())
        {
            var ring = ParseRing(ringElement);
            if (ring.Count >= 4)
                rings.Add(ring);
        }

        return rings;
    }

    private static IReadOnlyList<(double Lon, double Lat)> ParseRing(JsonElement ringElement)
    {
        var ring = new List<(double Lon, double Lat)>();
        if (ringElement.ValueKind != JsonValueKind.Array)
            return ring;

        foreach (var pointElement in ringElement.EnumerateArray())
        {
            if (TryReadPosition(pointElement, out var lon, out var lat))
                ring.Add((lon, lat));
        }

        return ring;
    }

    private static bool IsInsideRing(
        double lon,
        double lat,
        IReadOnlyList<(double Lon, double Lat)> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            var intersects = ((pi.Lat > lat) != (pj.Lat > lat)) &&
                             (lon < (pj.Lon - pi.Lon) * (lat - pi.Lat) / ((pj.Lat - pi.Lat) + double.Epsilon) + pi.Lon);
            if (intersects)
                inside = !inside;
        }

        return inside;
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

        return TryReadDouble(values[0], out lon) &&
               TryReadDouble(values[1], out lat) &&
               lon >= -180 &&
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
