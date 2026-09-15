using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationOptionsViewModelTests
{
    [Fact]
    public void EditSaveReloadPreservesAllThreeIndependentGroups()
    {
        var editor = new MapAnnotationOptionsViewModel
        {
            Preset = MapAnnotationPreset.Urban, PlacesEnabled = true, RoadsEnabled = true, PoisEnabled = false,
            RoadMinimumZoom = 18, RoadMaximumZoom = 12, PlaceMinimumZoom = 1, PlaceMaximumZoom = 15,
        };
        var saved = editor.ToOptions()!;
        Assert.Equal(new MapAnnotationZoomRange(12, 18), saved.RoadZooms);
        var restored = new MapAnnotationOptionsViewModel();
        var events = 0;
        restored.Changed += () => events++;
        restored.Load(JsonSerializer.Deserialize<MapAnnotationOptions>(JsonSerializer.Serialize(saved)));
        Assert.Equal(saved, restored.ToOptions());
        Assert.Equal(1, events);
        Assert.True(restored.RoadsEnabled);
        Assert.False(restored.PoisEnabled);
    }

    [Fact]
    public void LegacyNullIsNotSilentlyUpgradedAndNewEditorsDefaultToAnnotations()
    {
        var editor = new MapAnnotationOptionsViewModel();
        Assert.NotNull(editor.ToOptions());
        editor.Load(null);
        Assert.False(editor.Enabled);
        Assert.Null(editor.ToOptions());
        editor.Enabled = true;
        Assert.Equal(new MapAnnotationOptions(), editor.ToOptions());
    }
}
