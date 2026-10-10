using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Build-time polygon coverage. Retains boundary geometry, never a list of all tiles.</summary>
public sealed class TmapTileCoverage
{
    private readonly GeometryFactory _factory = new(new PrecisionModel(), 4326);
    private readonly IPreparedGeometry? _boundary;
    public TmapTileCoverage(MapPackAreaSelection area)
    {
        if (string.IsNullOrWhiteSpace(area.BoundaryGeoJson)) return;
        using var document = JsonDocument.Parse(area.BoundaryGeoJson);
        var polygons = new List<Polygon>();
        Collect(document.RootElement, polygons);
        if (polygons.Count == 0) throw new InvalidDataException("The selected boundary contains no polygons.");
        var geometry = _factory.CreateMultiPolygon(polygons.ToArray());
        if (!geometry.IsValid) throw new InvalidDataException("The selected administrative boundary is invalid.");
        var e = geometry.EnvelopeInternal;
        if (e.MinX < area.Bounds.West || e.MaxX > area.Bounds.East || e.MinY < area.Bounds.South || e.MaxY > area.Bounds.North)
            throw new InvalidDataException("The export bounds must contain the entire administrative boundary.");
        _boundary = PreparedGeometryFactory.Prepare(geometry);
    }
    public bool Includes(int z, int x, int y)
    {
        if (_boundary is null) return true;
        var b = TileMath.TileToBounds(x, y, z);
        return _boundary.Intersects(_factory.ToGeometry(new Envelope(b.West, b.East, b.South, b.North)));
    }
    public long Count(GeoBounds bounds, int minimumZoom, int maximumZoom, CancellationToken token = default)
    {
        if (_boundary is null) return ExportEstimator.CountTiles(bounds, minimumZoom, maximumZoom);
        long count = 0;
        for (var z = minimumZoom; z <= maximumZoom; z++)
        {
            var range = TileMath.BoundsToTileRange(bounds, z);
            for (var x = range.MinX; x <= range.MaxX; x++)
            {
                token.ThrowIfCancellationRequested();
                for (var y = range.MinY; y <= range.MaxY; y++) if (Includes(z, x, y)) count++;
            }
        }
        return count;
    }
    private void Collect(JsonElement value, List<Polygon> polygons)
    {
        switch (value.GetProperty("type").GetString())
        {
            case "FeatureCollection":
                foreach (var feature in value.GetProperty("features").EnumerateArray()) Collect(feature, polygons);
                break;
            case "Feature": Collect(value.GetProperty("geometry"), polygons); break;
            case "Polygon": polygons.Add(Polygon(value.GetProperty("coordinates"))); break;
            case "MultiPolygon":
                foreach (var polygon in value.GetProperty("coordinates").EnumerateArray()) polygons.Add(Polygon(polygon));
                break;
            default: throw new InvalidDataException("Export boundaries must contain only Polygon or MultiPolygon geometry.");
        }
    }
    private Polygon Polygon(JsonElement value)
    {
        var rings = value.EnumerateArray().Select(r =>
        {
            var points = r.EnumerateArray().Select(p =>
            {
                var lon = p[0].GetDouble(); var lat = p[1].GetDouble();
                if (!double.IsFinite(lon) || !double.IsFinite(lat) || lon is < -180 or > 180 || lat is < -85.05112878 or > 85.05112878)
                    throw new InvalidDataException("Export boundary requires valid Web Mercator latitude and WGS84 longitude.");
                return new Coordinate(lon, lat);
            }).ToArray();
            if (points.Length < 4 || !points[0].Equals2D(points[^1]) ||
                points.Zip(points.Skip(1)).Any(p => Math.Abs(p.First.X - p.Second.X) > 180))
                throw new InvalidDataException("Close boundary rings and split antimeridian crossings before export.");
            return _factory.CreateLinearRing(points);
        }).ToArray();
        if (rings.Length == 0) throw new InvalidDataException("Empty administrative polygon.");
        return _factory.CreatePolygon(rings[0], rings.Skip(1).ToArray());
    }
}
