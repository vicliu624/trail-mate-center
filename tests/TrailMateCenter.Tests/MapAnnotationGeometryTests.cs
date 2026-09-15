using NetTopologySuite.Geometries;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationGeometryTests
{
    [Fact]
    public void PbfImportPreservesNamedRoadGeometryAndIndependentFacilityIdentity()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("annotations", "人民路");
        using var store = new PbfBasemapStore(Path.Combine(Path.GetDirectoryName(plan.Poi.PbfPath)!, "annotations.sqlite"));
        store.Import(plan.Poi.PbfPath, null, default, plan.Area.Bounds, includeAnnotations: true);
        var annotations = store.QueryAnnotations(new Envelope(0, 1, 0, 1), 16).ToArray();
        Assert.Contains(annotations, a => a.Rule.Kind == MapAnnotationKind.Road && a.Name == "人民路" && a.Geometry is LineString);
        Assert.Contains(annotations, a => a.Rule.Kind == MapAnnotationKind.Poi && a.Rule.Category == "water" && a.Geometry is Point);
        Assert.Empty(store.QueryAnnotations(new Envelope(0, 1, 0, 1), 7));
    }

    [Fact]
    public void LongRoadProvidesGeometryEvenWhenItsGlobalMidpointIsOutsideTheTile()
    {
        var tile = new TileCoordinate(16, 51462, 28055);
        var scale = 256.0 * (1 << tile.Z);
        Coordinate Point(double x, double y) => new((tile.X * 256.0 + x) / scale, (tile.Y * 256.0 + y) / scale);
        var road = new MapAnnotationFeature("way/123", MapAnnotationPolicy.Find(MapAnnotationKind.Road, "residential")!, "人民路", null,
            new GeometryFactory().CreateLineString([Point(-1000, 100), Point(100, 100), Point(300, 110)]));
        var candidates = MapAnnotationCandidateBuilder.Build(road, tile, new()).ToArray();
        var candidate = Assert.Single(candidates);
        Assert.Equal(MapAnnotationKind.Road, candidate.Kind);
        Assert.NotNull(candidate.Path);
        Assert.InRange(candidate.Path.Length, 4, 16);
        Assert.All(candidate.Path, value => Assert.InRange((int)value, -32, 288));
        var adjacent = Assert.Single(MapAnnotationCandidateBuilder.Build(road, tile with { X = tile.X - 1 }, new()));
        Assert.Equal(candidate.FeatureId, adjacent.FeatureId);
        Assert.NotEqual(candidate.Id, adjacent.Id);
        Assert.Equal(candidate.Id, Assert.Single(MapAnnotationCandidateBuilder.Build(road, tile, new())).Id);
    }

    [Fact]
    public void GroupPolicyAppliesBeforeBuildingRoadCandidates()
    {
        var tile = new TileCoordinate(16, 1, 1);
        var bounds = MapAnnotationCandidateBuilder.TileEnvelope(tile, 0);
        var road = new MapAnnotationFeature("way/1", MapAnnotationPolicy.Find(MapAnnotationKind.Road, "residential")!, "街道", null,
            new GeometryFactory().CreateLineString([new(bounds.MinX, bounds.MinY), new(bounds.MaxX, bounds.MaxY)]));
        Assert.Empty(MapAnnotationCandidateBuilder.Build(road, tile, new() { RoadsEnabled = false }));
        Assert.NotEmpty(MapAnnotationCandidateBuilder.Build(road, tile, new() { PoisEnabled = false }));
    }
}
