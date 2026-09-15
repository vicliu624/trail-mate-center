using System.Reflection;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class AnnotationDialogPlanTests
{
    [Fact]
    public void MapBuilderAndCacheDialogPassTheSamePolicyThroughRegionPersistence()
    {
        var builder = new MapPackBuilderViewModel(new MapViewModel());
        builder.AnnotationOptions.Preset = MapAnnotationPreset.Urban;
        builder.AnnotationOptions.PoisEnabled = false;
        builder.AnnotationOptions.RoadMinimumZoom = 12;
        var plan = builder.BuildPlan();
        Assert.NotNull(plan.Annotations);
        Assert.Equal(plan.Annotations, builder.ToOfflineCacheBuildOptions().Annotations);
        var create = typeof(MainWindowViewModel).GetMethod("CreateMapCacheRegionSettings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settings = (MapCacheRegionSettings)create.Invoke(null, [plan])!;
        Assert.Equal(plan.Annotations, settings.Annotations);
        var restored = MapCacheRegionViewModel.FromSettings(settings);
        var dialog = new OfflineCacheDialogViewModel();
        dialog.AnnotationOptions.Load(restored.ToBuildOptions().Annotations);
        Assert.Equal(plan.Annotations, dialog.ToBuildOptions().Annotations);
        Assert.True(dialog.ToBuildOptions().Annotations!.RoadsEnabled);
        Assert.False(dialog.ToBuildOptions().Annotations!.PoisEnabled);
    }

    [Fact]
    public void NewModeStartsAtZoomOneAndLegacyKeepsZoomZero()
    {
        Assert.Equal(1, new OfflineCacheBuildOptions { MinimumZoom = 0, Annotations = new() }.Normalize().MinimumZoom);
        Assert.Equal(0, new OfflineCacheBuildOptions { MinimumZoom = 0 }.Normalize().MinimumZoom);
        var dialog = new OfflineCacheDialogViewModel();
        dialog.AnnotationOptions.Load(null);
        Assert.Null(dialog.ToBuildOptions().Annotations);
    }
}
