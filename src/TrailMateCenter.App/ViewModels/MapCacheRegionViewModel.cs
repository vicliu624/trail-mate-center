using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
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

            return parts.Count == 0
                ? "No layers selected"
                : string.Join(" / ", parts);
        }
    }

    public bool CacheNeedsMaintenance => CacheExpectedTiles > 0 && CacheExistingTiles < CacheExpectedTiles;
    public string ZoomRangeText => $"Z{MinimumZoom}-Z{MaximumZoom}";

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
                         MaximumZoom != normalized.MaximumZoom;

        IncludeOsm = normalized.IncludeOsm;
        IncludeTerrain = normalized.IncludeTerrain;
        IncludeSatellite = normalized.IncludeSatellite;
        IncludeContours = normalized.IncludeContours;
        IncludeUltraFineContours = normalized.IncludeUltraFineContours;
        MinimumZoom = normalized.MinimumZoom;
        MaximumZoom = normalized.MaximumZoom;

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
}
