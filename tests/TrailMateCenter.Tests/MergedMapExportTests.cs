using System.Reflection;
using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class MergedMapExportTests
{
    [Fact]
    public void LocalAnnotationExportAlsoProducesTheMainBranchPlaceSearchPack()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("merged-export", "饮水点");
        var region = new MapCacheRegionSettings
        {
            Name = "Merged export",
            West = plan.Area.Bounds.West,
            South = plan.Area.Bounds.South,
            East = plan.Area.Bounds.East,
            North = plan.Area.Bounds.North,
            MinimumZoom = 16,
            MaximumZoom = 16,
            IncludeOsm = true,
            IncludeTerrain = false,
            IncludeSatellite = false,
            IncludeContours = false,
            PoiPbfPath = plan.Poi.PbfPath,
            Annotations = new MapAnnotationOptions(),
        };
        var export = typeof(MainWindowViewModel).GetMethod("ExportOfflineCacheRegion", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (MainWindowViewModel.OfflineCacheRegionExportResult)export.Invoke(null,
            [region, Path.Combine(plan.OutputDirectory, "cache"), plan.OutputDirectory, CancellationToken.None, null])!;
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.PlaceSearchEnabled);
        Assert.True(result.PlaceSearchSuccess, result.PlaceSearchErrorMessage);
        Assert.True(result.PlaceSearchCount > 0);
        Assert.True(Directory.Exists(Path.Combine(plan.OutputDirectory, "places", "packs")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.TargetMapRoot!, "poi", "manifest.json")));
        Assert.Equal(3, manifest.RootElement.GetProperty("version").GetInt32());
        var kinds = Enum.GetValues<MainWindowViewModel.OfflineCacheExportProgressKind>();
        Assert.Equal(kinds.Length, kinds.Distinct().Count());
    }
}
