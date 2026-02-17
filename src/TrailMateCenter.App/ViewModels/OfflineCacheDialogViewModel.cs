using CommunityToolkit.Mvvm.ComponentModel;

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

    public bool HasSelection => IncludeOsm || IncludeTerrain || IncludeSatellite || IncludeContours;
    public bool CanBuild => HasSelection;

    public OfflineCacheBuildOptions ToBuildOptions()
    {
        return new OfflineCacheBuildOptions
        {
            IncludeOsm = IncludeOsm,
            IncludeTerrain = IncludeTerrain,
            IncludeSatellite = IncludeSatellite,
            IncludeContours = IncludeContours,
            IncludeUltraFineContours = IncludeContours && IncludeUltraFineContours,
        };
    }

    partial void OnIncludeOsmChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeTerrainChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeSatelliteChanged(bool value) => OnSelectionChanged();
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

