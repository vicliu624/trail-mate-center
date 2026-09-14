using System.Reflection;
using TrailMateCenter.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class LocalMapPackAppIntegrationTests
{
    [Fact]
    public void SharedAppExportEntry_GeneratesLocalTilesWithoutRasterCache()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("app", "水源");
        var bounds = plan.Area.Bounds;
        var region = new MapCacheRegionSettings
        {
            Name = "App export", West = bounds.West, South = bounds.South, East = bounds.East, North = bounds.North,
            IncludeOsm = true, IncludeTerrain = false, IncludeSatellite = false, IncludeContours = false,
            MinimumZoom = 16, MaximumZoom = 16, EnablePoiSeparation = true,
            PoiPbfPath = plan.Poi.PbfPath, SelectedPoiTypes = ["water"], PoiIndexMinimumZoom = 16, PoiIndexMaximumZoom = 16,
        };
        var result = Export(region, Path.Combine(plan.OutputDirectory, "nonexistent-cache"), plan.OutputDirectory);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.CopiedTiles);
        Assert.True(result.PoiSuccess);
        Assert.Equal(0, result.MissingSourceTiles);
        Assert.True(File.Exists(Path.Combine(result.TargetMapRoot, "map-pack.json")));
    }

    [Fact]
    public void SharedAppExportEntry_ReportsPoiFailureInsteadOfSuccessfulPartialMap()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("bad-app", "水源");
        var b = plan.Area.Bounds;
        var region = new MapCacheRegionSettings
        {
            West = b.West, South = b.South, East = b.East, North = b.North,
            IncludeOsm = true, IncludeTerrain = false, IncludeSatellite = false, IncludeContours = false,
            MinimumZoom = 16, MaximumZoom = 16, EnablePoiSeparation = true,
            PoiPbfPath = plan.Poi.PbfPath, SelectedPoiTypes = [],
        };
        var result = Export(region, "unused", plan.OutputDirectory);
        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(plan.OutputDirectory, "maps")));
    }

    [Fact]
    public void SharedAppExportEntry_LegacyRasterExportStillUsesExistingCache()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("legacy", "Water");
        var b = plan.Area.Bounds;
        var cache = Path.Combine(Path.GetDirectoryName(plan.OutputDirectory)!, "cache");
        var path = Path.Combine(cache, "tilecache", "16", "36064");
        Directory.CreateDirectory(path);
        using (var bitmap = new SkiaSharp.SKBitmap(256, 256))
        using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
        using (var output = File.Create(Path.Combine(path, "19335.png"))) data.SaveTo(output);
        var region = new MapCacheRegionSettings
        {
            West = b.West, South = b.South, East = b.East, North = b.North,
            IncludeOsm = true, IncludeTerrain = false, IncludeSatellite = false, IncludeContours = false,
            MinimumZoom = 16, MaximumZoom = 16, EnablePoiSeparation = false,
        };
        var result = Export(region, cache, plan.OutputDirectory);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, result.CopiedTiles);
        Assert.False(result.PoiEnabled);
        Assert.Equal(File.ReadAllBytes(Path.Combine(path, "19335.png")),
            File.ReadAllBytes(Path.Combine(result.TargetMapRoot, "base", "osm", "16", "36064", "19335.png")));
    }

    private static MainWindowViewModel.OfflineCacheRegionExportResult Export(MapCacheRegionSettings region, string cache, string output)
    {
        // Exercise the production entry shared by new packs, saved regions and resume, without starting a UI or network service.
        var entry = typeof(MainWindowViewModel).GetMethod("ExportOfflineCacheRegion", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (MainWindowViewModel.OfflineCacheRegionExportResult)entry.Invoke(null, [region, cache, output, CancellationToken.None, null])!;
    }
}
