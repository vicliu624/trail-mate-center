using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class MapCacheRegionViewModel : ObservableObject
{
    private const string ExportStateNone = "none";
    private const string ExportStateExporting = "exporting";
    private const string ExportStateCompleted = "completed";
    private const string ExportStatePartial = "partial";
    private const string ExportStateFailed = "failed";
    private const string ExportStateCanceled = "canceled";

    private static string T(string key) => LocalizationService.Instance.GetString(key);
    private static string F(string key, params object[] args) => LocalizationService.Instance.Format(key, args);

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
    private int? _adminLevel;

    [ObservableProperty]
    private string _boundaryGeoJson = string.Empty;

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
    private string _poiSourceProvider = "local";

    [ObservableProperty]
    private string _poiSourceDownloadUrl = string.Empty;

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

    [ObservableProperty]
    private string _exportOutputDirectory = string.Empty;

    [ObservableProperty]
    private string _exportState = ExportStateNone;

    [ObservableProperty]
    private long _exportProcessedTiles;

    [ObservableProperty]
    private long _exportExpectedTiles;

    [ObservableProperty]
    private long _exportSourceTiles;

    [ObservableProperty]
    private long _exportCopiedTiles;

    [ObservableProperty]
    private long _exportSkippedTiles;

    [ObservableProperty]
    private long _exportMissingTiles;

    [ObservableProperty]
    private long _exportUnreadableEntries;

    [ObservableProperty]
    private string _exportLastError = string.Empty;

    [ObservableProperty]
    private long _exportUpdatedAtUnixTime;

    public string BoundsText =>
        string.Format(
            CultureInfo.InvariantCulture,
            "W {0:F4}, S {1:F4}, E {2:F4}, N {3:F4}",
            West,
            South,
            East,
            North);
    public string SelectionShapeText => string.IsNullOrWhiteSpace(BoundaryGeoJson)
        ? T("Ui.Dashboard.OfflineCacheRegionsDialog.Shape.Rectangle")
        : T("Ui.Dashboard.OfflineCacheRegionsDialog.Shape.Boundary");

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
    public bool HasExportTask =>
        !string.IsNullOrWhiteSpace(ExportOutputDirectory) ||
        !string.Equals(NormalizeExportState(ExportState), ExportStateNone, StringComparison.OrdinalIgnoreCase);
    public bool CanResumeExport
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExportOutputDirectory))
                return false;

            if (NeedsPlaceSearchBackfill)
                return true;

            return NormalizeExportState(ExportState) is ExportStateExporting or
                ExportStatePartial or
                ExportStateFailed or
                ExportStateCanceled;
        }
    }

    public bool NeedsPlaceSearchBackfill =>
        HasPlaceSearchSourceReference &&
        !HasPlaceSearchPack(ExportOutputDirectory);

    private bool HasPlaceSearchSourceReference =>
        !string.IsNullOrWhiteSpace(PoiPbfPath) ||
        !string.IsNullOrWhiteSpace(PoiSourceDownloadUrl);

    public string ExportTaskText
    {
        get
        {
            if (NeedsPlaceSearchBackfill)
                return T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Partial");

            var state = NormalizeExportState(ExportState);
            return state switch
            {
                ExportStateExporting => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Exporting"),
                ExportStateCompleted => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Completed"),
                ExportStatePartial => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Partial"),
                ExportStateFailed => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Failed"),
                ExportStateCanceled => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Canceled"),
                _ => T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Ready"),
            };
        }
    }

    public string ExportTaskDetailText
    {
        get
        {
            if (!HasExportTask)
                return string.Empty;

            var destination = string.IsNullOrWhiteSpace(ExportOutputDirectory)
                ? T("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.NoDestination")
                : ExportOutputDirectory.Trim();
            var expected = Math.Max(0, ExportExpectedTiles);
            var processed = Math.Max(0, ExportProcessedTiles);
            var source = Math.Max(0, ExportSourceTiles);
            var missing = Math.Max(0, ExportMissingTiles);
            var copied = Math.Max(0, ExportCopiedTiles);
            var skipped = Math.Max(0, ExportSkippedTiles);
            var detail = string.Equals(NormalizeExportState(ExportState), ExportStateExporting, StringComparison.OrdinalIgnoreCase) && expected > 0
                ? F(
                    "Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.DetailRunning",
                    destination,
                    processed,
                    expected,
                    copied,
                    skipped)
                : expected > 0
                ? F(
                    "Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.DetailWithStats",
                    destination,
                    source,
                    expected,
                    missing,
                    copied,
                    skipped)
                : F("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Detail", destination);

            if (!string.IsNullOrWhiteSpace(ExportLastError))
            {
                detail = $"{detail} {F("Ui.Dashboard.OfflineCacheRegionsDialog.ExportTask.Error", ExportLastError.Trim())}";
            }

            return detail;
        }
    }

    public string ExportTaskColor
    {
        get
        {
            return NormalizeExportState(ExportState) switch
            {
                ExportStateExporting => "#7CC5FF",
                ExportStateCompleted => "#75E0A2",
                ExportStatePartial => "#FFCF48",
                ExportStateFailed => "#FF7373",
                ExportStateCanceled => "#9AA3AE",
                _ => "#9AA3AE",
            };
        }
    }
    public double ExportTaskPercent => ExportExpectedTiles <= 0
        ? 0
        : Math.Clamp((double)GetExportProgressNumerator() / ExportExpectedTiles * 100.0, 0, 100);

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
            AdminLevel = AdminLevel,
            BoundaryGeoJson = BoundaryGeoJson ?? string.Empty,
            IncludeOsm = options.IncludeOsm,
            IncludeTerrain = options.IncludeTerrain,
            IncludeSatellite = options.IncludeSatellite,
            IncludeContours = options.IncludeContours,
            IncludeUltraFineContours = options.IncludeUltraFineContours,
            MinimumZoom = options.MinimumZoom,
            MaximumZoom = options.MaximumZoom,
            EnablePoiSeparation = options.EnablePoiSeparation,
            PoiPbfPath = options.PoiPbfPath,
            PoiSourceProvider = string.IsNullOrWhiteSpace(PoiSourceProvider) ? "local" : PoiSourceProvider.Trim(),
            PoiSourceDownloadUrl = PoiSourceDownloadUrl ?? string.Empty,
            GenerateFullPoisJsonl = options.GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = options.GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = options.PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = options.PoiIndexMaximumZoom,
            MaxPoiPerTile = options.MaxPoiPerTile,
            IncludePoiLabels = options.IncludePoiLabels,
            IncludeOriginalOsmTags = options.IncludeOriginalOsmTags,
            PoiOutputFormat = options.PoiOutputFormat.ToString().ToLowerInvariant(),
            SelectedPoiTypes = options.SelectedPoiTypes.ToArray(),
            ExportOutputDirectory = ExportOutputDirectory ?? string.Empty,
            ExportState = NormalizeExportState(ExportState),
            ExportProcessedTiles = Math.Max(0, ExportProcessedTiles),
            ExportExpectedTiles = Math.Max(0, ExportExpectedTiles),
            ExportSourceTiles = Math.Max(0, ExportSourceTiles),
            ExportCopiedTiles = Math.Max(0, ExportCopiedTiles),
            ExportSkippedTiles = Math.Max(0, ExportSkippedTiles),
            ExportMissingTiles = Math.Max(0, ExportMissingTiles),
            ExportUnreadableEntries = Math.Max(0, ExportUnreadableEntries),
            ExportLastError = ExportLastError ?? string.Empty,
            ExportUpdatedAtUnixTime = Math.Max(0, ExportUpdatedAtUnixTime),
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
            AdminLevel = settings.AdminLevel,
            BoundaryGeoJson = settings.BoundaryGeoJson ?? string.Empty,
            IncludeOsm = options.IncludeOsm,
            IncludeTerrain = options.IncludeTerrain,
            IncludeSatellite = options.IncludeSatellite,
            IncludeContours = options.IncludeContours,
            IncludeUltraFineContours = options.IncludeUltraFineContours,
            MinimumZoom = options.MinimumZoom,
            MaximumZoom = options.MaximumZoom,
            EnablePoiSeparation = options.EnablePoiSeparation,
            PoiPbfPath = options.PoiPbfPath,
            PoiSourceProvider = string.IsNullOrWhiteSpace(settings.PoiSourceProvider) ? "local" : settings.PoiSourceProvider.Trim(),
            PoiSourceDownloadUrl = settings.PoiSourceDownloadUrl ?? string.Empty,
            GenerateFullPoisJsonl = options.GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = options.GenerateTileIndexedPoiFiles,
            PoiIndexMinimumZoom = options.PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = options.PoiIndexMaximumZoom,
            MaxPoiPerTile = options.MaxPoiPerTile,
            IncludePoiLabels = options.IncludePoiLabels,
            IncludeOriginalOsmTags = options.IncludeOriginalOsmTags,
            PoiOutputFormat = options.PoiOutputFormat,
            SelectedPoiTypes = options.SelectedPoiTypes.ToArray(),
            ExportOutputDirectory = settings.ExportOutputDirectory ?? string.Empty,
            ExportState = NormalizeExportState(settings.ExportState),
            ExportProcessedTiles = Math.Max(0, settings.ExportProcessedTiles),
            ExportExpectedTiles = Math.Max(0, settings.ExportExpectedTiles),
            ExportSourceTiles = Math.Max(0, settings.ExportSourceTiles),
            ExportCopiedTiles = Math.Max(0, settings.ExportCopiedTiles),
            ExportSkippedTiles = Math.Max(0, settings.ExportSkippedTiles),
            ExportMissingTiles = Math.Max(0, settings.ExportMissingTiles),
            ExportUnreadableEntries = Math.Max(0, settings.ExportUnreadableEntries),
            ExportLastError = settings.ExportLastError ?? string.Empty,
            ExportUpdatedAtUnixTime = Math.Max(0, settings.ExportUpdatedAtUnixTime),
        };
    }

    public void ApplySettings(MapCacheRegionSettings settings)
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

        if (string.IsNullOrWhiteSpace(Id))
            Id = string.IsNullOrWhiteSpace(settings.Id) ? Guid.NewGuid().ToString("N") : settings.Id.Trim();
        Name = string.IsNullOrWhiteSpace(settings.Name) ? Name : settings.Name.Trim();
        UpdateBounds((settings.West, settings.South, settings.East, settings.North));
        AdminLevel = settings.AdminLevel;
        BoundaryGeoJson = settings.BoundaryGeoJson ?? string.Empty;
        ApplyBuildOptions(options);
        PoiSourceProvider = string.IsNullOrWhiteSpace(settings.PoiSourceProvider) ? "local" : settings.PoiSourceProvider.Trim();
        PoiSourceDownloadUrl = settings.PoiSourceDownloadUrl ?? string.Empty;
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

    public void MarkExportStarted(string outputDirectory)
    {
        ExportOutputDirectory = outputDirectory?.Trim() ?? string.Empty;
        ExportState = ExportStateExporting;
        ExportProcessedTiles = 0;
        ExportExpectedTiles = 0;
        ExportSourceTiles = 0;
        ExportCopiedTiles = 0;
        ExportSkippedTiles = 0;
        ExportMissingTiles = 0;
        ExportUnreadableEntries = 0;
        ExportLastError = string.Empty;
        TouchExportTask();
    }

    public void ApplyExportProgress(long processedTiles, long expectedTiles, long copiedTiles, long skippedTiles)
    {
        ExportState = ExportStateExporting;
        ExportProcessedTiles = Math.Max(ExportProcessedTiles, processedTiles);
        ExportExpectedTiles = Math.Max(ExportExpectedTiles, expectedTiles);
        ExportCopiedTiles = Math.Max(0, copiedTiles);
        ExportSkippedTiles = Math.Max(0, skippedTiles);
        TouchExportTask();
    }

    public void ApplyExportResult(
        bool success,
        long expectedTiles,
        long sourceTiles,
        long copiedTiles,
        long skippedTiles,
        long unreadableEntries,
        string? errorMessage)
    {
        if (success || expectedTiles > 0)
            ExportExpectedTiles = Math.Max(0, expectedTiles);
        ExportProcessedTiles = success
            ? ExportExpectedTiles
            : ExportExpectedTiles > 0 ? Math.Clamp(ExportProcessedTiles, 0, ExportExpectedTiles) : ExportProcessedTiles;
        if (success || sourceTiles > 0)
            ExportSourceTiles = Math.Max(0, sourceTiles);
        if (success || copiedTiles > 0)
            ExportCopiedTiles = Math.Max(0, copiedTiles);
        if (success || skippedTiles > 0)
            ExportSkippedTiles = Math.Max(0, skippedTiles);
        ExportMissingTiles = ExportExpectedTiles > 0
            ? Math.Max(0, ExportExpectedTiles - ExportSourceTiles)
            : ExportMissingTiles;
        if (success || unreadableEntries > 0)
            ExportUnreadableEntries = Math.Max(0, unreadableEntries);
        ExportLastError = errorMessage ?? string.Empty;
        ExportState = success
            ? ExportMissingTiles > 0 || ExportUnreadableEntries > 0 ? ExportStatePartial : ExportStateCompleted
            : ExportStateFailed;
        TouchExportTask();
    }

    public void MarkExportCanceled()
    {
        ExportState = ExportStateCanceled;
        ExportLastError = string.Empty;
        TouchExportTask();
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
    partial void OnBoundaryGeoJsonChanged(string value) => OnPropertyChanged(nameof(SelectionShapeText));
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

    partial void OnPoiPbfPathChanged(string value) => NotifyExportTaskChanged();
    partial void OnPoiSourceDownloadUrlChanged(string value) => NotifyExportTaskChanged();

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
    partial void OnExportOutputDirectoryChanged(string value) => NotifyExportTaskChanged();
    partial void OnExportStateChanged(string value) => NotifyExportTaskChanged();
    partial void OnExportProcessedTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportExpectedTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportSourceTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportCopiedTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportSkippedTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportMissingTilesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportUnreadableEntriesChanged(long value) => NotifyExportTaskChanged();
    partial void OnExportLastErrorChanged(string value) => NotifyExportTaskChanged();
    partial void OnExportUpdatedAtUnixTimeChanged(long value) => NotifyExportTaskChanged();

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

    private void TouchExportTask()
    {
        ExportUpdatedAtUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        NotifyExportTaskChanged();
    }

    private void NotifyExportTaskChanged()
    {
        OnPropertyChanged(nameof(HasExportTask));
        OnPropertyChanged(nameof(CanResumeExport));
        OnPropertyChanged(nameof(NeedsPlaceSearchBackfill));
        OnPropertyChanged(nameof(ExportTaskText));
        OnPropertyChanged(nameof(ExportTaskDetailText));
        OnPropertyChanged(nameof(ExportTaskColor));
        OnPropertyChanged(nameof(ExportTaskPercent));
    }

    private long GetExportProgressNumerator()
    {
        var state = NormalizeExportState(ExportState);
        if (state == ExportStateExporting)
            return Math.Max(0, ExportProcessedTiles);

        if (state is ExportStateCompleted or ExportStatePartial)
            return Math.Max(0, ExportSourceTiles);

        return Math.Max(Math.Max(0, ExportProcessedTiles), Math.Max(0, ExportSourceTiles));
    }

    private static string NormalizeExportState(string? state)
    {
        return state?.Trim().ToLowerInvariant() switch
        {
            ExportStateExporting => ExportStateExporting,
            ExportStateCompleted => ExportStateCompleted,
            ExportStatePartial => ExportStatePartial,
            ExportStateFailed => ExportStateFailed,
            ExportStateCanceled => ExportStateCanceled,
            _ => ExportStateNone,
        };
    }

    private static bool HasPlaceSearchPack(string? exportOutputDirectory)
    {
        if (string.IsNullOrWhiteSpace(exportOutputDirectory))
            return false;

        var root = exportOutputDirectory.Trim();
        if (string.Equals(Path.GetFileName(root), "maps", StringComparison.OrdinalIgnoreCase))
        {
            root = Path.GetDirectoryName(root) ?? root;
        }

        var placesRoot = Path.Combine(root, "places");
        if (!Directory.Exists(placesRoot))
            return false;

        var packsRoot = Path.Combine(placesRoot, "packs");
        if (!Directory.Exists(packsRoot))
            return false;

        try
        {
            return Directory.EnumerateDirectories(packsRoot)
                .Where(static path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
                .Any(static path =>
                    File.Exists(Path.Combine(path, "manifest.json")) &&
                    File.Exists(Path.Combine(path, "places.bin")) &&
                    File.Exists(Path.Combine(path, "names.bin")) &&
                    File.Exists(Path.Combine(path, "licenses.json")));
        }
        catch
        {
            return false;
        }
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
