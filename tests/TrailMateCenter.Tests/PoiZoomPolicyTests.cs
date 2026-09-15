using System.Reflection;
using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class PoiZoomPolicyTests
{
    [Fact]
    public void ExplicitLevelsOverrideLegacyRangeAndEmptyMeansDisabled()
    {
        var sparse = new PoiIndexOptions { MinZoom = 0, MaxZoom = 18, EnabledZoomLevels = [15, 10, 12, 12] }.Normalize();
        Assert.Equal(new[] { 10, 12, 15 }, sparse.EnabledZoomLevels);
        Assert.False(sparse.IsEnabledAtZoom(11));
        var disabled = (sparse with { EnabledZoomLevels = [] }).Normalize();
        Assert.Empty(disabled.EnabledZoomLevels!);
        Assert.False(disabled.IsEnabledAtZoom(0));
        Assert.False(disabled.IsEnabledAtZoom(12));
        Assert.Equal(Enumerable.Range(10, 8), new PoiIndexOptions().Normalize().EnabledZoomLevels);
    }

    [Fact]
    public void SelectionCommandsUseOneLevelSetAndRespectMapBounds()
    {
        var vm = new PoiZoomSelectionViewModel();
        vm.SetBounds(10, 18);
        vm.RangeStart = 12; vm.RangeEnd = 15;
        vm.SelectRangeCommand.Execute(null);
        vm.Levels.Single(l => l.Zoom == 13).IsSelected = false;
        Assert.Equal(new[] { 12, 14, 15 }, vm.EnabledZoomLevels);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.EnabledZoomLevels);
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(Enumerable.Range(10, 9), vm.EnabledZoomLevels);
        vm.SetBounds(12, 16);
        Assert.Equal(Enumerable.Range(12, 5), vm.EnabledZoomLevels);
        Assert.False(vm.Levels.Single(l => l.Zoom == 17).IsAvailable);
        vm.LoadSelection([], 10, 17);
        Assert.Empty(vm.EnabledZoomLevels);
    }

    [Fact]
    public void SaveReloadAndApplyPreserveSparseAndEmptyPolicies()
    {
        var settings = new MapCacheRegionSettings { MinimumZoom = 1, MaximumZoom = 18, PoiEnabledZoomLevels = [10, 12, 15] };
        var restored = MapCacheRegionViewModel.FromSettings(JsonSerializer.Deserialize<MapCacheRegionSettings>(JsonSerializer.Serialize(settings))!);
        Assert.Equal(settings.PoiEnabledZoomLevels, restored.ToSettings().PoiEnabledZoomLevels);
        Assert.Equal(settings.PoiEnabledZoomLevels, restored.ToBuildOptions().ToPoiIndexOptions().EnabledZoomLevels);
        restored.ApplySettings(settings with { PoiEnabledZoomLevels = [] });
        Assert.Empty(restored.ToSettings().PoiEnabledZoomLevels!);
        restored.ApplySettings(settings with { PoiEnabledZoomLevels = null, PoiIndexMinimumZoom = 11, PoiIndexMaximumZoom = 13 });
        Assert.Equal(new[] { 11, 12, 13 }, restored.ToSettings().PoiEnabledZoomLevels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothWritersOnlyCreateSelectedLevelsAndRemoveDisabledStaleIndexes(bool streaming)
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var writer = new PoiIndexWriter();
            var poi = new PoiRecord { Id = "test-1", Type = "water", Name = "水源", Latitude = 25, Longitude = 102.7, Priority = 90 };
            var input = new PoiManifestInput { Index = new PoiIndexOptions { EnabledZoomLevels = [10, 12, 15] }, PoiTypes = ["water"] };
            async Task Write(PoiManifestInput value)
            {
                if (streaming) await writer.WriteStreamingAsync(root, value, async add => await add(poi));
                else await writer.WriteAsync(root, [poi], value);
            }
            await Write(input);
            Assert.Equal(new[] { 10, 12, 15 }, Directory.GetDirectories(Path.Combine(root, "poi", "index")).Select(p => int.Parse(Path.GetFileName(p)!)).Order());
            using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "poi", "manifest.json"))))
            {
                Assert.Equal(2, manifest.RootElement.GetProperty("version").GetInt32());
                Assert.Equal(new[] { 10, 12, 15 }, manifest.RootElement.GetProperty("index").GetProperty("enabled_zoom_levels").EnumerateArray().Select(x => x.GetInt32()));
            }
            await Write(input with { Index = input.Index with { EnabledZoomLevels = [] } });
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "poi"), "*.jsonl", SearchOption.AllDirectories).Where(p => p.Contains($"{Path.DirectorySeparatorChar}index{Path.DirectorySeparatorChar}")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnselectedLevelsBakeChineseTextAndSelectedLevelsKeepRasterTextFree()
    {
        using var fixture = new LocalMapPackTests();
        var source = fixture.Plan("independent-poi", "昆明饮水点");
        var unlabelled = await new TrailMateCenter.Maps.Rendering.LocalMapPackExporter().ExportAsync(source);
        var plan = source with { OutputDirectory = source.OutputDirectory + "-baked", Poi = source.Poi with { IndexOptions = source.Poi.IndexOptions with { EnabledZoomLevels = [] } } };
        var result = await new TrailMateCenter.Maps.Rendering.LocalMapPackExporter().ExportAsync(plan);
        Assert.Equal(1, result.TileCount);
        Assert.Equal(0, result.Poi.TileFilesWritten);
        Assert.True(result.Poi.Success);
        var bakedFile = Assert.Single(Directory.GetFiles(Path.Combine(result.MapsRoot, "base"), "*.png", SearchOption.AllDirectories));
        var cleanFile = Assert.Single(Directory.GetFiles(Path.Combine(unlabelled.MapsRoot, "base"), "*.png", SearchOption.AllDirectories));
        Assert.NotEqual(await File.ReadAllBytesAsync(cleanFile), await File.ReadAllBytesAsync(bakedFile));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.MapsRoot, "map-pack.json")));
        Assert.Equal(new[] { 16 }, manifest.RootElement.GetProperty("labelled_zoom_levels").EnumerateArray().Select(z => z.GetInt32()));
        Assert.Empty(manifest.RootElement.GetProperty("text_free_zoom_levels").EnumerateArray());
        Assert.True(manifest.RootElement.GetProperty("text_in_basemap").GetBoolean());
    }

    [Fact]
    public void ActualMapPreviewHonorsDisabledLevelsAndLabelPolicy()
    {
        var map = new MapViewModel();
        map.SetPoiPreview([new PoiRecord { Id = "water", Type = "water", Priority = 100, Name = "水源", Latitude = 25, Longitude = 102.7 }]);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var zoom = (int)typeof(MapViewModel).GetMethod("GetCurrentZoom", flags)!.Invoke(map, null)!;
        var samples = typeof(MapViewModel).GetField("_poiPreviewSamples", flags)!.GetValue(map);
        var build = typeof(MapViewModel).GetMethod("BuildPoiPreviewFeatures", flags)!;
        Mapsui.IFeature[] Features() => ((IEnumerable<Mapsui.IFeature>)build.Invoke(map, [samples, 100])!).ToArray();
        map.SetPoiPreviewPolicy(new PoiIndexOptions { EnabledZoomLevels = [zoom], IncludeLabels = true });
        Assert.Contains(Assert.Single(Features()).Styles, s => s is Mapsui.Styles.LabelStyle);
        map.SetPoiPreviewPolicy(new PoiIndexOptions { EnabledZoomLevels = [], MinZoom = 0, MaxZoom = 24 });
        Assert.Empty(Features());
        map.SetPoiPreviewPolicy(new PoiIndexOptions { EnabledZoomLevels = [zoom], IncludeLabels = false });
        Assert.DoesNotContain(Assert.Single(Features()).Styles, s => s is Mapsui.Styles.LabelStyle);
    }
}
