using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class MapCacheRegionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "Area";

    [ObservableProperty]
    private double _west;

    [ObservableProperty]
    private double _south;

    [ObservableProperty]
    private double _east;

    [ObservableProperty]
    private double _north;

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

    [ObservableProperty]
    private IReadOnlyList<string> _selectedPoiTypes = Array.Empty<string>();

    [ObservableProperty]
    private bool _isCacheHealthChecking;

    [ObservableProperty]
    private string _cacheHealthText = "Not checked";

    [ObservableProperty]
    private string _cacheHealthDetailText = "Press refresh to inspect local cache.";

    [ObservableProperty]
    private string _cacheHealthColor = "#9AA3AE";

    [ObservableProperty]
    private double _cacheHealthPercent;

    [ObservableProperty]
    private long _cacheExistingTiles;

    [ObservableProperty]
    private long _cacheExpectedTiles;

    [ObservableProperty]
    private string _osmCoverageText = "--";

    [ObservableProperty]
    private string _terrainCoverageText = "--";

    [ObservableProperty]
    private string _satelliteCoverageText = "--";

    [ObservableProperty]
    private string _contourCoverageText = "--";

    [ObservableProperty]
    private string _osmCoverageColor = "#9AA3AE";

    [ObservableProperty]
    private string _terrainCoverageColor = "#9AA3AE";

    [ObservableProperty]
    private string _satelliteCoverageColor = "#9AA3AE";

    [ObservableProperty]
    private string _contourCoverageColor = "#9AA3AE";

    public string BoundsText =>
        string.Format(
            CultureInfo.InvariantCulture,
            "W {0:F4}, S {1:F4}, E {2:F4}, N {3:F4}",
            West,
            South,
            East,
            North);

    public string BuildTargetsText
    {
        get
        {
            var parts = new List<string>(5);
            if (IncludeOsm)
                parts.Add("OSM");
            if (IncludeTerrain)
                parts.Add("Terrain");
            if (IncludeSatellite)
                parts.Add("Satellite");
            if (IncludeContours)
            {
                parts.Add(IncludeUltraFineContours ? "Contours(+5m)" : "Contours");
            }
            if (EnablePoiSeparation)
            {
                parts.Add("POI");
            }

            return parts.Count == 0
                ? "No layers selected"
                : string.Join(" / ", parts);
        }
    }

    public bool CacheNeedsMaintenance => CacheExpectedTiles > 0 && CacheExistingTiles < CacheExpectedTiles;
    public string ZoomRangeText => $"Z{MinimumZoom}-Z{MaximumZoom}";
    public string PoiSummaryText => EnablePoiSeparation
        ? $"POI Z{PoiIndexMinimumZoom}-Z{PoiIndexMaximumZoom}, {SelectedPoiTypes.Count} types"
        : "POI disabled";

    public MapCacheRegionSettings ToSettings()
    {
        var options = ToBuildOptions();
        return new MapCacheRegionSettings
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "Area" : Name.Trim(),
            West = West,
            South = South,
            East = East,
            North = North,
            IncludeOsm = options.IncludeOsm,
            IncludeTerrain = options.IncludeTerrain,
            IncludeSatellite = options.IncludeSatellite,
            IncludeContours = options.IncludeContours,
            IncludeUltraFineContours = options.IncludeUltraFineContours,
            MinimumZoom = options.MinimumZoom,
            MaximumZoom = options.MaximumZoom,
            EnablePoiSeparation = options.EnablePoiSeparation,
            PoiPbfPath = options.PoiPbfPath,
            GenerateFullPoisJsonl = options.GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = options.GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = options.PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = options.PoiIndexMaximumZoom,
            MaxPoiPerTile = options.MaxPoiPerTile,
            IncludePoiLabels = options.IncludePoiLabels,
            IncludeOriginalOsmTags = options.IncludeOriginalOsmTags,
            PoiOutputFormat = options.PoiOutputFormat.ToString().ToLowerInvariant(),
            SelectedPoiTypes = options.SelectedPoiTypes.ToArray(),
        };
    }

    public static MapCacheRegionViewModel FromSettings(MapCacheRegionSettings settings)
    {
        var options = new OfflineCacheBuildOptions
        {
            IncludeOsm = settings.IncludeOsm,
            IncludeTerrain = settings.IncludeTerrain,
            IncludeSatellite = settings.IncludeSatellite,
            IncludeContours = settings.IncludeContours,
            IncludeUltraFineContours = settings.IncludeUltraFineContours,
            MinimumZoom = settings.MinimumZoom,
            MaximumZoom = settings.MaximumZoom,
            EnablePoiSeparation = settings.EnablePoiSeparation,
            PoiPbfPath = settings.PoiPbfPath ?? string.Empty,
            GenerateFullPoisJsonl = settings.GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = settings.GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = settings.PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = settings.PoiIndexMaximumZoom,
            MaxPoiPerTile = settings.MaxPoiPerTile,
            IncludePoiLabels = settings.IncludePoiLabels,
            IncludeOriginalOsmTags = settings.IncludeOriginalOsmTags,
            PoiOutputFormat = ParsePoiOutputFormat(settings.PoiOutputFormat),
            SelectedPoiTypes = NormalizePoiTypes(settings.SelectedPoiTypes),
        }.Normalize();

        return new MapCacheRegionViewModel
        {
            Id = string.IsNullOrWhiteSpace(settings.Id) ? Guid.NewGuid().ToString("N") : settings.Id.Trim(),
            Name = settings.Name ?? string.Empty,
            West = settings.West,
            South = settings.South,
            East = settings.East,
            North = settings.North,
            IncludeOsm = options.IncludeOsm,
            IncludeTerrain = options.IncludeTerrain,
            IncludeSatellite = options.IncludeSatellite,
            IncludeContours = options.IncludeContours,
            IncludeUltraFineContours = options.IncludeUltraFineContours,
            MinimumZoom = options.MinimumZoom,
            MaximumZoom = options.MaximumZoom,
            EnablePoiSeparation = options.EnablePoiSeparation,
            PoiPbfPath = options.PoiPbfPath,
            GenerateFullPoisJsonl = options.GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = options.GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = options.PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = options.PoiIndexMaximumZoom,
            MaxPoiPerTile = options.MaxPoiPerTile,
            IncludePoiLabels = options.IncludePoiLabels,
            IncludeOriginalOsmTags = options.IncludeOriginalOsmTags,
            PoiOutputFormat = options.PoiOutputFormat,
            SelectedPoiTypes = options.SelectedPoiTypes.ToArray(),
        };
    }

    public void ApplyBuildOptions(OfflineCacheBuildOptions options)
    {
        var normalized = options.Normalize();
        var hasChanged = IncludeOsm != normalized.IncludeOsm ||
                         IncludeTerrain != normalized.IncludeTerrain ||
                         IncludeSatellite != normalized.IncludeSatellite ||
                         IncludeContours != normalized.IncludeContours ||
                         IncludeUltraFineContours != normalized.IncludeUltraFineContours ||
                         MinimumZoom != normalized.MinimumZoom ||
                         MaximumZoom != normalized.MaximumZoom ||
                         EnablePoiSeparation != normalized.EnablePoiSeparation ||
                         !string.Equals(PoiPbfPath, normalized.PoiPbfPath, StringComparison.Ordinal) ||
                         GenerateFullPoisJsonl != normalized.GenerateFullPoisJsonl ||
                         GenerateTileIndexedPoiFiles != normalized.GenerateTileIndexedPoiFiles ||
                         PoiIndexMinimumZoom != normalized.PoiIndexMinimumZoom ||
                         PoiIndexMaximumZoom != normalized.PoiIndexMaximumZoom ||
                         MaxPoiPerTile != normalized.MaxPoiPerTile ||
                         IncludePoiLabels != normalized.IncludePoiLabels ||
                         IncludeOriginalOsmTags != normalized.IncludeOriginalOsmTags ||
                         PoiOutputFormat != normalized.PoiOutputFormat ||
                         !SelectedPoiTypes.SequenceEqual(normalized.SelectedPoiTypes, StringComparer.OrdinalIgnoreCase);

        IncludeOsm = normalized.IncludeOsm;
        IncludeTerrain = normalized.IncludeTerrain;
        IncludeSatellite = normalized.IncludeSatellite;
        IncludeContours = normalized.IncludeContours;
        IncludeUltraFineContours = normalized.IncludeUltraFineContours;
        MinimumZoom = normalized.MinimumZoom;
        MaximumZoom = normalized.MaximumZoom;
        EnablePoiSeparation = normalized.EnablePoiSeparation;
        PoiPbfPath = normalized.PoiPbfPath;
        GenerateFullPoisJsonl = normalized.GenerateFullPoisJsonl;
        GenerateTileIndexedPoiFiles = normalized.GenerateTileIndexedPoiFiles;
        PoiIndexMinimumZoom = normalized.PoiIndexMinimumZoom;
        PoiIndexMaximumZoom = normalized.PoiIndexMaximumZoom;
        MaxPoiPerTile = normalized.MaxPoiPerTile;
        IncludePoiLabels = normalized.IncludePoiLabels;
        IncludeOriginalOsmTags = normalized.IncludeOriginalOsmTags;
        PoiOutputFormat = normalized.PoiOutputFormat;
        SelectedPoiTypes = normalized.SelectedPoiTypes.ToArray();

        if (hasChanged)
            ResetCacheHealthSummary();
    }

    public OfflineCacheBuildOptions ToBuildOptions()
    {
        return new OfflineCacheBuildOptions
        {
            IncludeOsm = IncludeOsm,
            IncludeTerrain = IncludeTerrain,
            IncludeSatellite = IncludeSatellite,
            IncludeContours = IncludeContours,
            IncludeUltraFineContours = IncludeUltraFineContours,
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
            SelectedPoiTypes = SelectedPoiTypes.ToArray(),
        }.Normalize();
    }

    public void UpdateBounds((double West, double South, double East, double North) bounds)
    {
        var hasChanged = West != bounds.West ||
                         South != bounds.South ||
                         East != bounds.East ||
                         North != bounds.North;

        West = bounds.West;
        South = bounds.South;
        East = bounds.East;
        North = bounds.North;

        if (hasChanged)
            ResetCacheHealthSummary();
    }

    public void SetCacheHealthChecking(bool checking)
    {
        IsCacheHealthChecking = checking;
        if (checking)
        {
            CacheHealthText = "Inspecting...";
            CacheHealthDetailText = "Scanning local files for this region.";
            CacheHealthColor = "#7CC5FF";
        }
    }

    public void SetCacheHealth(
        long existingTiles,
        long expectedTiles,
        (long Existing, long Expected) osm,
        (long Existing, long Expected) terrain,
        (long Existing, long Expected) satellite,
        (long Existing, long Expected) contour)
    {
        CacheExistingTiles = Math.Max(0, existingTiles);
        CacheExpectedTiles = Math.Max(0, expectedTiles);
        CacheHealthPercent = CacheExpectedTiles <= 0
            ? 0
            : Math.Clamp((double)CacheExistingTiles / CacheExpectedTiles * 100.0, 0, 100);

        var osmLayer = BuildLayerCoverage(osm.Existing, osm.Expected);
        OsmCoverageText = osmLayer.Text;
        OsmCoverageColor = osmLayer.Color;

        var terrainLayer = BuildLayerCoverage(terrain.Existing, terrain.Expected);
        TerrainCoverageText = terrainLayer.Text;
        TerrainCoverageColor = terrainLayer.Color;

        var satelliteLayer = BuildLayerCoverage(satellite.Existing, satellite.Expected);
        SatelliteCoverageText = satelliteLayer.Text;
        SatelliteCoverageColor = satelliteLayer.Color;

        var contourLayer = BuildLayerCoverage(contour.Existing, contour.Expected);
        ContourCoverageText = contourLayer.Text;
        ContourCoverageColor = contourLayer.Color;

        var missing = Math.Max(0, CacheExpectedTiles - CacheExistingTiles);
        if (CacheExpectedTiles <= 0)
        {
            CacheHealthText = "No cache target";
            CacheHealthDetailText = "Select at least one map layer for this region.";
            CacheHealthColor = "#9AA3AE";
        }
        else if (CacheExistingTiles <= 0)
        {
            CacheHealthText = "0.0% (empty)";
            CacheHealthDetailText = $"Missing {missing:N0} tiles. Continue cache to fill.";
            CacheHealthColor = "#FF9E7A";
        }
        else if (CacheExistingTiles >= CacheExpectedTiles)
        {
            CacheHealthText = $"100.0% ({CacheExistingTiles:N0}/{CacheExpectedTiles:N0})";
            CacheHealthDetailText = "Cache looks complete.";
            CacheHealthColor = "#75E0A2";
        }
        else
        {
            CacheHealthText = $"{CacheHealthPercent:F1}% ({CacheExistingTiles:N0}/{CacheExpectedTiles:N0})";
            CacheHealthDetailText = $"Missing {missing:N0} tiles. Continue cache to fill.";
            CacheHealthColor = "#FFCF48";
        }
    }

    partial void OnWestChanged(double value) => OnPropertyChanged(nameof(BoundsText));
    partial void OnSouthChanged(double value) => OnPropertyChanged(nameof(BoundsText));
    partial void OnEastChanged(double value) => OnPropertyChanged(nameof(BoundsText));
    partial void OnNorthChanged(double value) => OnPropertyChanged(nameof(BoundsText));
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

        OnPropertyChanged(nameof(ZoomRangeText));
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

        OnPropertyChanged(nameof(ZoomRangeText));
    }

    partial void OnIncludeOsmChanged(bool value) => OnPropertyChanged(nameof(BuildTargetsText));
    partial void OnIncludeTerrainChanged(bool value) => OnPropertyChanged(nameof(BuildTargetsText));
    partial void OnIncludeSatelliteChanged(bool value) => OnPropertyChanged(nameof(BuildTargetsText));
    partial void OnIncludeContoursChanged(bool value) => OnPropertyChanged(nameof(BuildTargetsText));
    partial void OnIncludeUltraFineContoursChanged(bool value) => OnPropertyChanged(nameof(BuildTargetsText));
    partial void OnEnablePoiSeparationChanged(bool value)
    {
        OnPropertyChanged(nameof(BuildTargetsText));
        OnPropertyChanged(nameof(PoiSummaryText));
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

        OnPropertyChanged(nameof(PoiSummaryText));
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

        OnPropertyChanged(nameof(PoiSummaryText));
    }

    partial void OnSelectedPoiTypesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(PoiSummaryText));

    partial void OnCacheExistingTilesChanged(long value) => OnPropertyChanged(nameof(CacheNeedsMaintenance));
    partial void OnCacheExpectedTilesChanged(long value) => OnPropertyChanged(nameof(CacheNeedsMaintenance));

    private static (string Text, string Color) BuildLayerCoverage(long existing, long expected)
    {
        var safeExisting = Math.Max(0, existing);
        var safeExpected = Math.Max(0, expected);
        if (safeExpected <= 0)
        {
            return ("--", "#9AA3AE");
        }

        var percent = Math.Clamp((double)safeExisting / safeExpected * 100.0, 0, 100);
        var text = $"{percent:F1}% ({safeExisting:N0}/{safeExpected:N0})";
        if (safeExisting <= 0)
            return (text, "#FF9E7A");
        if (safeExisting >= safeExpected)
            return (text, "#75E0A2");
        return (text, "#FFCF48");
    }

    private void ResetCacheHealthSummary()
    {
        IsCacheHealthChecking = false;
        CacheHealthText = "Not checked";
        CacheHealthDetailText = "Press refresh to inspect local cache.";
        CacheHealthColor = "#9AA3AE";
        CacheHealthPercent = 0;
        CacheExistingTiles = 0;
        CacheExpectedTiles = 0;
        OsmCoverageText = "--";
        TerrainCoverageText = "--";
        SatelliteCoverageText = "--";
        ContourCoverageText = "--";
        OsmCoverageColor = "#9AA3AE";
        TerrainCoverageColor = "#9AA3AE";
        SatelliteCoverageColor = "#9AA3AE";
        ContourCoverageColor = "#9AA3AE";
    }

    private static PoiOutputFormat ParsePoiOutputFormat(string? value)
    {
        return string.Equals(value, "compact", StringComparison.OrdinalIgnoreCase)
            ? PoiOutputFormat.Compact
            : PoiOutputFormat.Readable;
    }

    private static IReadOnlyList<string> NormalizePoiTypes(IEnumerable<string>? types)
    {
        return (types ?? Array.Empty<string>())
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
