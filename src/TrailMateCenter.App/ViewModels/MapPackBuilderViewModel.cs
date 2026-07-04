using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.ViewModels;

public sealed partial class MapPackBuilderViewModel : ObservableObject
{
    private static string T(string key) => LocalizationService.Instance.GetString(key);
    private static string F(string key, params object[] args) => LocalizationService.Instance.Format(key, args);

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
            ApplyBounds(new GeoBounds(bounds.West, bounds.South, bounds.East, bounds.North), T("Ui.MapPack.CurrentMapSelection"), null, string.Empty);
        }
        else
        {
            ApplyBounds(new GeoBounds(10, 55, 25, 69.5), T("Ui.MapPack.DefaultSweden"), 2, string.Empty);
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
    private string _packName = T("Ui.MapPack.DefaultPackName");

    [ObservableProperty]
    private string _areaSearchText = string.Empty;

    [ObservableProperty]
    private AdminAreaOptionViewModel? _selectedAdminArea;

    [ObservableProperty]
    private GeofabrikRegionOptionViewModel? _selectedGeofabrikRegion;

    [ObservableProperty]
    private string _areaName = T("Ui.MapPack.DefaultAreaName");

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
    private bool _isExporting;

    [ObservableProperty]
    private bool _isExportProgressIndeterminate;

    [ObservableProperty]
    private double _exportProgressPercent;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    [ObservableProperty]
    private string _statusText = T("Ui.MapPack.Status.Ready");

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private long _previewPoiCount;

    public IReadOnlyList<int> AvailableZoomLevels { get; } = Enumerable.Range(0, 19).ToArray();
    public IReadOnlyList<int> AvailablePoiZoomLevels { get; } = Enumerable.Range(0, 25).ToArray();
    public IReadOnlyList<PoiOutputFormatOptionViewModel> AvailablePoiOutputFormats { get; } =
        [new(PoiOutputFormat.Readable), new(PoiOutputFormat.Compact)];

    public bool HasSelectedPoiTypes => PoiTypes.Any(static p => p.IsSelected);
    public bool CanExport => !IsBusy && (HasTileSelection || HasPoiExportSelection);
    public bool HasTileSelection => IncludeOsm || IncludeTerrain || IncludeSatellite || IncludeContours;
    public bool HasPoiExportSelection => EnablePoiSeparation && !string.IsNullOrWhiteSpace(PbfPath) && HasSelectedPoiTypes;
    public bool HasExportProgress => IsExporting || !string.IsNullOrWhiteSpace(ExportProgressText);
    public string BoundsText => CurrentBounds.ToInvariantText();
    public GeoBounds CurrentBounds => new(West, South, East, North);
    public string AdminLevelText => AdminLevel.HasValue ? AdminLevel.Value.ToString() : T("Ui.MapPack.AdminLevelUnknown");
    public string PreviewPoiCountText => F("Ui.MapPack.PreviewPoiCount", PreviewPoiCount);
    public PoiOutputFormatOptionViewModel? SelectedPoiOutputFormat
    {
        get => AvailablePoiOutputFormats.FirstOrDefault(option => option.Value == PoiOutputFormat);
        set
        {
            if (value is not null)
                PoiOutputFormat = value.Value;
        }
    }

    public MapPackExportPlan BuildPlan()
    {
        return new MapPackExportPlan
        {
            Name = string.IsNullOrWhiteSpace(PackName)
                ? (string.IsNullOrWhiteSpace(AreaName) ? T("Ui.MapPack.DefaultPackName") : AreaName.Trim())
                : PackName.Trim(),
            Area = new MapPackAreaSelection
            {
                Name = string.IsNullOrWhiteSpace(AreaName) ? T("Ui.MapPack.DefaultAreaName") : AreaName.Trim(),
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

    public CancellationToken BeginExport()
    {
        if (IsBusy)
            return CancellationToken.None;

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        IsExporting = true;
        IsExportProgressIndeterminate = true;
        ExportProgressPercent = 0;
        ExportProgressText = T("Ui.MapPack.Status.ExportStarting");
        StatusText = ExportProgressText;
        NotifyCommandStates();
        return _operationCts.Token;
    }

    public void ApplyExportProgress(MainWindowViewModel.OfflineCacheExportProgress progress)
    {
        var updateStatusText = true;
        switch (progress.Kind)
        {
            case MainWindowViewModel.OfflineCacheExportProgressKind.Preparing:
                IsExportProgressIndeterminate = true;
                ExportProgressPercent = 0;
                ExportProgressText = T("Ui.MapPack.Status.ExportPreparing");
                break;
            case MainWindowViewModel.OfflineCacheExportProgressKind.Layer:
                IsExportProgressIndeterminate = progress.Total <= 0;
                ExportProgressPercent = progress.Percent;
                ExportProgressText = F(
                    "Ui.MapPack.Status.ExportLayerProgress",
                    T(progress.LayerResourceKey),
                    progress.Zoom,
                    progress.Completed,
                    progress.Total,
                    progress.CopiedTiles,
                    progress.SkippedTiles);
                break;
            case MainWindowViewModel.OfflineCacheExportProgressKind.Poi:
                IsExportProgressIndeterminate = true;
                ExportProgressText = F(
                    "Ui.MapPack.Status.ExportPoiProgress",
                    progress.ProcessedElements,
                    progress.ExtractedPoiCount);
                break;
            case MainWindowViewModel.OfflineCacheExportProgressKind.Finalizing:
                IsExportProgressIndeterminate = true;
                ExportProgressText = T("Ui.MapPack.Status.ExportFinalizing");
                break;
            case MainWindowViewModel.OfflineCacheExportProgressKind.Completed:
                IsExportProgressIndeterminate = false;
                ExportProgressPercent = 100;
                ExportProgressText = T("Ui.MapPack.Status.ExportCompleted");
                updateStatusText = false;
                break;
            case MainWindowViewModel.OfflineCacheExportProgressKind.Failed:
                IsExportProgressIndeterminate = false;
                ExportProgressText = T("Ui.MapPack.Status.ExportFailedShort");
                updateStatusText = false;
                break;
        }

        if (updateStatusText)
            StatusText = ExportProgressText;
    }

    public void ApplyTilePreparationProgress()
    {
        IsExportProgressIndeterminate = true;
        ExportProgressPercent = 0;
        ExportProgressText = T("Ui.MapPack.Status.PreparingTiles");
        StatusText = ExportProgressText;
    }

    public void ApplyTilePreparationComplete()
    {
        IsExportProgressIndeterminate = true;
        ExportProgressPercent = 0;
        ExportProgressText = T("Ui.MapPack.Status.TilesReady");
        StatusText = ExportProgressText;
    }

    public void ApplyOperationCanceled()
    {
        IsExportProgressIndeterminate = false;
        ExportProgressText = T("Ui.MapPack.Status.OperationCanceled");
        StatusText = ExportProgressText;
    }

    public void EndExport()
    {
        IsExporting = false;
        IsExportProgressIndeterminate = false;
        IsBusy = false;
        NotifyCommandStates();
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
        StatusText = F("Ui.MapPack.Status.PbfSelected", Path.GetFileName(path));
    }

    public void SetOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        OutputDirectory = path;
        StatusText = F("Ui.MapPack.Status.OutputDirectory", path);
    }

    public void ApplyManualBounds()
    {
        ApplyBounds(CurrentBounds, string.IsNullOrWhiteSpace(AreaName) ? T("Ui.MapPack.ManualBounds") : AreaName, AdminLevel, BoundaryGeoJson);
    }

    private async Task SearchAdminAreasAsync()
    {
        await RunOperationAsync(async token =>
        {
            StatusText = T("Ui.MapPack.Status.SearchingBoundary");
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
                ? F("Ui.MapPack.Status.BoundaryFallback", result.ErrorMessage)
                : result.FromCache
                    ? F("Ui.MapPack.Status.BoundaryLoadedCache", AdminAreas.Count)
                    : F("Ui.MapPack.Status.BoundaryLoaded", AdminAreas.Count);
        }).ConfigureAwait(false);
    }

    private async Task SearchGeofabrikAsync()
    {
        await RunOperationAsync(async token =>
        {
            await RefreshPbfSourcesForCurrentBoundsAsync(token, autoSelect: true).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private async Task DownloadSelectedPbfAsync()
    {
        if (SelectedGeofabrikRegion is null)
            return;

        await RunOperationAsync(async token =>
        {
            var region = SelectedGeofabrikRegion.Record;
            StatusText = F("Ui.MapPack.Status.DownloadingRegion", region.DisplayName);
            var progress = new Progress<GeofabrikDownloadProgress>(p =>
            {
                StatusText = p.Percent.HasValue
                    ? F(
                        "Ui.MapPack.Status.DownloadingPbfPercent",
                        p.Percent.Value,
                        ExportEstimator.FormatBytes(p.BytesReceived),
                        ExportEstimator.FormatBytes(p.TotalBytes ?? 0))
                    : F("Ui.MapPack.Status.DownloadingPbfBytes", ExportEstimator.FormatBytes(p.BytesReceived));
            });

            var entry = await _pbfDownloadService.DownloadAsync(region, forceRefresh: false, progress, token);
            PbfPath = entry.LocalPath;
            PbfProvider = "geofabrik";
            PbfDownloadUrl = entry.Url;
            EnablePoiSeparation = true;
            StatusText = F("Ui.MapPack.Status.PbfReady", Path.GetFileName(entry.LocalPath), ExportEstimator.FormatBytes(entry.SizeBytes));
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
                StatusText = result.ErrorMessage ?? T("Ui.MapPack.Status.BoundaryImportFailed");
                return;
            }

            ApplyBounds(result.Bounds, result.Name, null, result.BoundaryGeoJson);
            StatusText = F("Ui.MapPack.Status.BoundaryImported", result.Name);
        }).ConfigureAwait(false);
    }

    private async Task LoadPoiPreviewAsync()
    {
        if (!CanLoadPoiPreview())
            return;

        await RunOperationAsync(async token =>
        {
            StatusText = T("Ui.MapPack.Status.ExtractingPoiPreview");
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
                        StatusText = F("Ui.MapPack.Status.ScanningPbf", p.ProcessedElements, p.ExtractedPoiCount);
                    }),
                    token);

            PreviewPoiCount = preview.Count;
            _map.SetPoiPreview(preview, ShowPoiPreview, PoiPreviewLimit);
            StatusText = preview.Count == 0
                ? T("Ui.MapPack.Status.NoPoiFound")
                : F("Ui.MapPack.Status.PreviewLoaded", preview.Count);
        }).ConfigureAwait(false);
    }

    private void UpdateEstimate()
    {
        var estimate = _estimator.Estimate(BuildPlan());
        EstimateText = string.Join(
            Environment.NewLine,
            estimate.Layers.Select(l => F("Ui.MapPack.Estimate.Layer", l.Name, l.TileCount, ExportEstimator.FormatBytes(l.EstimatedBytes)))
                .Append(F("Ui.MapPack.Estimate.Total", estimate.TotalTileCount, ExportEstimator.FormatBytes(estimate.EstimatedTileBytes)))
                .Append(EnablePoiSeparation
                    ? F("Ui.MapPack.Estimate.Poi", SelectedPoiTypeText(), PoiIndexMinimumZoom, PoiIndexMaximumZoom, MaxPoiPerTile)
                    : T("Ui.MapPack.Estimate.PoiDisabled")));
    }

    private string SelectedPoiTypeText()
    {
        var selected = PoiTypes.Where(static p => p.IsSelected).Select(static p => p.Label).ToArray();
        return selected.Length == 0 ? T("Ui.MapPack.None") : string.Join(", ", selected);
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
            StatusText = T("Ui.MapPack.Status.OperationCanceled");
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
        AreaName = string.IsNullOrWhiteSpace(name) ? T("Ui.MapPack.DefaultAreaName") : name.Trim();
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

    private async void QueuePbfSourceAutoMatch()
    {
        if (IsBusy)
            return;

        await RunOperationAsync(async token =>
        {
            await RefreshPbfSourcesForCurrentBoundsAsync(token, autoSelect: true).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private async Task RefreshPbfSourcesForCurrentBoundsAsync(CancellationToken token, bool autoSelect)
    {
        StatusText = T("Ui.MapPack.Status.MatchingPbfSource");
        var results = await _geofabrikCatalogProvider
            .FindCoveringRegionsAsync(CurrentBounds, 12, token)
            .ConfigureAwait(true);

        GeofabrikRegions.Clear();
        foreach (var region in results)
            GeofabrikRegions.Add(new GeofabrikRegionOptionViewModel(region));

        if (results.Count == 0)
        {
            SelectedGeofabrikRegion = null;
            StatusText = T("Ui.MapPack.Status.NoCoveringPbfSource");
            return;
        }

        if (autoSelect)
        {
            SelectedGeofabrikRegion = GeofabrikRegions[0];
            StatusText = F("Ui.MapPack.Status.PbfSourceMatched", SelectedGeofabrikRegion.Record.DisplayName);
        }
        else
        {
            StatusText = F("Ui.MapPack.Status.PbfOptionsLoaded", GeofabrikRegions.Count);
        }
    }

    private void ResetPoiTypes()
    {
        PoiTypes.Clear();
        var selectedPreviewTypes = _map.SelectedPoiPreviewTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PoiTypeCatalog.DefaultTypes)
        {
            var option = PoiTypeCatalog.CreateOption(definition, selectedPreviewTypes.Contains(definition.Id));
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
        StatusText = F("Ui.MapPack.Status.AreaSelected", area.DisplayName);
        QueuePbfSourceAutoMatch();
    }

    partial void OnSelectedGeofabrikRegionChanged(GeofabrikRegionOptionViewModel? value)
    {
        DownloadSelectedPbfCommand.NotifyCanExecuteChanged();
        if (value is null)
            return;

        var region = value.Record;
        PbfDownloadUrl = region.PbfUrl;
        PbfProvider = "geofabrik";
        StatusText = F("Ui.MapPack.Status.PbfSourceSelected", region.DisplayName);
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();
    partial void OnIsExportingChanged(bool value) => OnPropertyChanged(nameof(HasExportProgress));
    partial void OnExportProgressTextChanged(string value) => OnPropertyChanged(nameof(HasExportProgress));
    partial void OnAdminLevelChanged(int? value) => OnPropertyChanged(nameof(AdminLevelText));
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
    partial void OnPoiOutputFormatChanged(PoiOutputFormat value)
    {
        OnPropertyChanged(nameof(SelectedPoiOutputFormat));
        OnExportInputChanged();
    }
    partial void OnPreviewPoiCountChanged(long value) => OnPropertyChanged(nameof(PreviewPoiCountText));
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
        ? LocalizationService.Instance.Format("Ui.MapPack.Detail.AdminLevel", Record.AdminLevel.Value, Record.Bounds.ToInvariantText())
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
        ? LocalizationService.Instance.GetString("Ui.MapPack.Detail.NoPbfUrl")
        : Record.PbfUrl;
}

public sealed class PoiOutputFormatOptionViewModel
{
    public PoiOutputFormatOptionViewModel(PoiOutputFormat value)
    {
        Value = value;
    }

    public PoiOutputFormat Value { get; }
    public string Label => Value == PoiOutputFormat.Compact
        ? LocalizationService.Instance.GetString("Ui.MapPack.OutputFormat.Compact")
        : LocalizationService.Instance.GetString("Ui.MapPack.OutputFormat.Readable");
}
