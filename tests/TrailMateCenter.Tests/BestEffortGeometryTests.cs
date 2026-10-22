using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class BestEffortGeometryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Center.BestEffort", Guid.NewGuid().ToString("N"));
    public BestEffortGeometryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task OriginalV8_DefaultExportCompletesAndReportsArtificialClosingEdges()
    {
        var plan = new MapPackExportPlan
        {
            OutputDirectory = _root,
            Area = new MapPackAreaSelection { Name = "Forêt de Loudéac", Bounds = new(-2.681, 48.239, -2.679, 48.241) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 12, MaximumZoom = 12 },
            Poi = new MapPackPoiSelection
            {
                EnablePoiSeparation = true,
                PbfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", "relation-6730991-v8.osm.pbf"),
                SelectedPoiTypes = ["water"]
            },
            Annotations = new MapAnnotationOptions(),
        };
        var result = await new LocalMapPackExporter().ExportAsync(plan);
        Assert.True(result.TileCount > 0);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(result.MapsRoot, "base", "osm"), "*.png", SearchOption.AllDirectories));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.MapsRoot, "source-geometry-report.json")));
        Assert.False(report.RootElement.GetProperty("strict_geometry").GetBoolean());
        Assert.Equal(1, report.RootElement.GetProperty("repaired_relations").GetInt32());
        var repair = report.RootElement.GetProperty("repairs")[0];
        Assert.Equal(6730991, repair.GetProperty("RelationId").GetInt64());
        var lengths = repair.GetProperty("AddedEdges").EnumerateArray().Select(edge => edge.GetProperty("LengthMeters").GetDouble()).Order().ToArray();
        Assert.Equal(2, lengths.Length);
        Assert.InRange(lengths[0], 570, 590);
        Assert.InRange(lengths[1], 3290, 3310);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task StrictExport_RejectsOriginalV8AndPreservesExistingMap()
    {
        var maps = Path.Combine(_root, "maps");
        Directory.CreateDirectory(maps);
        await File.WriteAllTextAsync(Path.Combine(maps, "keep.txt"), "previous map");
        var plan = new MapPackExportPlan
        {
            StrictGeometry = true,
            OutputDirectory = _root,
            Area = new MapPackAreaSelection { Bounds = new(-2.681, 48.239, -2.679, 48.241) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 12, MaximumZoom = 12 },
            Poi = new MapPackPoiSelection
            {
                EnablePoiSeparation = true,
                PbfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", "relation-6730991-v8.osm.pbf"),
                SelectedPoiTypes = ["water"]
            },
            Annotations = new MapAnnotationOptions(),
        };
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new LocalMapPackExporter().ExportAsync(plan));
        Assert.Contains("6730991 has incomplete polygon rings", error.Message);
        Assert.Equal("previous map", await File.ReadAllTextAsync(Path.Combine(maps, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(_root, ".map-pack-*"));
    }

    [Fact]
    public void StrictMode_SurvivesSavedRegionRoundTripAndCanBeDisabled()
    {
        var settings = new TrailMateCenter.Storage.MapCacheRegionSettings { StrictGeometry = true };
        var region = TrailMateCenter.ViewModels.MapCacheRegionViewModel.FromSettings(settings);
        Assert.True(region.ToSettings().StrictGeometry);
        region.ApplySettings(settings with { StrictGeometry = false });
        Assert.False(region.ToSettings().StrictGeometry);
        Assert.False(new MapPackExportPlan().StrictGeometry);
    }
}
