using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class AnnotationRegionSettingsTests
{
    [Fact]
    public void RegionSaveLoadAndResumeKeepTheSameAnnotationPolicy()
    {
        var policy = new MapAnnotationOptions { Preset = MapAnnotationPreset.Urban, PoisEnabled = false, RoadZooms = new(12, 18) };
        var settings = new MapCacheRegionSettings { MinimumZoom = 1, MaximumZoom = 18, Annotations = policy };
        var restored = MapCacheRegionViewModel.FromSettings(JsonSerializer.Deserialize<MapCacheRegionSettings>(JsonSerializer.Serialize(settings))!);
        Assert.Equal(policy, restored.ToBuildOptions().Annotations);
        Assert.Equal(policy, restored.ToSettings().Annotations);
        var changed = policy with { PlacesEnabled = false };
        restored.ApplyBuildOptions(restored.ToBuildOptions() with { Annotations = changed });
        Assert.Equal(changed, restored.ToSettings().Annotations);
        restored.ApplySettings(settings);
        Assert.Equal(policy, restored.ToSettings().Annotations);
        restored.ApplySettings(settings with { Annotations = null });
        Assert.Null(restored.ToSettings().Annotations);
    }
}
