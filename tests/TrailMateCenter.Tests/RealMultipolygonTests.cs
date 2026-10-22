using NetTopologySuite.Geometries;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class RealMultipolygonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Center.RealMultipolygons", Guid.NewGuid().ToString("N"));
    private static readonly GeoBounds Bretagne = new(-5.1440329, 47.277755, -1.01569, 49.1133044);

    public RealMultipolygonTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task EtangDuBoisDAmour_OriginalInnerRole_ExportsWaterWithoutOmittingRelation()
    {
        var pbf = Fixture("relation-7815642-v4.osm.pbf");
        using (var store = new PbfBasemapStore(Path.Combine(_root, "lake.sqlite")))
        {
            store.Import(pbf, null, default, Bretagne);
            var water = Assert.Single(store.Query(new Envelope(0, 1, 0, 1), 18).Where(f => f.Kind == "water")).Geometry;
            Assert.True(water.IsValid);
            Assert.False(water.IsEmpty);
            Assert.InRange(water.Area, 1e-13, 1e-11);
            Assert.Equal(0, store.SourceIssueCount);
        }
        var result = await new LocalMapPackExporter().ExportAsync(new MapPackExportPlan
        {
            OutputDirectory = Path.Combine(_root, "export"),
            Area = new MapPackAreaSelection { Name = "Étang du Bois d'Amour", Bounds = new(-2.8445955, 47.8885384, -2.8443522, 47.8887292) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 16, MaximumZoom = 16 },
            Poi = new MapPackPoiSelection { EnablePoiSeparation = true, PbfPath = pbf, SelectedPoiTypes = ["water"] },
            Annotations = new MapAnnotationOptions(),
        });
        Assert.True(result.TileCount > 0);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(result.MapsRoot, "base", "osm"), "*.png", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(result.MapsRoot, "source-geometry-warnings.json")));
    }

    [Fact]
    public void ForetDeLoudeac_V8_StillRejectsIncompleteBoundary()
    {
        using var store = new PbfBasemapStore(Path.Combine(_root, "forest-v8.sqlite"));
        var error = Assert.Throws<InvalidDataException>(() => store.Import(Fixture("relation-6730991-v8.osm.pbf"), null, default, Bretagne));
        Assert.Contains("6730991 has incomplete polygon rings", error.Message);
        Assert.Equal(0, store.SourceIssueCount);
    }

    [Fact]
    public void ForetDeLoudeac_V9_ImportsFiveShellsAndFifteenHolesInStrictMode()
    {
        using var store = new PbfBasemapStore(Path.Combine(_root, "forest-v9.sqlite"));
        store.Import(Fixture("relation-6730991-v9.osm.pbf"), null, default, Bretagne);
        var forest = Assert.Single(store.Query(new Envelope(0, 1, 0, 1), 18).Where(f => f.Kind == "forest")).Geometry;
        Assert.True(forest.IsValid);
        Assert.Equal(5, forest.NumGeometries);
        Assert.Equal(15, Enumerable.Range(0, forest.NumGeometries).Sum(i => ((Polygon)forest.GetGeometryN(i)).NumInteriorRings));
        Assert.Equal(0, store.SourceIssueCount);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", name);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
