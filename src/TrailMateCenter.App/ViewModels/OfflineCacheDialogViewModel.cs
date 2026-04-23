using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace TrailMateCenter.ViewModels;

public enum OfflineCacheDialogAction
{
    Cancel = 0,
    SaveOnly = 1,
    Build = 2,
}

public sealed record OfflineCacheDialogResult(
    OfflineCacheDialogAction Action,
    string RegionName,
    bool SaveRegion,
    OfflineCacheBuildOptions BuildOptions);

public sealed partial class OfflineCacheDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyList<int> ZoomLevels =
        Enumerable.Range(
            OfflineCacheBuildOptions.DefaultMinimumZoom,
            OfflineCacheBuildOptions.DefaultMaximumZoom - OfflineCacheBuildOptions.DefaultMinimumZoom + 1)
        .ToArray();

    [ObservableProperty]
    private string _regionName = string.Empty;

    [ObservableProperty]
    private bool _saveRegion = true;

    [ObservableProperty]
    private bool _includeOsm = true;

    [ObservableProperty]
    private bool _includeTerrain = true;

    [ObservableProperty]
    private bool _includeSatellite = true;

    [ObservableProperty]
    private bool _includeContours = true;

    [ObservableProperty]
    private bool _includeUltraFineContours;

    [ObservableProperty]
    private int _minimumZoom = OfflineCacheBuildOptions.DefaultMinimumZoom;

    [ObservableProperty]
    private int _maximumZoom = OfflineCacheBuildOptions.DefaultMaximumZoom;

    public bool HasSelection => IncludeOsm || IncludeTerrain || IncludeSatellite || IncludeContours;
    public bool CanBuild => HasSelection;
    public IReadOnlyList<int> AvailableZoomLevels => ZoomLevels;

    public OfflineCacheBuildOptions ToBuildOptions()
    {
        return new OfflineCacheBuildOptions
        {
            IncludeOsm = IncludeOsm,
            IncludeTerrain = IncludeTerrain,
            IncludeSatellite = IncludeSatellite,
            IncludeContours = IncludeContours,
            IncludeUltraFineContours = IncludeContours && IncludeUltraFineContours,
            MinimumZoom = MinimumZoom,
            MaximumZoom = MaximumZoom,
        }.Normalize();
    }

    partial void OnIncludeOsmChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeTerrainChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeSatelliteChanged(bool value) => OnSelectionChanged();
    partial void OnMinimumZoomChanged(int value)
    {
        var clamped = Math.Clamp(
            value,
            OfflineCacheBuildOptions.DefaultMinimumZoom,
            OfflineCacheBuildOptions.DefaultMaximumZoom);
        if (clamped != value)
        {
            MinimumZoom = clamped;
            return;
        }

        if (MaximumZoom < clamped)
            MaximumZoom = clamped;
    }

    partial void OnMaximumZoomChanged(int value)
    {
        var clamped = Math.Clamp(
            value,
            OfflineCacheBuildOptions.DefaultMinimumZoom,
            OfflineCacheBuildOptions.DefaultMaximumZoom);
        if (clamped != value)
        {
            MaximumZoom = clamped;
            return;
        }

        if (MinimumZoom > clamped)
            MinimumZoom = clamped;
    }

    partial void OnIncludeContoursChanged(bool value)
    {
        if (!value && IncludeUltraFineContours)
            IncludeUltraFineContours = false;

        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanBuild));
    }
}

