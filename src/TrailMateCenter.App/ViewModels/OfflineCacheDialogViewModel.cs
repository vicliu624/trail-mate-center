using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using TrailMateCenter.Maps;

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

public sealed partial class PoiTypeOptionViewModel : ObservableObject
{
    public PoiTypeOptionViewModel(string id, string label, bool isSelected)
    {
        Id = id;
        Label = label;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record PoiTypeOptionDefinition(string Id, string Label, bool Selected);

public static class PoiTypeCatalog
{
    public static readonly IReadOnlyList<PoiTypeOptionDefinition> DefaultTypes =
    [
        new("water", "Water", true),
        new("camp", "Camp", true),
        new("shelter", "Shelter", true),
        new("peak", "Peak", true),
        new("viewpoint", "Viewpoint", true),
        new("trailhead", "Trailhead", true),
        new("parking", "Parking", true),
        new("toilet", "Toilet", true),
        new("ranger", "Ranger / info", false),
        new("emergency", "Emergency", true),
        new("bridge", "Bridge", false),
        new("ford", "Ford", false),
    ];

    public static PoiTypeOptionViewModel CreateOption(PoiTypeOptionDefinition definition)
    {
        return new PoiTypeOptionViewModel(definition.Id, definition.Label, definition.Selected);
    }
}

public sealed partial class OfflineCacheDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyList<int> ZoomLevels =
        Enumerable.Range(
            OfflineCacheBuildOptions.DefaultMinimumZoom,
            OfflineCacheBuildOptions.DefaultMaximumZoom - OfflineCacheBuildOptions.DefaultMinimumZoom + 1)
        .ToArray();

    private static readonly IReadOnlyList<int> PoiZoomLevels = Enumerable.Range(0, 25).ToArray();

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

    [ObservableProperty]
    private bool _enablePoiSeparation;

    [ObservableProperty]
    private string _poiPbfPath = string.Empty;

    [ObservableProperty]
    private bool _generateFullPoisJsonl = true;

    [ObservableProperty]
    private bool _generateTileIndexedPoiFiles = true;

    [ObservableProperty]
    private int _poiIndexMinimumZoom = 10;

    [ObservableProperty]
    private int _poiIndexMaximumZoom = 17;

    [ObservableProperty]
    private int _maxPoiPerTile = 200;

    [ObservableProperty]
    private bool _includePoiLabels = true;

    [ObservableProperty]
    private bool _includeOriginalOsmTags;

    [ObservableProperty]
    private PoiOutputFormat _poiOutputFormat = PoiOutputFormat.Readable;

    public OfflineCacheDialogViewModel()
    {
        foreach (var definition in PoiTypeCatalog.DefaultTypes)
        {
            var option = PoiTypeCatalog.CreateOption(definition);
            option.PropertyChanged += (_, _) => OnSelectionChanged();
            PoiTypes.Add(option);
        }
    }

    public bool HasTileSelection => IncludeOsm || IncludeTerrain || IncludeSatellite || IncludeContours;
    public bool HasSelectedPoiTypes => PoiTypes.Any(static t => t.IsSelected);
    public bool HasPoiSelection => EnablePoiSeparation;
    public bool HasSelection => HasTileSelection || HasPoiSelection;
    public bool CanBuild => HasTileSelection;
    public IReadOnlyList<int> AvailableZoomLevels => ZoomLevels;
    public IReadOnlyList<int> AvailablePoiZoomLevels => PoiZoomLevels;
    public IReadOnlyList<PoiOutputFormat> AvailablePoiOutputFormats { get; } =
        [PoiOutputFormat.Readable, PoiOutputFormat.Compact];
    public List<PoiTypeOptionViewModel> PoiTypes { get; } = new();

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
            EnablePoiSeparation = EnablePoiSeparation,
            PoiPbfPath = PoiPbfPath,
            GenerateFullPoisJsonl = GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = PoiIndexMaximumZoom,
            MaxPoiPerTile = MaxPoiPerTile,
            IncludePoiLabels = IncludePoiLabels,
            IncludeOriginalOsmTags = IncludeOriginalOsmTags,
            PoiOutputFormat = PoiOutputFormat,
            SelectedPoiTypes = PoiTypes
                .Where(static t => t.IsSelected)
                .Select(static t => t.Id)
                .ToArray(),
        }.Normalize();
    }

    partial void OnIncludeOsmChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeTerrainChanged(bool value) => OnSelectionChanged();
    partial void OnIncludeSatelliteChanged(bool value) => OnSelectionChanged();
    partial void OnEnablePoiSeparationChanged(bool value) => OnSelectionChanged();
    partial void OnPoiPbfPathChanged(string value) => OnSelectionChanged();
    partial void OnGenerateFullPoisJsonlChanged(bool value) => OnSelectionChanged();
    partial void OnGenerateTileIndexedPoiFilesChanged(bool value) => OnSelectionChanged();
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

    partial void OnPoiIndexMinimumZoomChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 24);
        if (clamped != value)
        {
            PoiIndexMinimumZoom = clamped;
            return;
        }

        if (PoiIndexMaximumZoom < clamped)
            PoiIndexMaximumZoom = clamped;
    }

    partial void OnPoiIndexMaximumZoomChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 24);
        if (clamped != value)
        {
            PoiIndexMaximumZoom = clamped;
            return;
        }

        if (PoiIndexMinimumZoom > clamped)
            PoiIndexMinimumZoom = clamped;
    }

    partial void OnMaxPoiPerTileChanged(int value)
    {
        if (value < 1)
            MaxPoiPerTile = 1;
    }

    partial void OnIncludeContoursChanged(bool value)
    {
        if (!value && IncludeUltraFineContours)
            IncludeUltraFineContours = false;

        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasTileSelection));
        OnPropertyChanged(nameof(HasSelectedPoiTypes));
        OnPropertyChanged(nameof(HasPoiSelection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanBuild));
    }

}

