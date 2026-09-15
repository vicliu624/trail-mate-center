using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using NetTopologySuite.Simplify;

namespace TrailMateCenter.Maps.Rendering;

internal sealed record MapAnnotationCandidate(
    string Id, string FeatureId, MapAnnotationKind Kind, string Category,
    string Name, int Priority, double Latitude, double Longitude, short[]? Path = null);

/// <summary>Prepares bounded tile-local road geometry; final label placement belongs to the device.</summary>
internal static class MapAnnotationCandidateBuilder
{
    public const int MaximumPathPoints = 8;
    public const int TileMargin = 32;

    public static Envelope TileEnvelope(TileCoordinate tile, int margin = TileMargin)
    {
        var scale = (double)TileMath.TileSize * (1 << tile.Z);
        return new((tile.X * 256.0 - margin) / scale, (tile.X * 256.0 + 256 + margin) / scale,
            (tile.Y * 256.0 - margin) / scale, (tile.Y * 256.0 + 256 + margin) / scale);
    }

    public static IEnumerable<MapAnnotationCandidate> Build(MapAnnotationFeature feature, TileCoordinate tile, MapAnnotationOptions options)
    {
        if (!MapAnnotationPolicy.IsEligible(feature.Rule, tile.Z, options)) yield break;
        var name = feature.Rule.Kind == MapAnnotationKind.Road
            ? MapAnnotationPolicy.RoadText(feature.Name, feature.Reference, tile.Z)
            : feature.Name;
        if (feature.Rule.Kind != MapAnnotationKind.Poi && string.IsNullOrWhiteSpace(name)) yield break;
        var scale = (double)TileMath.TileSize * (1 << tile.Z);
        var featureKey = Key(feature.Id);
        if (feature.Rule.Kind != MapAnnotationKind.Road)
        {
            var anchor = feature.Geometry.InteriorPoint.Coordinate;
            // Point ownership follows the exact XYZ cell, avoiding duplicated
            // facilities in padded neighbour queries. Roads may span many cells.
            if (anchor is null || (int)Math.Floor(anchor.X * (1 << tile.Z)) != tile.X ||
                (int)Math.Floor(anchor.Y * (1 << tile.Z)) != tile.Y) yield break;
            yield return Candidate(featureKey, featureKey, feature, name ?? string.Empty, anchor);
            yield break;
        }

        var clip = feature.Geometry.Factory.ToGeometry(TileEnvelope(tile));
        var clipped = feature.Geometry.Intersection(clip);
        var part = 0;
        foreach (var line in Lines(clipped))
        {
            var simplified = (LineString)DouglasPeuckerSimplifier.Simplify(line, 1.0 / scale);
            if (simplified.IsEmpty || simplified.NumPoints < 2) continue;
            // Split instead of dropping vertices or making the ESP hold a whole road.
            for (var start = 0; start < simplified.NumPoints - 1; start += MaximumPathPoints - 1)
            {
                var count = Math.Min(MaximumPathPoints, simplified.NumPoints - start);
                var coordinates = Enumerable.Range(start, count).Select(simplified.GetCoordinateN).ToArray();
                var segment = feature.Geometry.Factory.CreateLineString(coordinates);
                if (segment.Length * scale < 4) continue;
                var anchor = new LengthIndexedLine(segment).ExtractPoint(segment.Length / 2);
                var path = new short[count * 2];
                for (var i = 0; i < count; i++)
                {
                    path[i * 2] = (short)Math.Clamp(Math.Round(coordinates[i].X * scale - tile.X * 256.0), -TileMargin, 256 + TileMargin);
                    path[i * 2 + 1] = (short)Math.Clamp(Math.Round(coordinates[i].Y * scale - tile.Y * 256.0), -TileMargin, 256 + TileMargin);
                }
                var id = Key($"{feature.Id}@{tile.Z}/{tile.X}/{tile.Y}/{part++}");
                yield return Candidate(id, featureKey, feature, name!, anchor) with { Path = path };
            }
        }
    }

    private static MapAnnotationCandidate Candidate(string id, string featureId, MapAnnotationFeature feature, string name, Coordinate anchor) =>
        new(id, featureId, feature.Rule.Kind, feature.Rule.Category, name, feature.Rule.Priority,
            Math.Atan(Math.Sinh(Math.PI * (1 - 2 * anchor.Y))) * 180 / Math.PI, anchor.X * 360 - 180);

    private static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8));

    private static IEnumerable<LineString> Lines(Geometry geometry)
    {
        if (geometry is LineString line) yield return line;
        else if (geometry is GeometryCollection collection)
            for (var i = 0; i < collection.NumGeometries; i++)
                foreach (var child in Lines(collection.GetGeometryN(i))) yield return child;
    }
}
