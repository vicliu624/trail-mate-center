using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class UnifiedMapPackExportTests
{
    [Fact]
    public async Task PublicExporterPublishesOneTypedIndexWithTextFreeRaster()
    {
        using var fixture = new LocalMapPackTests();
        var legacy = fixture.Plan("legacy-control", "人民路");
        var clean = await new LocalMapPackExporter().ExportAsync(legacy);
        var unified = await new LocalMapPackExporter().ExportAsync(legacy with
        {
            OutputDirectory = legacy.OutputDirectory + "-unified",
            Annotations = new(),
            Poi = legacy.Poi with { IndexOptions = legacy.Poi.IndexOptions with { EnabledZoomLevels = [] } },
        });
        Assert.NotNull(unified.Annotations);
        Assert.Equal(1, unified.Annotations.RoadFeatures);
        Assert.Equal(1, unified.Annotations.PoiFeatures);
        var image = Assert.Single(Directory.GetFiles(Path.Combine(unified.MapsRoot, "base"), "*.png", SearchOption.AllDirectories));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(clean.MapsRoot, Path.GetRelativePath(unified.MapsRoot, image))), await File.ReadAllBytesAsync(image));
        using var pack = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(unified.MapsRoot, "map-pack.json")));
        Assert.Equal(3, pack.RootElement.GetProperty("version").GetInt32());
        Assert.False(pack.RootElement.GetProperty("text_in_basemap").GetBoolean());
        Assert.Empty(pack.RootElement.GetProperty("labelled_zoom_levels").EnumerateArray());
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(unified.MapsRoot, "poi", "manifest.json")));
        Assert.Equal(3, index.RootElement.GetProperty("version").GetInt32());
        Assert.False(File.Exists(Path.Combine(unified.MapsRoot, "poi", "pois.jsonl")));
        Assert.Contains((int)'人', unified.Annotations.RequiredCodepoints);
    }

    [Fact]
    public async Task RoadNamesDoNotDependOnLegacyPoiCategorySelection()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("road-only", "人民路");
        var result = await new LocalMapPackExporter().ExportAsync(plan with
        {
            Annotations = new() { PoisEnabled = false },
            Poi = plan.Poi with { EnablePoiSeparation = false, SelectedPoiTypes = [], GenerateTileIndex = false },
        });
        Assert.NotNull(result.Annotations);
        Assert.Equal(1, result.Annotations.RoadFeatures);
        Assert.Equal(0, result.Annotations.PoiFeatures);
    }

    [Fact]
    public async Task FailureAfterWritingAnnotationsDoesNotReplaceTheExistingMap()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("failed-unified", "人民路");
        var maps = Path.Combine(plan.OutputDirectory, "maps");
        Directory.CreateDirectory(maps);
        await File.WriteAllTextAsync(Path.Combine(maps, "keep.txt"), "existing");
        await Assert.ThrowsAsync<IOException>(() => new LocalMapPackExporter().ExportAsync(plan with
        {
            Annotations = new(),
            BaseLayers = plan.BaseLayers with { IncludeContours = true },
        }, addContours: (_, _) => throw new IOException("contours unavailable")));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(maps, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(maps, "poi")));
    }
}
