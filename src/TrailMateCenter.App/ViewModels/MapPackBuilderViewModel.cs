using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.ViewModels;

public sealed partial class MapPackBuilderViewModel : ObservableObject
{
    private readonly MapViewModel _map;
    private readonly CachedNominatimAdminAreaProvider _adminAreaProvider;
    private readonly GeofabrikCatalogProvider _geofabrikCatalogProvider;
    private readonly GeofabrikPbfDownloadService _pbfDownloadService;
    private readonly GeoJsonBoundaryImporter _boundaryImporter = new();
    private readonly OsmPoiExtractor _poiExtractor = new();
    private readonly ExportEstimator _estimator = new();
    private CancellationTokenSource? _operationCts;

    public MapPackBuilderViewModel(
        MapViewModel map,
        CachedNominatimAdminAreaProvider? adminAreaProvider = null,
        GeofabrikCatalogProvider? geofabrikCatalogProvider = null,
        GeofabrikPbfDownloadService? pbfDownloadService = null)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _adminAreaProvider = adminAreaProvider ?? new CachedNominatimAdminAreaProvider();
        _geofabrikCatalogProvider = geofabrikCatalogProvider ?? new GeofabrikCatalogProvider();
        _pbfDownloadService = pbfDownloadService ?? new GeofabrikPbfDownloadService();

        SearchAdminAreasCommand = new AsyncRelayCommand(SearchAdminAreasAsync, CanRunOperation);
        SearchGeofabrikCommand = new AsyncRelayCommand(SearchGeofabrikAsync, CanRunOperation);
        DownloadSelectedPbfCommand = new AsyncRelayCommand(DownloadSelectedPbfAsync, CanDownloadSelectedPbf);
        ImportBoundaryCommand = new AsyncRelayCommand<string?>(ImportBoundaryAsync, _ => CanRunOperation());
        LoadPoiPreviewCommand = new AsyncRelayCommand(LoadPoiPreviewAsync, CanLoadPoiPreview);
        EstimateCommand = new RelayCommand(UpdateEstimate);
        CancelCommand = new RelayCommand(CancelOperation, () => IsBusy);

        ResetPoiTypes();

        if (_map.TryGetOfflineCacheSelectionBounds(out var bounds))
        {
            ApplyBounds(new GeoBounds(bounds.West, bounds.South, bounds.East, bounds.North), "Current map selection", null, string.Empty);
        }
        else
        {
            ApplyBounds(new GeoBounds(10, 55, 25, 69.5), "Sweden", 2, string.Empty);
        }

        UpdateEstimate();
    }

    public IAsyncRelayCommand SearchAdminAreasCommand { get; }
    public IAsyncRelayCommand SearchGeofabrikCommand { get; }
    public IAsyncRelayCommand DownloadSelectedPbfCommand { get; }
    public IAsyncRelayCommand<string?> ImportBoundaryCommand { get; }
    public IAsyncRelayCommand LoadPoiPreviewCommand { get; }
    public IRelayCommand EstimateCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public ObservableCollection<AdminAreaOptionViewModel> AdminAreas { get; } = new();
    public ObservableCollection<GeofabrikRegionOptionViewModel> GeofabrikRegions { get; } = new();
    public ObservableCollection<PoiTypeOptionViewModel> PoiTypes { get; } = new();

    [ObservableProperty]
    private string _packName = "TrailMate map pack";

    [ObservableProperty]
    private string _areaSearchText = string.Empty;

    [ObservableProperty]
    private string _geofabrikSearchText = string.Empty;

    [ObservableProperty]
    private AdminAreaOptionViewModel? _selectedAdminArea;

    [ObservableProperty]
    private GeofabrikRegionOptionViewModel? _selectedGeofabrikRegion;

    [ObservableProperty]
    private string _areaName = "Selection";

    [ObservableProperty]
    private int? _adminLevel;

    [ObservableProperty]
    private double _west;

    [ObservableProperty]
    private double _south;

    [ObservableProperty]
    private double _east;

    [ObservableProperty]
    private double _north;

    [ObservableProperty]
    private string _boundaryGeoJson = string.Empty;

    [ObservableProperty]
    private bool _includeOsm = true;

    [ObservableProperty]
    private bool _includeTerrain;

    [ObservableProperty]
    private bool _includeSatellite;

    [ObservableProperty]
    private bool _includeContours;

    [ObservableProperty]
    private bool _includeUltraFineContours;

    [ObservableProperty]
    private int _minimumZoom = 10;

    [ObservableProperty]
    private int _maximumZoom = 16;

    [ObservableProperty]
    private bool _enablePoiSeparation = true;

    [ObservableProperty]
    private string _pbfPath = string.Empty;

    [ObservableProperty]
    private string _pbfProvider = "local";

    [ObservableProperty]
    private string _pbfDownloadUrl = string.Empty;

    [ObservableProperty]
    private bool _generateFullPoisJsonl = true;

    [ObservableProperty]
    private bool _generateTileIndex = true;

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
    private bool _showPoiPreview = true;

    [ObservableProperty]
    private int _poiPreviewLimit = 800;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private long _previewPoiCount;

    public IReadOnlyList<int> AvailableZoomLevels { get; } = Enumerable.Range(0, 19).ToArray();
    public IReadOnlyList<int> AvailablePoiZoomLevels { get; } = Enumerable.Range(0, 25).ToArray();
    public IReadOnlyList<PoiOutputFormat> AvailablePoiOutputFormats { get; } =
        [PoiOutputFormat.Readable, PoiOutputFormat.Compact];

    public bool HasSelectedPoiTypes => PoiTypes.Any(static p => p.IsSelected);
    public bool CanExport => !IsBusy && (HasTileSelection || HasPoiExportSelection);
    public bool HasTileSelection => IncludeOsm || IncludeTerrain || IncludeSatellite || IncludeContours;
    public bool HasPoiExportSelection => EnablePoiSeparation && !string.IsNullOrWhiteSpace(PbfPath) && HasSelectedPoiTypes;
    public string BoundsText => CurrentBounds.ToInvariantText();
    public GeoBounds CurrentBounds => new(West, South, East, North);

    public MapPackExportPlan BuildPlan()
    {
        return new MapPackExportPlan
        {
            Name = string.IsNullOrWhiteSpace(PackName) ? AreaName : PackName.Trim(),
            Area = new MapPackAreaSelection
            {
                Name = string.IsNullOrWhiteSpace(AreaName) ? "Selection" : AreaName.Trim(),
                AdminLevel = AdminLevel,
                Bounds = CurrentBounds.Normalize(),
                BoundaryGeoJson = BoundaryGeoJson,
            },
            BaseLayers = new MapPackBaseLayerSelection
            {
                IncludeOsm = IncludeOsm,
                IncludeTerrain = IncludeTerrain,
                IncludeSatellite = IncludeSatellite,
                IncludeContours = IncludeContours,
                IncludeUltraFineContours = IncludeContours && IncludeUltraFineContours,
                MinimumZoom = MinimumZoom,
                MaximumZoom = MaximumZoom,
            },
            Poi = new MapPackPoiSelection
            {
                EnablePoiSeparation = EnablePoiSeparation,
                PbfPath = PbfPath,
                SourceProvider = PbfProvider,
                SourceDownloadUrl = PbfDownloadUrl,
                GenerateFullPoisJsonl = GenerateFullPoisJsonl,
                GenerateTileIndex = GenerateTileIndex,
                SelectedPoiTypes = PoiTypes.Where(static p => p.IsSelected).Select(static p => p.Id).ToArray(),
                IndexOptions = BuildPoiIndexOptions(),
            },
            OutputDirectory = OutputDirectory,
        };
    }

    public OfflineCacheBuildOptions ToOfflineCacheBuildOptions()
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
            PoiPbfPath = PbfPath,
            GenerateFullPoisJsonl = GenerateFullPoisJsonl,
            GenerateTileIndexedPoiFiles = GenerateTileIndex,
            PoiIndexMinimumZoom = PoiIndexMinimumZoom,
            PoiIndexMaximumZoom = PoiIndexMaximumZoom,
            MaxPoiPerTile = MaxPoiPerTile,
            IncludePoiLabels = IncludePoiLabels,
            IncludeOriginalOsmTags = IncludeOriginalOsmTags,
            PoiOutputFormat = PoiOutputFormat,
            SelectedPoiTypes = PoiTypes.Where(static p => p.IsSelected).Select(static p => p.Id).ToArray(),
        }.Normalize();
    }

    public void SetLocalPbfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        PbfPath = path;
        PbfProvider = "local";
        PbfDownloadUrl = string.Empty;
        EnablePoiSeparation = true;
        StatusText = $"PBF selected: {Path.GetFileName(path)}";
    }

    public void SetOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        OutputDirectory = path;
        StatusText = $"Output directory: {path}";
    }

    public void ApplyManualBounds()
    {
        ApplyBounds(CurrentBounds, string.IsNullOrWhiteSpace(AreaName) ? "Manual bounds" : AreaName, AdminLevel, BoundaryGeoJson);
    }

    private async Task SearchAdminAreasAsync()
    {
        await RunOperationAsync(async token =>
        {
            StatusText = "Searching administrative boundaries...";
            var result = await _adminAreaProvider.SearchAsync(
                    new AdminAreaQuery
                    {
                        Text = AreaSearchText,
                        IncludeBoundaryGeoJson = true,
                        Limit = 12,
                    },
                    token);

            AdminAreas.Clear();
            foreach (var area in result.Areas)
                AdminAreas.Add(new AdminAreaOptionViewModel(area));

            StatusText = result.ErrorMessage is not null
                ? $"Boundary search used fallback/cache: {result.ErrorMessage}"
                : result.FromCache
                    ? $"Loaded {AdminAreas.Count} boundary results from cache."
                    : $"Loaded {AdminAreas.Count} boundary results.";
        }).ConfigureAwait(false);
    }

    private async Task SearchGeofabrikAsync()
    {
        await RunOperationAsync(async token =>
        {
            StatusText = "Loading Geofabrik PBF catalog...";
            var results = await _geofabrikCatalogProvider.SearchAsync(GeofabrikSearchText, 20, token);
            GeofabrikRegions.Clear();
            foreach (var region in results)
                GeofabrikRegions.Add(new GeofabrikRegionOptionViewModel(region));

            StatusText = $"Loaded {GeofabrikRegions.Count} PBF source options.";
        }).ConfigureAwait(false);
    }

    private async Task DownloadSelectedPbfAsync()
    {
        if (SelectedGeofabrikRegion is null)
            return;

        await RunOperationAsync(async token =>
        {
            var region = SelectedGeofabrikRegion.Record;
            StatusText = $"Downloading {region.DisplayName}...";
            var progress = new Progress<GeofabrikDownloadProgress>(p =>
            {
                StatusText = p.Percent.HasValue
                    ? $"Downloading PBF {p.Percent.Value:F1}% ({ExportEstimator.FormatBytes(p.BytesReceived)} / {ExportEstimator.FormatBytes(p.TotalBytes ?? 0)})"
                    : $"Downloading PBF {ExportEstimator.FormatBytes(p.BytesReceived)}";
            });

            var entry = await _pbfDownloadService.DownloadAsync(region, forceRefresh: false, progress, token);
            PbfPath = entry.LocalPath;
            PbfProvider = "geofabrik";
            PbfDownloadUrl = entry.Url;
            EnablePoiSeparation = true;
            StatusText = $"PBF ready: {Path.GetFileName(entry.LocalPath)} ({ExportEstimator.FormatBytes(entry.SizeBytes)})";
        }).ConfigureAwait(false);
    }

    private async Task ImportBoundaryAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        await RunOperationAsync(async token =>
        {
            var result = await _boundaryImporter.ImportAsync(path, token);
            if (!result.Success)
            {
                StatusText = result.ErrorMessage ?? "Boundary import failed.";
                return;
            }

            ApplyBounds(result.Bounds, result.Name, null, result.BoundaryGeoJson);
            StatusText = $"Boundary imported: {result.Name}";
        }).ConfigureAwait(false);
    }

    private async Task LoadPoiPreviewAsync()
    {
        if (!CanLoadPoiPreview())
            return;

        await RunOperationAsync(async token =>
        {
            StatusText = "Extracting POI preview...";
            var preview = await _poiExtractor.ExtractAsync(
                    new OsmPoiExtractionOptions
                    {
                        PbfPath = PbfPath,
                        Bounds = CurrentBounds.Normalize(),
                        SelectedPoiTypes = PoiTypes.Where(static p => p.IsSelected).Select(static p => p.Id).ToArray(),
                        IncludeOriginalTags = false,
                        IncludeWays = true,
                        MaxPois = PoiPreviewLimit,
                        ProgressInterval = 10_000,
                    },
                    new Progress<OsmPoiExtractionProgress>(p =>
                    {
                        StatusText = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Scanning PBF: {p.ProcessedElements:N0} OSM elements, {p.ExtractedPoiCount:N0} preview POI");
                    }),
                    token);

            PreviewPoiCount = preview.Count;
            _map.SetPoiPreview(preview, ShowPoiPreview, PoiPreviewLimit);
            StatusText = preview.Count == 0
                ? "No POI found in selected preview area."
                : $"Preview loaded: {preview.Count:N0} POI.";
        }).ConfigureAwait(false);
    }

    private void UpdateEstimate()
    {
        var estimate = _estimator.Estimate(BuildPlan());
        EstimateText = string.Join(
            Environment.NewLine,
            estimate.Layers.Select(l => $"{l.Name}: {l.TileCount:N0} tiles, ~{ExportEstimator.FormatBytes(l.EstimatedBytes)}")
                .Append($"Total: {estimate.TotalTileCount:N0} tiles, ~{ExportEstimator.FormatBytes(estimate.EstimatedTileBytes)}")
                .Append(EnablePoiSeparation
                    ? $"POI: {SelectedPoiTypeText()}, index Z{PoiIndexMinimumZoom}-{PoiIndexMaximumZoom}, max {MaxPoiPerTile:N0}/tile"
                    : "POI: disabled"));
    }

    private string SelectedPoiTypeText()
    {
        var selected = PoiTypes.Where(static p => p.IsSelected).Select(static p => p.Id).ToArray();
        return selected.Length == 0 ? "none" : string.Join(", ", selected);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
            return;

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await operation(_operationCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
            UpdateEstimate();
        }
    }

    private bool CanRunOperation() => !IsBusy;

    private bool CanDownloadSelectedPbf()
    {
        return !IsBusy && SelectedGeofabrikRegion is not null && !string.IsNullOrWhiteSpace(SelectedGeofabrikRegion.Record.PbfUrl);
    }

    private bool CanLoadPoiPreview()
    {
        return !IsBusy &&
               EnablePoiSeparation &&
               !string.IsNullOrWhiteSpace(PbfPath) &&
               File.Exists(PbfPath) &&
               HasSelectedPoiTypes;
    }

    private void CancelOperation()
    {
        _operationCts?.Cancel();
    }

    private void NotifyCommandStates()
    {
        SearchAdminAreasCommand.NotifyCanExecuteChanged();
        SearchGeofabrikCommand.NotifyCanExecuteChanged();
        DownloadSelectedPbfCommand.NotifyCanExecuteChanged();
        ImportBoundaryCommand.NotifyCanExecuteChanged();
        LoadPoiPreviewCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExport));
    }

    private PoiIndexOptions BuildPoiIndexOptions()
    {
        return new PoiIndexOptions
        {
            MinZoom = PoiIndexMinimumZoom,
            MaxZoom = PoiIndexMaximumZoom,
            MaxPoiPerTile = MaxPoiPerTile,
            IncludeLabels = IncludePoiLabels,
            IncludeOriginalTags = IncludeOriginalOsmTags,
            GenerateFullPoisJsonl = GenerateFullPoisJsonl,
            GenerateTileIndex = GenerateTileIndex,
            OutputFormat = PoiOutputFormat,
        }.Normalize();
    }

    private void ApplyBounds(GeoBounds bounds, string? name, int? adminLevel, string boundaryGeoJson)
    {
        var normalized = bounds.Normalize();
        West = normalized.West;
        South = normalized.South;
        East = normalized.East;
        North = normalized.North;
        AreaName = string.IsNullOrWhiteSpace(name) ? "Selection" : name.Trim();
        AdminLevel = adminLevel;
        BoundaryGeoJson = boundaryGeoJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(BoundaryGeoJson) ||
            !_map.SetOfflineCacheSelectionGeoJson(BoundaryGeoJson, AreaName, focusMap: true))
        {
            _map.SetOfflineCacheSelectionBounds((normalized.West, normalized.South, normalized.East, normalized.North), AreaName);
            _map.FocusOnBounds((normalized.West, normalized.South, normalized.East, normalized.North));
        }
        OnPropertyChanged(nameof(BoundsText));
        UpdateEstimate();
    }

    private void ResetPoiTypes()
    {
        PoiTypes.Clear();
        var selectedPreviewTypes = _map.SelectedPoiPreviewTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PoiTypeCatalog.DefaultTypes)
        {
            var option = new PoiTypeOptionViewModel(
                definition.Id,
                definition.Label,
                selectedPreviewTypes.Contains(definition.Id));
            option.PropertyChanged += (_, e) =>
            {
                OnPropertyChanged(nameof(HasSelectedPoiTypes));
                OnPropertyChanged(nameof(HasPoiExportSelection));
                OnPropertyChanged(nameof(CanExport));
                LoadPoiPreviewCommand.NotifyCanExecuteChanged();
                if (string.Equals(e.PropertyName, nameof(PoiTypeOptionViewModel.IsSelected), StringComparison.Ordinal))
                {
                    _map.SetPoiPreviewSelectedTypes(PoiTypes
                        .Where(static p => p.IsSelected)
                        .Select(static p => p.Id));
                }
                UpdateEstimate();
            };
            PoiTypes.Add(option);
        }
    }

    partial void OnSelectedAdminAreaChanged(AdminAreaOptionViewModel? value)
    {
        if (value is null)
            return;

        var area = value.Record;
        ApplyBounds(area.Bounds, area.Name, area.AdminLevel, area.BoundaryGeoJson);
        StatusText = $"Area selected: {area.DisplayName}";
    }

    partial void OnSelectedGeofabrikRegionChanged(GeofabrikRegionOptionViewModel? value)
    {
        DownloadSelectedPbfCommand.NotifyCanExecuteChanged();
        if (value is null)
            return;

        var region = value.Record;
        if (!region.Bounds.Equals(default(GeoBounds)))
        {
            ApplyBounds(region.Bounds, region.Name, null, region.BoundaryGeoJson);
        }
        PbfDownloadUrl = region.PbfUrl;
        PbfProvider = "geofabrik";
        StatusText = $"PBF source selected: {region.DisplayName}";
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();
    partial void OnIncludeOsmChanged(bool value) => OnExportInputChanged();
    partial void OnIncludeTerrainChanged(bool value) => OnExportInputChanged();
    partial void OnIncludeSatelliteChanged(bool value) => OnExportInputChanged();
    partial void OnIncludeContoursChanged(bool value)
    {
        if (!value)
            IncludeUltraFineContours = false;
        OnExportInputChanged();
    }

    partial void OnIncludeUltraFineContoursChanged(bool value) => OnExportInputChanged();
    partial void OnEnablePoiSeparationChanged(bool value) => OnExportInputChanged();
    partial void OnPbfPathChanged(string value)
    {
        OnExportInputChanged();
        LoadPoiPreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnGenerateFullPoisJsonlChanged(bool value) => OnExportInputChanged();
    partial void OnGenerateTileIndexChanged(bool value) => OnExportInputChanged();
    partial void OnIncludePoiLabelsChanged(bool value) => OnExportInputChanged();
    partial void OnIncludeOriginalOsmTagsChanged(bool value) => OnExportInputChanged();
    partial void OnPoiOutputFormatChanged(PoiOutputFormat value) => OnExportInputChanged();
    partial void OnOutputDirectoryChanged(string value) => OnExportInputChanged();
    partial void OnShowPoiPreviewChanged(bool value) => _map.ShowPoiPreview = value;

    partial void OnWestChanged(double value) => OnBoundsChanged();
    partial void OnSouthChanged(double value) => OnBoundsChanged();
    partial void OnEastChanged(double value) => OnBoundsChanged();
    partial void OnNorthChanged(double value) => OnBoundsChanged();

    partial void OnMinimumZoomChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 18);
        if (clamped != value)
        {
            MinimumZoom = clamped;
            return;
        }
        if (MaximumZoom < clamped)
            MaximumZoom = clamped;
        OnExportInputChanged();
    }

    partial void OnMaximumZoomChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 18);
        if (clamped != value)
        {
            MaximumZoom = clamped;
            return;
        }
        if (MinimumZoom > clamped)
            MinimumZoom = clamped;
        OnExportInputChanged();
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
        OnExportInputChanged();
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
        OnExportInputChanged();
    }

    partial void OnMaxPoiPerTileChanged(int value)
    {
        if (value < 1)
        {
            MaxPoiPerTile = 1;
            return;
        }
        OnExportInputChanged();
    }

    partial void OnPoiPreviewLimitChanged(int value)
    {
        if (value < 1)
        {
            PoiPreviewLimit = 1;
            return;
        }

        _map.PoiPreviewLimit = value;
    }

    private void OnBoundsChanged()
    {
        OnPropertyChanged(nameof(BoundsText));
        UpdateEstimate();
    }

    private void OnExportInputChanged()
    {
        OnPropertyChanged(nameof(HasTileSelection));
        OnPropertyChanged(nameof(HasPoiExportSelection));
        OnPropertyChanged(nameof(CanExport));
        UpdateEstimate();
    }
}

public sealed class AdminAreaOptionViewModel
{
    public AdminAreaOptionViewModel(AdminAreaRecord record)
    {
        Record = record;
    }

    public AdminAreaRecord Record { get; }
    public string DisplayText => string.IsNullOrWhiteSpace(Record.DisplayName) ? Record.Name : Record.DisplayName;
    public string DetailText => Record.AdminLevel.HasValue
        ? $"admin_level {Record.AdminLevel.Value} | {Record.Bounds.ToInvariantText()}"
        : Record.Bounds.ToInvariantText();
}

public sealed class GeofabrikRegionOptionViewModel
{
    public GeofabrikRegionOptionViewModel(GeofabrikRegionRecord record)
    {
        Record = record;
    }

    public GeofabrikRegionRecord Record { get; }
    public string DisplayText => string.IsNullOrWhiteSpace(Record.DisplayName) ? Record.Name : Record.DisplayName;
    public string DetailText => string.IsNullOrWhiteSpace(Record.PbfUrl)
        ? "No PBF URL"
        : Record.PbfUrl;
}
