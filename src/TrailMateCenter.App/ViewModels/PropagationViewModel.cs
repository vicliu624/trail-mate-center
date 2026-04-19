using System.Collections.ObjectModel;
using System.Text.Json;
using Grpc.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public enum PropagationMode
{
    CoverageMap = 0,
    InterferenceAnalysis = 1,
    RelayOptimization = 2,
    AdvancedModeling = 3,
    CalibrationValidation = 4,
}

public enum PropagationTaskState
{
    Queued = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5,
}

public sealed partial class PropagationTaskItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _taskName = string.Empty;

    [ObservableProperty]
    private string _runId = string.Empty;

    [ObservableProperty]
    private string _modeText = string.Empty;

    [ObservableProperty]
    private PropagationTaskState _state = PropagationTaskState.Queued;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private string _stageText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _startedText = "--";

    [ObservableProperty]
    private string _durationText = "--";

    [ObservableProperty]
    private string _cacheText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private string _dataVersionTag = string.Empty;

    [ObservableProperty]
    private string _parameterHashTag = string.Empty;
}

public sealed class PropagationLayerOptionViewModel
{
    public PropagationLayerOptionViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
}

public sealed class PropagationSiteRoleOptionViewModel
{
    public PropagationSiteRoleOptionViewModel(PropagationSiteRole role, string label)
    {
        Role = role;
        Label = label;
    }

    public PropagationSiteRole Role { get; }
    public string Label { get; }
}

public sealed class PropagationSiteColorOptionViewModel
{
    public PropagationSiteColorOptionViewModel(string colorHex, string label)
    {
        ColorHex = colorHex;
        Label = label;
    }

    public string ColorHex { get; }
    public string Label { get; }
}

public sealed class PropagationCameraPresetViewModel
{
    public PropagationCameraPresetViewModel(
        string key,
        string label,
        double x,
        double y,
        double z,
        double pitch,
        double yaw,
        double roll,
        double fov)
    {
        Key = key;
        Label = label;
        X = x;
        Y = y;
        Z = z;
        Pitch = pitch;
        Yaw = yaw;
        Roll = roll;
        Fov = fov;
    }

    public string Key { get; }
    public string Label { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double Pitch { get; }
    public double Yaw { get; }
    public double Roll { get; }
    public double Fov { get; }
}

public sealed partial class PropagationViewModel : ViewModelBase, ILocalizationAware
{
    private const int AutoRunDebounceMilliseconds = 400;
    private readonly IPropagationSimulationService _simulationService;
    private readonly PropagationServiceProcessManager _propagationServiceProcessManager;
    private readonly IPropagationUnityBridge _unityBridge;
    private readonly LogStore _logStore;
    private readonly PropagationTerrainInputBuilder _terrainInputBuilder = new();
    private readonly Dictionary<string, PropagationSimulationResult> _resultsByRunId = new(StringComparer.Ordinal);
    private readonly ObservableCollection<PropagationSiteInput> _scenarioSites = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _autoRunDebounceCts;
    private string? _activeRunId;
    private MapViewModel? _mapViewModel;
    private bool _suppressDirtyFlag;
    private bool _suppressCameraPresetRefresh;
    private bool _isUnityLayerLoading;
    private bool _preferScenarioSiteScene;
    private PropagationUnityProcessState _unityProcessState = PropagationUnityProcessState.Stopped;
    private string _unityProcessMessage = string.Empty;

    public PropagationViewModel(
        IPropagationSimulationService simulationService,
        LogStore logStore,
        PropagationServiceProcessManager propagationServiceProcessManager,
        IPropagationUnityBridge unityBridge)
    {
        _simulationService = simulationService;
        _logStore = logStore;
        _propagationServiceProcessManager = propagationServiceProcessManager;
        _unityBridge = unityBridge;
        ScenarioSites = new ReadOnlyObservableCollection<PropagationSiteInput>(_scenarioSites);

        _unityBridge.BridgeStateChanged += OnUnityBridgeStateChanged;
        _unityBridge.TelemetryUpdated += OnUnityBridgeTelemetryUpdated;
        _unityBridge.LayerStateChanged += OnUnityLayerStateChanged;
        _unityBridge.DiagnosticSnapshotReceived += OnUnityDiagnosticSnapshotReceived;
        _unityBridge.CameraStateChanged += OnUnityCameraStateChanged;
        _unityBridge.MapPointSelected += OnUnityMapPointSelected;
        _unityBridge.ProfileLineChanged += OnUnityProfileLineChanged;

        SetCoverageModeCommand = new RelayCommand(SetCoverageMode);
        SetInterferenceModeCommand = new RelayCommand(SetInterferenceMode);
        SetRelayModeCommand = new RelayCommand(SetRelayMode);
        SetAdvancedModeCommand = new RelayCommand(SetAdvancedMode);
        SetCalibrationModeCommand = new RelayCommand(SetCalibrationMode);

        RunSimulationCommand = new AsyncRelayCommand(RunSimulationAsync, CanRunSimulation);
        PauseSimulationCommand = new AsyncRelayCommand(TogglePauseSimulationAsync, CanPauseSimulation);
        CancelSimulationCommand = new AsyncRelayCommand(CancelSimulationAsync, CanCancelSimulation);
        OptimizeRelaysCommand = new AsyncRelayCommand(RunRelayOptimizationAsync, CanRunSimulation);
        AnalyzeInterferenceCommand = new AsyncRelayCommand(RunInterferenceAnalysisAsync, CanRunSimulation);
        RunMonteCarloCommand = new AsyncRelayCommand(RunAdvancedMonteCarloAsync, CanRunSimulation);
        ExportResultCommand = new AsyncRelayCommand(ExportResultAsync, CanExportResult);
        ConfirmPendingSiteCommand = new RelayCommand(ConfirmPendingScenarioSite, CanConfirmPendingScenarioSite);
        CancelPendingSiteCommand = new RelayCommand(CancelPendingScenarioSite);
        ConfirmDeleteSiteCommand = new RelayCommand(ConfirmDeleteScenarioSite, CanConfirmDeleteScenarioSite);
        CancelDeleteSiteCommand = new RelayCommand(CancelDeleteScenarioSite);
        AttachUnityViewportCommand = new AsyncRelayCommand(AttachUnityViewportAsync);
        SyncUnityBridgeCommand = new AsyncRelayCommand(SyncUnityBridgeAsync, CanSyncUnityBridge);
        SyncUnityCameraCommand = new AsyncRelayCommand(SyncUnityCameraAsync, CanSyncUnityCamera);
        ApplyCameraPresetCommand = new RelayCommand(ApplyCameraPreset);

        UnityBridgeStateText = _unityBridge.IsAttached
            ? LocalizationService.Instance.GetString("Status.Propagation.UnityAttached")
            : LocalizationService.Instance.GetString("Status.Propagation.UnityDetached");
        IsUnityBridgeAttached = _unityBridge.IsAttached;
        PauseButtonText = LocalizationService.Instance.GetString("Ui.Propagation.Button.Pause");
        RefreshLayerOptions();
        RefreshScenarioSiteRoleOptions();
        RefreshScenarioSiteColorOptions();
        RefreshCameraPresets();
        ApplyDefaultCameraPreset();
        InitializeWorkbenchModules();
        RefreshLocalization();
        UpdateUnityViewportOverlay();
    }

    public void AttachMap(MapViewModel mapViewModel)
    {
        _mapViewModel = mapViewModel;
        MapWorkbench?.OnScenarioSitesChanged();
    }

    internal bool TryGetViewportWorldBounds(out (double MinX, double MinZ, double MaxX, double MaxZ) bounds)
    {
        if (_mapViewModel is null)
        {
            bounds = default;
            return false;
        }

        return _mapViewModel.TryGetViewportWorldBounds(out bounds);
    }

    public ObservableCollection<PropagationTaskItemViewModel> ActiveTasks { get; } = new();
    public ObservableCollection<PropagationLayerOptionViewModel> LayerOptions { get; } = new();
    public ObservableCollection<PropagationSiteRoleOptionViewModel> ScenarioSiteRoleOptions { get; } = new();
    public ObservableCollection<PropagationSiteColorOptionViewModel> ScenarioSiteColorOptions { get; } = new();
    public ObservableCollection<PropagationCameraPresetViewModel> CameraPresets { get; } = new();
    public ReadOnlyObservableCollection<PropagationSiteInput> ScenarioSites { get; }

    [ObservableProperty]
    private PropagationTaskItemViewModel? _selectedTask;

    [ObservableProperty]
    private PropagationMode _selectedPropagationMode = PropagationMode.CoverageMap;

    [ObservableProperty]
    private bool _isParametersDirty;

    [ObservableProperty]
    private bool _isSimulationRunning;

    [ObservableProperty]
    private bool _isSimulationPaused;

    [ObservableProperty]
    private string _simulationStateText = string.Empty;

    [ObservableProperty]
    private string _currentStageText = string.Empty;

    [ObservableProperty]
    private string _selectedModeText = string.Empty;

    [ObservableProperty]
    private string _modeHintText = string.Empty;

    [ObservableProperty]
    private string _pauseButtonText = string.Empty;

    [ObservableProperty]
    private string _selectedPresetName = "Mountain Rescue";

    [ObservableProperty]
    private double _terrainOverlayOpacity = 0.5;

    [ObservableProperty]
    private bool _isPendingSiteDialogVisible;

    [ObservableProperty]
    private bool _isDeleteSiteDialogVisible;

    [ObservableProperty]
    private string _pendingSiteLabel = string.Empty;

    [ObservableProperty]
    private PropagationSiteRoleOptionViewModel? _selectedPendingSiteRole;

    [ObservableProperty]
    private double _pendingSiteAntennaHeightM = 14;

    [ObservableProperty]
    private double _pendingSiteFrequencyMHz = 915;

    [ObservableProperty]
    private double _pendingSiteTxPowerDbm = 20;

    [ObservableProperty]
    private string _pendingSiteSpreadingFactor = "SF10";

    [ObservableProperty]
    private PropagationSiteColorOptionViewModel? _selectedPendingSiteColor;

    [ObservableProperty]
    private double? _pendingSiteX;

    [ObservableProperty]
    private double? _pendingSiteZ;

    [ObservableProperty]
    private string? _editingScenarioSiteId;

    [ObservableProperty]
    private string? _sitePendingDeletionId;

    [ObservableProperty]
    private string? _selectedScenarioSiteId;

    [ObservableProperty]
    private bool _useMonteCarlo;

    [ObservableProperty]
    private int _monteCarloIterations = 120;

    [ObservableProperty]
    private string _selectedOptimizationAlgorithm = "Greedy";

    [ObservableProperty]
    private double _frequencyMHz = 915;

    [ObservableProperty]
    private double _txPowerDbm = 20;

    [ObservableProperty]
    private string _spreadingFactor = "SF10";

    [ObservableProperty]
    private double _environmentLossDb = 6;

    [ObservableProperty]
    private double _vegetationAlphaSparse = 0.3;

    [ObservableProperty]
    private double _vegetationAlphaDense = 0.8;

    [ObservableProperty]
    private double _shadowSigmaDb = 8;

    [ObservableProperty]
    private double _reflectionCoeff = 0.2;

    [ObservableProperty]
    private double? _downlinkRssiDbm;

    [ObservableProperty]
    private double? _uplinkRssiDbm;

    [ObservableProperty]
    private double? _downlinkMarginDb;

    [ObservableProperty]
    private double? _uplinkMarginDb;

    [ObservableProperty]
    private bool? _isLinkFeasible;

    [ObservableProperty]
    private double? _reliability95;

    [ObservableProperty]
    private double? _reliability80;

    [ObservableProperty]
    private double? _coverage95AreaKm2;

    [ObservableProperty]
    private double? _coverage80AreaKm2;

    [ObservableProperty]
    private double? _fsplDb;

    [ObservableProperty]
    private double? _diffractionLossDb;

    [ObservableProperty]
    private double? _vegetationLossDb;

    [ObservableProperty]
    private double? _reflectionLossDb;

    [ObservableProperty]
    private double? _shadowLossDb;

    [ObservableProperty]
    private double? _sinrDb;

    [ObservableProperty]
    private double? _conflictRate;

    [ObservableProperty]
    private int? _maxCapacity;

    [ObservableProperty]
    private double? _profileDistanceKm;

    [ObservableProperty]
    private double? _profileFresnelRadiusM;

    [ObservableProperty]
    private double? _profileMarginDb;

    [ObservableProperty]
    private string _profileMainObstacleLabel = "--";

    [ObservableProperty]
    private double? _profileMainObstacleV;

    [ObservableProperty]
    private double? _profileMainObstacleLdDb;

    [ObservableProperty]
    private string _dataVersionTag = "--";

    [ObservableProperty]
    private string _modelVersionTag = "--";

    [ObservableProperty]
    private string _parameterHashTag = "--";

    [ObservableProperty]
    private string _assumptionFlagsText = "--";

    [ObservableProperty]
    private string _validityWarningsText = "--";

    [ObservableProperty]
    private string _outputLayerUrisText = "--";

    [ObservableProperty]
    private string _selectedRunIdText = "--";

    [ObservableProperty]
    private string _selectedRunStatusText = "--";

    [ObservableProperty]
    private string _unityBridgeStateText = string.Empty;

    [ObservableProperty]
    private bool _isUnityBridgeAttached;

    [ObservableProperty]
    private string _unityLastAckText = "--";

    [ObservableProperty]
    private string _unitySelectedPointText = "--";

    [ObservableProperty]
    private string _unityProfileLineText = "--";

    [ObservableProperty]
    private string _unityBridgeTelemetryText = "--";

    [ObservableProperty]
    private string _unityHeartbeatText = "--";

    [ObservableProperty]
    private string _unityReconnectText = "--";

    [ObservableProperty]
    private string _unityProcessStateText = "--";

    [ObservableProperty]
    private string _unityProcessPidText = "--";

    [ObservableProperty]
    private double? _unityDiagnosticFps;

    [ObservableProperty]
    private double? _unityDiagnosticFrameTimeP95Ms;

    [ObservableProperty]
    private double? _unityDiagnosticGpuMemoryMb;

    [ObservableProperty]
    private double? _unityDiagnosticLayerLoadMs;

    [ObservableProperty]
    private double? _unityDiagnosticTileCacheHitRate;

    [ObservableProperty]
    private string _unityDiagnosticText = "--";

    [ObservableProperty]
    private string _unityDiagnosticStatusText = "--";

    [ObservableProperty]
    private string _unityDiagnosticStatusColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityDiagnosticFpsColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityDiagnosticFrameTimeColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityDiagnosticGpuColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityDiagnosticLayerLoadColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityDiagnosticTileHitColor = "#9AA3AE";

    [ObservableProperty]
    private string _unityCameraStateText = "--";

    [ObservableProperty]
    private double? _unityCameraX;

    [ObservableProperty]
    private double? _unityCameraY;

    [ObservableProperty]
    private double? _unityCameraZ;

    [ObservableProperty]
    private double? _unityCameraYaw;

    [ObservableProperty]
    private double? _unityCameraPitch;

    [ObservableProperty]
    private double? _unityCameraRoll;

    [ObservableProperty]
    private double? _unityCameraFov;

    [ObservableProperty]
    private double? _selectedPointX;

    [ObservableProperty]
    private double? _selectedPointY;

    [ObservableProperty]
    private string _selectedNodeId = string.Empty;

    [ObservableProperty]
    private string _simulationMessage = string.Empty;

    [ObservableProperty]
    private string _simulationIssueTitle = string.Empty;

    [ObservableProperty]
    private string _simulationIssueAction = string.Empty;

    [ObservableProperty]
    private string _simulationIssueDetail = string.Empty;

    [ObservableProperty]
    private PropagationLayerOptionViewModel? _selectedLayerOption;

    [ObservableProperty]
    private string _unityActiveLayerText = "--";

    [ObservableProperty]
    private PropagationCameraPresetViewModel? _selectedCameraPreset;

    [ObservableProperty]
    private string _unityLayerStatusText = "--";

    [ObservableProperty]
    private string _unityLayerStatusColor = "#9AA3AE";

    [ObservableProperty]
    private double? _unityLayerLoadProgress;

    [ObservableProperty]
    private bool _unityLayerProgressVisible;

    [ObservableProperty]
    private double? _unityLayerTransitionMs;

    [ObservableProperty]
    private bool _unityViewportOverlayVisible;

    [ObservableProperty]
    private string _unityViewportOverlayTitle = string.Empty;

    [ObservableProperty]
    private string _unityViewportOverlayDetail = string.Empty;

    public bool HasSelectedResult => SelectedTask is not null && _resultsByRunId.ContainsKey(SelectedTask.RunId);
    internal bool PreferScenarioSiteScene => _preferScenarioSiteScene;

    public IRelayCommand SetCoverageModeCommand { get; }
    public IRelayCommand SetInterferenceModeCommand { get; }
    public IRelayCommand SetRelayModeCommand { get; }
    public IRelayCommand SetAdvancedModeCommand { get; }
    public IRelayCommand SetCalibrationModeCommand { get; }

    public IAsyncRelayCommand RunSimulationCommand { get; }
    public IAsyncRelayCommand PauseSimulationCommand { get; }
    public IAsyncRelayCommand CancelSimulationCommand { get; }
    public IAsyncRelayCommand OptimizeRelaysCommand { get; }
    public IAsyncRelayCommand AnalyzeInterferenceCommand { get; }
    public IAsyncRelayCommand RunMonteCarloCommand { get; }
    public IAsyncRelayCommand ExportResultCommand { get; }
    public IRelayCommand ConfirmPendingSiteCommand { get; }
    public IRelayCommand CancelPendingSiteCommand { get; }
    public IRelayCommand ConfirmDeleteSiteCommand { get; }
    public IRelayCommand CancelDeleteSiteCommand { get; }
    public IAsyncRelayCommand AttachUnityViewportCommand { get; }
    public IAsyncRelayCommand SyncUnityBridgeCommand { get; }
    public IAsyncRelayCommand SyncUnityCameraCommand { get; }
    public IRelayCommand ApplyCameraPresetCommand { get; }

    partial void OnFrequencyMHzChanged(double value) => MarkParametersDirty();
    partial void OnTxPowerDbmChanged(double value) => MarkParametersDirty();
    partial void OnSpreadingFactorChanged(string value) => MarkParametersDirty();
    partial void OnEnvironmentLossDbChanged(double value) => MarkParametersDirty();
    partial void OnVegetationAlphaSparseChanged(double value) => MarkParametersDirty();
    partial void OnVegetationAlphaDenseChanged(double value) => MarkParametersDirty();
    partial void OnShadowSigmaDbChanged(double value) => MarkParametersDirty();
    partial void OnReflectionCoeffChanged(double value) => MarkParametersDirty();
    partial void OnUseMonteCarloChanged(bool value) => MarkParametersDirty();
    partial void OnMonteCarloIterationsChanged(int value) => MarkParametersDirty();
    partial void OnSelectedOptimizationAlgorithmChanged(string value) => MarkParametersDirty();
    partial void OnSelectedPresetNameChanged(string value) => MarkParametersDirty();
    partial void OnTerrainOverlayOpacityChanged(double value) => MapWorkbench?.OnOverlayOpacityChanged();
    partial void OnSelectedScenarioSiteIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedScenarioSiteCoverageRadiusM));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnPendingSiteLabelChanged(string value)
    {
        ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PendingScenarioSitePoint));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnSelectedPendingSiteRoleChanged(PropagationSiteRoleOptionViewModel? value) => ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
    partial void OnPendingSiteAntennaHeightMChanged(double value) => ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
    partial void OnPendingSiteFrequencyMHzChanged(double value) => ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
    partial void OnPendingSiteTxPowerDbmChanged(double value) => ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
    partial void OnPendingSiteSpreadingFactorChanged(string value) => ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
    partial void OnSelectedPendingSiteColorChanged(PropagationSiteColorOptionViewModel? value)
    {
        ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PendingScenarioSitePoint));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnPendingSiteXChanged(double? value)
    {
        ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PendingSiteCoordinateText));
        OnPropertyChanged(nameof(PendingScenarioSitePoint));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnPendingSiteZChanged(double? value)
    {
        ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PendingSiteCoordinateText));
        OnPropertyChanged(nameof(PendingScenarioSitePoint));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnIsPendingSiteDialogVisibleChanged(bool value)
    {
        ConfirmPendingSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PendingSiteDialogTitle));
        OnPropertyChanged(nameof(PendingSiteConfirmText));
        OnPropertyChanged(nameof(PendingScenarioSitePoint));
        MapWorkbench?.OnScenarioSitesChanged();
    }
    partial void OnIsDeleteSiteDialogVisibleChanged(bool value) => ConfirmDeleteSiteCommand.NotifyCanExecuteChanged();
    partial void OnEditingScenarioSiteIdChanged(string? value)
    {
        OnPropertyChanged(nameof(PendingSiteDialogTitle));
        OnPropertyChanged(nameof(PendingSiteConfirmText));
    }
    partial void OnSitePendingDeletionIdChanged(string? value)
    {
        OnPropertyChanged(nameof(DeleteSiteDialogMessage));
        ConfirmDeleteSiteCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTaskChanged(PropagationTaskItemViewModel? value)
    {
        if (value is null)
        {
            OnPropertyChanged(nameof(HasSelectedResult));
            ExportResultCommand.NotifyCanExecuteChanged();
            return;
        }

        if (_resultsByRunId.TryGetValue(value.RunId, out var result))
        {
            ApplyResultToPanels(result);
        }
        else
        {
            DataVersionTag = value.DataVersionTag;
            ParameterHashTag = value.ParameterHashTag;
            SelectedRunIdText = value.RunId;
            SelectedRunStatusText = value.StateText;
        }

        OnPropertyChanged(nameof(HasSelectedResult));
        ExportResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPropagationModeChanged(PropagationMode value)
    {
        var loc = LocalizationService.Instance;
        SelectedModeText = value switch
        {
            PropagationMode.CoverageMap => loc.GetString("Ui.Propagation.Mode.Coverage"),
            PropagationMode.InterferenceAnalysis => loc.GetString("Ui.Propagation.Mode.Interference"),
            PropagationMode.RelayOptimization => loc.GetString("Ui.Propagation.Mode.Relay"),
            PropagationMode.AdvancedModeling => loc.GetString("Ui.Propagation.Mode.Advanced"),
            PropagationMode.CalibrationValidation => loc.GetString("Ui.Propagation.Mode.Calibration"),
            _ => loc.GetString("Ui.Propagation.Mode.Coverage"),
        };

        ModeHintText = value switch
        {
            PropagationMode.CoverageMap => loc.GetString("Ui.Propagation.ModeHint.Coverage"),
            PropagationMode.InterferenceAnalysis => loc.GetString("Ui.Propagation.ModeHint.Interference"),
            PropagationMode.RelayOptimization => loc.GetString("Ui.Propagation.ModeHint.Relay"),
            PropagationMode.AdvancedModeling => loc.GetString("Ui.Propagation.ModeHint.Advanced"),
            PropagationMode.CalibrationValidation => loc.GetString("Ui.Propagation.ModeHint.Calibration"),
            _ => loc.GetString("Ui.Propagation.ModeHint.Coverage"),
        };
    }

    partial void OnSelectedLayerOptionChanged(PropagationLayerOptionViewModel? value)
    {
        UnityActiveLayerText = value?.Label ?? "--";
    }

    partial void OnUnityCameraXChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraYChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraZChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraYawChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraPitchChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraRollChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();
    partial void OnUnityCameraFovChanged(double? value) => SyncUnityCameraCommand.NotifyCanExecuteChanged();

    partial void OnIsSimulationRunningChanged(bool value)
    {
        RunSimulationCommand.NotifyCanExecuteChanged();
        PauseSimulationCommand.NotifyCanExecuteChanged();
        CancelSimulationCommand.NotifyCanExecuteChanged();
        OptimizeRelaysCommand.NotifyCanExecuteChanged();
        AnalyzeInterferenceCommand.NotifyCanExecuteChanged();
        RunMonteCarloCommand.NotifyCanExecuteChanged();
        ExportResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSimulationPausedChanged(bool value)
    {
        PauseButtonText = value
            ? LocalizationService.Instance.GetString("Ui.Propagation.Button.Resume")
            : LocalizationService.Instance.GetString("Ui.Propagation.Button.Pause");
    }

    public void RefreshLocalization()
    {
        OnSelectedPropagationModeChanged(SelectedPropagationMode);
        PauseButtonText = IsSimulationPaused
            ? LocalizationService.Instance.GetString("Ui.Propagation.Button.Resume")
            : LocalizationService.Instance.GetString("Ui.Propagation.Button.Pause");

        if (!IsSimulationRunning && string.IsNullOrWhiteSpace(SimulationStateText))
        {
            SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Idle");
        }

        foreach (var task in ActiveTasks)
        {
            task.StateText = LocalizeTaskState(task.State);
        }

        RefreshLayerOptions();
        RefreshScenarioSiteRoleOptions();
        RefreshScenarioSiteColorOptions();
        RefreshCameraPresets();
        UpdateUnityViewportOverlay();
    }

    private bool CanRunSimulation() => !IsSimulationRunning;
    private bool CanPauseSimulation() => IsSimulationRunning;
    private bool CanCancelSimulation() => IsSimulationRunning;
    private bool CanExportResult() => HasSelectedResult && !IsSimulationRunning;
    private bool CanSyncUnityBridge() => _unityBridge.IsAttached && (HasSelectedResult || IsSimulationRunning);
    private bool CanSyncUnityCamera() => _unityBridge.IsAttached && UnityCameraX.HasValue;

    private void MarkParametersDirty()
    {
        if (_suppressDirtyFlag)
            return;

        IsParametersDirty = true;
        OnPropertyChanged(nameof(SelectedScenarioSiteCoverageRadiusM));
        ScheduleAutoRun();
    }

    private void ScheduleAutoRun()
    {
        if (!HasRunnableScenarioSites())
            return;

        CancelScheduledAutoRun();

        var debounceCts = new CancellationTokenSource();
        _autoRunDebounceCts = debounceCts;
        _ = ExecuteAutoRunAsync(debounceCts);
    }

    private void CancelScheduledAutoRun()
    {
        _autoRunDebounceCts?.Cancel();
        _autoRunDebounceCts?.Dispose();
        _autoRunDebounceCts = null;
    }

    private async Task ExecuteAutoRunAsync(CancellationTokenSource debounceCts)
    {
        try
        {
            await Task.Delay(AutoRunDebounceMilliseconds, debounceCts.Token);

            while (IsSimulationRunning)
                await Task.Delay(100, debounceCts.Token);

            if (debounceCts.Token.IsCancellationRequested ||
                !ReferenceEquals(_autoRunDebounceCts, debounceCts) ||
                !IsParametersDirty ||
                IsPendingSiteDialogVisible ||
                IsDeleteSiteDialogVisible ||
                !HasRunnableScenarioSites())
            {
                return;
            }

            await RunSimulationAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_autoRunDebounceCts, debounceCts))
            {
                debounceCts.Dispose();
                _autoRunDebounceCts = null;
            }
        }
    }

    private void SetCoverageMode() => SelectedPropagationMode = PropagationMode.CoverageMap;
    private void SetInterferenceMode() => SelectedPropagationMode = PropagationMode.InterferenceAnalysis;
    private void SetRelayMode() => SelectedPropagationMode = PropagationMode.RelayOptimization;
    private void SetAdvancedMode() => SelectedPropagationMode = PropagationMode.AdvancedModeling;
    private void SetCalibrationMode() => SelectedPropagationMode = PropagationMode.CalibrationValidation;

    private async Task RunSimulationAsync()
    {
        CancelScheduledAutoRun();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        ClearSimulationIssue();

        if (_simulationService is GrpcPropagationSimulationService)
        {
            var loc = LocalizationService.Instance;
            var endpoint = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_GRPC_ENDPOINT") ?? "http://127.0.0.1:51051";
            var serviceReady = await _propagationServiceProcessManager.EnsureServiceReadyAsync(endpoint, token);
            if (!serviceReady.IsReady)
            {
                ReportSimulationFailure(
                    title: loc.GetString("Ui.Propagation.Error.ServiceUnavailableTitle"),
                    action: loc.GetString("Ui.Propagation.Error.ServiceUnavailableAction"),
                    detail: string.Format(
                        loc.GetString("Ui.Propagation.Error.ServiceUnavailableDetail"),
                        endpoint,
                        serviceReady.StatusCode,
                        LocalizePropagationServiceStartDetail(serviceReady)),
                    rawCode: $"propagation.grpc.preflight.{serviceReady.StatusCode}");
                return;
            }
        }

        if (!HasRunnableScenarioSites())
        {
            var loc = LocalizationService.Instance;
            ReportSimulationFailure(
                title: loc.GetString("Ui.Propagation.Error.SitesMissingTitle"),
                action: loc.GetString("Ui.Propagation.Error.SitesMissingAction"),
                detail: loc.GetString("Ui.Propagation.Error.SitesMissingDetail"),
                rawCode: "propagation.sites.missing");
            return;
        }

        IsSimulationRunning = true;
        IsSimulationPaused = false;
        CurrentStageText = string.Empty;
        SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Running");
        SimulationMessage = string.Empty;
        _activeRunId = null;

        PropagationTaskItemViewModel? task = null;

        try
        {
            var request = await BuildRequestAsync(SelectedPropagationMode, sourceRunId: null, token);
            var handle = await _simulationService.StartSimulationAsync(request, token);
            _activeRunId = handle.RunId;

            task = new PropagationTaskItemViewModel
            {
                RunId = handle.RunId,
                TaskName = handle.RunId,
                ModeText = SelectedModeText,
                State = ToTaskState(handle.InitialState),
                StateText = LocalizeTaskState(ToTaskState(handle.InitialState)),
                StageText = "queued",
                ProgressPercent = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
                StartedText = DateTimeOffset.UtcNow.ToLocalTime().ToString("HH:mm:ss"),
                DurationText = "--",
                CacheText = "cache: --",
            };
            ActiveTasks.Insert(0, task);
            SelectedTask = task;

            await foreach (var update in _simulationService.StreamSimulationUpdatesAsync(handle.RunId, token))
            {
                ApplyProgressUpdate(task, update);
                if (update.State is PropagationJobState.Canceled or PropagationJobState.Failed or PropagationJobState.Completed)
                    break;
            }

            if (task.State == PropagationTaskState.Completed)
            {
                var result = await _simulationService.GetSimulationResultAsync(handle.RunId, token);
                _resultsByRunId[handle.RunId] = result;
                ApplyResultToTask(task, result);
                ApplyResultToPanels(result);
                IsParametersDirty = false;
                SimulationMessage = string.Empty;
            }
            else if (task.State == PropagationTaskState.Canceled)
            {
                SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Canceled");
            }
            else if (task.State == PropagationTaskState.Failed)
            {
                var loc = LocalizationService.Instance;
                ReportSimulationFailure(
                    title: loc.GetString("Ui.Propagation.Error.SolveFailedTitle"),
                    action: loc.GetString("Ui.Propagation.Error.SolveFailedAction"),
                    detail: loc.GetString("Status.Propagation.FailedHint"),
                    rawCode: "propagation.failed.terminal");
            }
        }
        catch (OperationCanceledException)
        {
            if (task is not null)
            {
                task.State = PropagationTaskState.Canceled;
                task.StateText = LocalizeTaskState(task.State);
                task.DurationText = (DateTimeOffset.UtcNow - task.StartedAtUtc).ToString(@"mm\:ss");
            }
            SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Canceled");
        }
        catch (RpcException ex)
        {
            if (task is not null)
            {
                task.State = PropagationTaskState.Failed;
                task.StateText = LocalizeTaskState(task.State);
                task.DurationText = (DateTimeOffset.UtcNow - task.StartedAtUtc).ToString(@"mm\:ss");
            }

            var endpoint = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_GRPC_ENDPOINT") ?? "http://127.0.0.1:51051";
            var loc = LocalizationService.Instance;
            if (ex.StatusCode == StatusCode.Unavailable)
            {
                ReportSimulationFailure(
                    title: loc.GetString("Ui.Propagation.Error.ServiceUnavailableTitle"),
                    action: loc.GetString("Ui.Propagation.Error.RpcUnavailableAction"),
                    detail: string.Format(
                        loc.GetString("Ui.Propagation.Error.RpcUnavailableDetail"),
                        endpoint,
                        ex.Message),
                    rawCode: "propagation.grpc.unavailable");
            }
            else
            {
                ReportSimulationFailure(
                    title: string.Format(
                        loc.GetString("Ui.Propagation.Error.RpcStatusTitle"),
                        ex.StatusCode),
                    action: loc.GetString("Ui.Propagation.Error.RpcStatusAction"),
                    detail: ex.Message,
                    rawCode: $"propagation.grpc.{ex.StatusCode.ToString().ToLowerInvariant()}");
            }
        }
        catch (Exception ex)
        {
            if (task is not null)
            {
                task.State = PropagationTaskState.Failed;
                task.StateText = LocalizeTaskState(task.State);
                task.DurationText = (DateTimeOffset.UtcNow - task.StartedAtUtc).ToString(@"mm\:ss");
            }

            var loc = LocalizationService.Instance;
            ReportSimulationFailure(
                title: string.Format(
                    loc.GetString("Ui.Propagation.Error.RuntimeExceptionTitle"),
                    ex.GetType().Name),
                action: loc.GetString("Ui.Propagation.Error.RuntimeExceptionAction"),
                detail: ex.Message,
                rawCode: "propagation.runtime.exception");
        }
        finally
        {
            IsSimulationRunning = false;
            IsSimulationPaused = false;
            PauseSimulationCommand.NotifyCanExecuteChanged();
            CancelSimulationCommand.NotifyCanExecuteChanged();
            ExportResultCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearSimulationIssue()
    {
        SimulationIssueTitle = string.Empty;
        SimulationIssueAction = string.Empty;
        SimulationIssueDetail = string.Empty;
    }

    private void ReportSimulationFailure(string title, string action, string detail, string rawCode)
    {
        SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Failed");
        SimulationIssueTitle = title;
        SimulationIssueAction = action;
        SimulationIssueDetail = detail;
        SimulationMessage = $"{title}\n{action}\n{detail}";
        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = Microsoft.Extensions.Logging.LogLevel.Error,
            Message = $"[Propagation] {title} | {action} | {detail}",
            RawCode = rawCode,
        });
    }

    private string LocalizePropagationServiceStartDetail(PropagationServiceStartResult startResult)
    {
        var loc = LocalizationService.Instance;
        return startResult.StatusCode switch
        {
            "invalid-endpoint" => string.Format(
                loc.GetString("Ui.Propagation.Error.Detail.InvalidEndpoint"),
                startResult.Detail),
            "remote-endpoint-unreachable" => string.Format(
                loc.GetString("Ui.Propagation.Error.Detail.RemoteEndpointUnreachable"),
                startResult.Detail),
            "managed-process-unhealthy" => loc.GetString("Ui.Propagation.Error.Detail.ManagedProcessUnhealthy"),
            "service-binary-missing" => loc.GetString("Ui.Propagation.Error.Detail.ServiceBinaryMissing"),
            "service-start-failed" => loc.GetString("Ui.Propagation.Error.Detail.ServiceStartFailed"),
            "service-start-timeout" => loc.GetString("Ui.Propagation.Error.Detail.ServiceStartTimeout"),
            "service-start-exception" => string.Format(
                loc.GetString("Ui.Propagation.Error.Detail.ServiceStartException"),
                startResult.Detail),
            _ => startResult.Detail,
        };
    }


    private async Task TogglePauseSimulationAsync()
    {
        if (string.IsNullOrWhiteSpace(_activeRunId))
            return;

        if (!IsSimulationPaused)
        {
            await _simulationService.PauseSimulationAsync(_activeRunId, CancellationToken.None);
            IsSimulationPaused = true;
            SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Paused");
            SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.PausedHint");
            return;
        }

        await _simulationService.ResumeSimulationAsync(_activeRunId, CancellationToken.None);
        IsSimulationPaused = false;
        SimulationStateText = LocalizationService.Instance.GetString("Status.Propagation.Running");
        SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.Resumed");
    }

    private async Task CancelSimulationAsync()
    {
        if (string.IsNullOrWhiteSpace(_activeRunId))
            return;
        await _simulationService.CancelSimulationAsync(_activeRunId, CancellationToken.None);
    }

    private async Task RunRelayOptimizationAsync()
    {
        SelectedPropagationMode = PropagationMode.RelayOptimization;
        SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.OptimizeQueued");
        await RunSimulationAsync();
    }

    private async Task RunInterferenceAnalysisAsync()
    {
        SelectedPropagationMode = PropagationMode.InterferenceAnalysis;
        SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.InterferenceQueued");
        await RunSimulationAsync();
    }

    private async Task RunAdvancedMonteCarloAsync()
    {
        SelectedPropagationMode = PropagationMode.AdvancedModeling;
        UseMonteCarlo = true;
        MonteCarloIterations = Math.Max(MonteCarloIterations, 200);
        SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.MonteCarloQueued");
        await RunSimulationAsync();
    }

    private async Task ExportResultAsync()
    {
        if (SelectedTask is null || !_resultsByRunId.ContainsKey(SelectedTask.RunId))
        {
            SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.NoResultSelected");
            return;
        }

        var export = await _simulationService.ExportResultAsync(SelectedTask.RunId, string.Empty, CancellationToken.None);
        SimulationMessage = $"{LocalizationService.Instance.GetString("Status.Propagation.ExportReady")} {export.ExportPath}";
    }

    private async Task AttachUnityViewportAsync()
    {
        await EnsureUnityViewportAttachedAsync(CancellationToken.None);
    }

    private async Task SyncUnityBridgeAsync()
    {
        if (!_unityBridge.IsAttached)
        {
            SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.UnityNotAttached");
            return;
        }

        var runId = SelectedTask?.RunId ?? _activeRunId ?? $"preview_{DateTimeOffset.UtcNow:HHmmss}";
        var request = await BuildRequestAsync(SelectedPropagationMode, SelectedTask?.RunId, CancellationToken.None);
        await TryPushRequestToUnityAsync(runId, request, CancellationToken.None);

        if (SelectedTask is not null && _resultsByRunId.TryGetValue(SelectedTask.RunId, out var result))
        {
            await TryPushResultToUnityAsync(result, CancellationToken.None);
        }

        await SetActiveLayerAsync(SelectedLayerOption);
        await SyncUnityCameraAsync();
    }

    private async Task SyncUnityCameraAsync()
    {
        if (!_unityBridge.IsAttached)
            return;

        var cameraState = BuildCameraState();
        if (cameraState is null)
            return;

        var runId = SelectedTask?.RunId ?? _activeRunId ?? string.Empty;
        var ack = await _unityBridge.SetCameraStateAsync(cameraState, runId, CancellationToken.None);
        UnityLastAckText = $"{ack.Action} {ack.TimestampUtc:HH:mm:ss} {ack.Detail}";
    }

    private async Task<PropagationSimulationRequest> BuildRequestAsync(
        PropagationMode mode,
        string? sourceRunId,
        CancellationToken cancellationToken)
    {
        if (_mapViewModel is null)
            throw new InvalidOperationException("Propagation map context is not attached.");

        var terrainInput = await _terrainInputBuilder.BuildForCurrentViewportAsync(_mapViewModel, ScenarioSites, cancellationToken);
        return new PropagationSimulationRequest
        {
            RequestId = $"req_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
            Mode = mode switch
            {
                PropagationMode.CoverageMap => PropagationSimulationMode.CoverageMap,
                PropagationMode.InterferenceAnalysis => PropagationSimulationMode.InterferenceAnalysis,
                PropagationMode.RelayOptimization => PropagationSimulationMode.RelayOptimization,
                PropagationMode.AdvancedModeling => PropagationSimulationMode.AdvancedModeling,
                PropagationMode.CalibrationValidation => PropagationSimulationMode.AdvancedModeling,
                _ => PropagationSimulationMode.CoverageMap,
            },
            SourceRunId = sourceRunId,
            OptimizationAlgorithm = SelectedOptimizationAlgorithm,
            ScenarioPresetName = SelectedPresetName,
            Sites = ScenarioSites.ToArray(),
            TerrainInput = terrainInput,
            FrequencyMHz = FrequencyMHz,
            TxPowerDbm = TxPowerDbm,
            UplinkSpreadingFactor = SpreadingFactor,
            DownlinkSpreadingFactor = SpreadingFactor,
            EnvironmentLossDb = EnvironmentLossDb,
            VegetationAlphaSparse = VegetationAlphaSparse,
            VegetationAlphaDense = VegetationAlphaDense,
            ShadowSigmaDb = ShadowSigmaDb,
            ReflectionCoeff = ReflectionCoeff,
            EnableMonteCarlo = UseMonteCarlo,
            MonteCarloIterations = MonteCarloIterations,
            DemVersion = "earthdata_nasadem_hgt_001",
            LandcoverVersion = "landcover_unavailable_bare_ground_assumed_v1",
            SurfaceVersion = "viewport_dem_grid_3857_v1",
        };
    }

    public void BeginScenarioSitePlacement(double x, double z)
    {
        var nextRole = _scenarioSites.Any(site => site.Role == PropagationSiteRole.BaseStation)
            ? PropagationSiteRole.TargetNode
            : PropagationSiteRole.BaseStation;
        var targetIndex = _scenarioSites.Count(site => site.Role == PropagationSiteRole.TargetNode) + 1;

        EditingScenarioSiteId = null;
        SelectedPendingSiteRole = ScenarioSiteRoleOptions.FirstOrDefault(option => option.Role == nextRole);
        PendingSiteLabel = nextRole == PropagationSiteRole.BaseStation
            ? LocalizationService.Instance.GetString("Ui.Propagation.Site.DefaultBaseLabel")
            : LocalizationService.Instance.Format("Ui.Propagation.Site.DefaultNodeLabel", targetIndex);
        PendingSiteAntennaHeightM = nextRole == PropagationSiteRole.BaseStation ? 24 : 14;
        PendingSiteFrequencyMHz = FrequencyMHz;
        PendingSiteTxPowerDbm = TxPowerDbm;
        PendingSiteSpreadingFactor = SpreadingFactor;
        SelectedPendingSiteColor = ResolveDefaultColorOption(nextRole);
        PendingSiteX = x;
        PendingSiteZ = z;
        IsPendingSiteDialogVisible = true;
        SelectedScenarioSiteId = null;
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public void BeginScenarioSiteEdit(string siteId)
    {
        var site = _scenarioSites.FirstOrDefault(item => string.Equals(item.Id, siteId, StringComparison.Ordinal));
        if (site is null)
            return;

        EditingScenarioSiteId = site.Id;
        SelectedPendingSiteRole = ScenarioSiteRoleOptions.FirstOrDefault(option => option.Role == site.Role);
        PendingSiteLabel = site.Label;
        PendingSiteAntennaHeightM = site.AntennaHeightM;
        PendingSiteFrequencyMHz = site.FrequencyMHz > 0 ? site.FrequencyMHz : FrequencyMHz;
        PendingSiteTxPowerDbm = site.TxPowerDbm != 0 ? site.TxPowerDbm : TxPowerDbm;
        PendingSiteSpreadingFactor = string.IsNullOrWhiteSpace(site.SpreadingFactor) ? SpreadingFactor : site.SpreadingFactor;
        SelectedPendingSiteColor = ResolveColorOption(site.ColorHex, site.Role);
        PendingSiteX = site.X;
        PendingSiteZ = site.Z;
        IsPendingSiteDialogVisible = true;
        SelectedScenarioSiteId = site.Id;
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public void RequestDeleteScenarioSite(string siteId)
    {
        if (_scenarioSites.All(site => !string.Equals(site.Id, siteId, StringComparison.Ordinal)))
            return;

        SitePendingDeletionId = siteId;
        IsDeleteSiteDialogVisible = true;
    }

    public void MoveScenarioSite(string siteId, double x, double z)
    {
        var site = ResolveScenarioSite(siteId);
        if (site is null)
            return;

        var index = _scenarioSites.IndexOf(site);
        if (index < 0)
            return;

        _scenarioSites[index] = new PropagationSiteInput
        {
            Id = site.Id,
            Label = site.Label,
            Role = site.Role,
            ColorHex = site.ColorHex,
            X = x,
            Z = z,
            ElevationM = site.ElevationM,
            AntennaHeightM = site.AntennaHeightM,
            FrequencyMHz = site.FrequencyMHz,
            TxPowerDbm = site.TxPowerDbm,
            SpreadingFactor = site.SpreadingFactor,
        };

        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        SelectedScenarioSiteId = site.Id;
        OnPropertyChanged(nameof(ScenarioSites));
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public void SetScenarioSiteRole(string siteId, PropagationSiteRole role)
    {
        var site = ResolveScenarioSite(siteId);
        if (site is null || site.Role == role)
            return;

        var index = _scenarioSites.IndexOf(site);
        if (index < 0)
            return;

        if (role == PropagationSiteRole.BaseStation)
        {
            var existingBase = _scenarioSites.FirstOrDefault(item => item.Role == PropagationSiteRole.BaseStation && !string.Equals(item.Id, siteId, StringComparison.Ordinal));
            if (existingBase is not null)
            {
                var existingBaseIndex = _scenarioSites.IndexOf(existingBase);
                if (existingBaseIndex >= 0)
                {
                    _scenarioSites[existingBaseIndex] = new PropagationSiteInput
                    {
                        Id = existingBase.Id,
                        Label = existingBase.Label,
                        Role = PropagationSiteRole.TargetNode,
                        ColorHex = existingBase.ColorHex == "#7BEA49" ? "#4AA3FF" : existingBase.ColorHex,
                        X = existingBase.X,
                        Z = existingBase.Z,
                        ElevationM = existingBase.ElevationM,
                        AntennaHeightM = existingBase.AntennaHeightM,
                        FrequencyMHz = existingBase.FrequencyMHz,
                        TxPowerDbm = existingBase.TxPowerDbm,
                        SpreadingFactor = existingBase.SpreadingFactor,
                    };
                }
            }
        }

        _scenarioSites[index] = new PropagationSiteInput
        {
            Id = site.Id,
            Label = site.Label,
            Role = role,
            ColorHex = site.ColorHex == ResolveDefaultColor(site.Role) ? ResolveDefaultColor(role) : site.ColorHex,
            X = site.X,
            Z = site.Z,
            ElevationM = site.ElevationM,
            AntennaHeightM = site.AntennaHeightM,
            FrequencyMHz = site.FrequencyMHz,
            TxPowerDbm = site.TxPowerDbm,
            SpreadingFactor = site.SpreadingFactor,
        };

        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        SelectedScenarioSiteId = site.Id;
        OnPropertyChanged(nameof(ScenarioSites));
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public void DuplicateScenarioSite(string siteId)
    {
        var source = ResolveScenarioSite(siteId);
        if (source is null)
            return;

        var targetIndex = _scenarioSites.Count(site => site.Role == PropagationSiteRole.TargetNode) + 1;
        var duplicateRole = source.Role == PropagationSiteRole.BaseStation
            ? PropagationSiteRole.TargetNode
            : source.Role;
        var duplicate = new PropagationSiteInput
        {
            Id = $"node_{targetIndex:00}",
            Label = LocalizationService.Instance.Format("Ui.Propagation.Site.CopyLabel", source.Label),
            Role = duplicateRole,
            ColorHex = source.ColorHex,
            X = source.X + 90,
            Z = source.Z + 90,
            ElevationM = source.ElevationM,
            AntennaHeightM = source.AntennaHeightM,
            FrequencyMHz = source.FrequencyMHz,
            TxPowerDbm = source.TxPowerDbm,
            SpreadingFactor = source.SpreadingFactor,
        };

        _scenarioSites.Add(duplicate);
        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        SelectedScenarioSiteId = duplicate.Id;
        OnPropertyChanged(nameof(ScenarioSites));
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public void SelectScenarioSite(string? siteId)
    {
        SelectedScenarioSiteId = ResolveScenarioSite(siteId)?.Id;
    }

    public bool IsScenarioSiteBaseStation(string? siteId)
    {
        return ResolveScenarioSite(siteId)?.Role == PropagationSiteRole.BaseStation;
    }

    public bool HasScenarioSite(string? siteId)
    {
        return ResolveScenarioSite(siteId) is not null;
    }

    public void ClearScenarioSites()
    {
        if (_scenarioSites.Count == 0)
            return;

        _scenarioSites.Clear();
        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        SelectedScenarioSiteId = null;
        OnPropertyChanged(nameof(ScenarioSites));
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    public PropagationScenePoint? PendingScenarioSitePoint
    {
        get
        {
            if (!IsPendingSiteDialogVisible || !PendingSiteX.HasValue || !PendingSiteZ.HasValue)
                return null;

            return new PropagationScenePoint
            {
                Id = "pending_site",
                Label = PendingSiteLabel,
                ColorHex = SelectedPendingSiteColor?.ColorHex ?? "#FF8C42",
                X = PendingSiteX.Value,
                Z = PendingSiteZ.Value,
            };
        }
    }

    public double? SelectedScenarioSiteCoverageRadiusM
    {
        get
        {
            var site = ResolveScenarioSite(SelectedScenarioSiteId);
            if (site is null)
                return null;

            var thresholdDbm = site.Role == PropagationSiteRole.BaseStation ? -112d : -118d;
            var frequencyMHz = site.FrequencyMHz > 0 ? site.FrequencyMHz : FrequencyMHz;
            var txPowerDbm = site.TxPowerDbm != 0 ? site.TxPowerDbm : TxPowerDbm;
            var spreadingFactor = string.IsNullOrWhiteSpace(site.SpreadingFactor) ? SpreadingFactor : site.SpreadingFactor;
            var spreadingGainDb = ResolveSpreadingFactorGainDb(spreadingFactor);
            var antennaBonusDb = Math.Clamp((site.AntennaHeightM - 10d) * 0.35d, -2d, 8d);
            var vegetationPenaltyDb = Math.Max(VegetationAlphaSparse, VegetationAlphaDense) * 3.5d;
            var shadowPenaltyDb = ShadowSigmaDb * 0.45d;
            var reflectionPenaltyDb = Math.Abs(ReflectionCoeff - 0.35d) * 4d;
            var lossBudgetDb =
                txPowerDbm +
                spreadingGainDb +
                antennaBonusDb -
                EnvironmentLossDb -
                vegetationPenaltyDb -
                shadowPenaltyDb -
                reflectionPenaltyDb -
                thresholdDbm;

            var distanceKm = Math.Pow(10d, (lossBudgetDb - 32.44d - (20d * Math.Log10(Math.Max(frequencyMHz, 1d)))) / 20d);
            var distanceM = Math.Clamp(distanceKm * 1000d, 90d, 1800d);

            if (SelectedPropagationMode == PropagationMode.InterferenceAnalysis)
                distanceM *= 0.82d;
            else if (SelectedPropagationMode == PropagationMode.RelayOptimization)
                distanceM *= 0.9d;

            return Math.Round(distanceM, 1);
        }
    }

    public string PendingSiteDialogTitle => LocalizationService.Instance.GetString(
        string.IsNullOrWhiteSpace(EditingScenarioSiteId)
            ? "Ui.Propagation.SiteDialog.AddTitle"
            : "Ui.Propagation.SiteDialog.EditTitle");
    public string PendingSiteCoordinateText => PendingSiteX.HasValue && PendingSiteZ.HasValue
        ? LocalizationService.Instance.Format("Ui.Propagation.SiteDialog.Coordinates", PendingSiteX.Value, PendingSiteZ.Value)
        : "--";
    public string PendingSiteConfirmText => LocalizationService.Instance.GetString(
        string.IsNullOrWhiteSpace(EditingScenarioSiteId)
            ? "Ui.Propagation.SiteDialog.ConfirmAdd"
            : "Ui.Propagation.SiteDialog.ConfirmSave");
    public string DeleteSiteDialogTitle => LocalizationService.Instance.GetString("Ui.Propagation.DeleteDialog.Title");
    public string DeleteSiteDialogMessage
    {
        get
        {
            var site = ResolveScenarioSite(SitePendingDeletionId);
            return site is null
                ? LocalizationService.Instance.GetString("Ui.Propagation.DeleteDialog.Fallback")
                : LocalizationService.Instance.Format("Ui.Propagation.DeleteDialog.Message", site.Label);
        }
    }

    private bool HasRunnableScenarioSites()
    {
        return _scenarioSites.Any(site => site.Role == PropagationSiteRole.BaseStation)
            && _scenarioSites.Any(site => site.Role == PropagationSiteRole.TargetNode);
    }

    private void ConfirmPendingScenarioSite()
    {
        if (!CanConfirmPendingScenarioSite())
            return;

        var role = SelectedPendingSiteRole!.Role;
        var targetIndex = _scenarioSites.Count(site => site.Role == PropagationSiteRole.TargetNode) + 1;
        var existingSite = ResolveScenarioSite(EditingScenarioSiteId);
        var site = new PropagationSiteInput
        {
            Id = string.IsNullOrWhiteSpace(EditingScenarioSiteId)
                ? (role == PropagationSiteRole.BaseStation ? "base" : $"node_{targetIndex:00}")
                : EditingScenarioSiteId!,
            Label = PendingSiteLabel.Trim(),
            Role = role,
            ColorHex = SelectedPendingSiteColor?.ColorHex ?? ResolveDefaultColor(role),
            X = PendingSiteX!.Value,
            Z = PendingSiteZ!.Value,
            ElevationM = existingSite?.ElevationM,
            AntennaHeightM = PendingSiteAntennaHeightM,
            FrequencyMHz = PendingSiteFrequencyMHz,
            TxPowerDbm = PendingSiteTxPowerDbm,
            SpreadingFactor = PendingSiteSpreadingFactor,
        };

        if (string.IsNullOrWhiteSpace(EditingScenarioSiteId))
        {
            _scenarioSites.Add(site);
        }
        else
        {
            var existingIndex = _scenarioSites
                .Select((item, index) => new { item, index })
                .FirstOrDefault(pair => string.Equals(pair.item.Id, EditingScenarioSiteId, StringComparison.Ordinal))
                ?.index ?? -1;

            if (existingIndex >= 0)
            {
                _scenarioSites[existingIndex] = site;
            }
        }

        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        SelectedScenarioSiteId = site.Id;
        OnPropertyChanged(nameof(ScenarioSites));
        CancelPendingScenarioSite();
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    private bool CanConfirmPendingScenarioSite()
    {
        return IsPendingSiteDialogVisible
            && SelectedPendingSiteRole is not null
            && SelectedPendingSiteColor is not null
            && PendingSiteX.HasValue
            && PendingSiteZ.HasValue
            && PendingSiteAntennaHeightM > 0
            && PendingSiteFrequencyMHz > 0
            && !string.IsNullOrWhiteSpace(PendingSiteSpreadingFactor)
            && !string.IsNullOrWhiteSpace(PendingSiteLabel);
    }

    private void CancelPendingScenarioSite()
    {
        IsPendingSiteDialogVisible = false;
        PendingSiteLabel = string.Empty;
        PendingSiteAntennaHeightM = 14;
        PendingSiteFrequencyMHz = FrequencyMHz;
        PendingSiteTxPowerDbm = TxPowerDbm;
        PendingSiteSpreadingFactor = SpreadingFactor;
        PendingSiteX = null;
        PendingSiteZ = null;
        SelectedPendingSiteRole = null;
        SelectedPendingSiteColor = null;
        EditingScenarioSiteId = null;
        MapWorkbench?.OnScenarioSitesChanged();
    }

    private void ConfirmDeleteScenarioSite()
    {
        if (!CanConfirmDeleteScenarioSite())
            return;

        var site = ResolveScenarioSite(SitePendingDeletionId);
        if (site is null)
        {
            CancelDeleteScenarioSite();
            return;
        }

        _scenarioSites.Remove(site);
        _preferScenarioSiteScene = true;
        MarkParametersDirty();
        if (string.Equals(SelectedScenarioSiteId, site.Id, StringComparison.Ordinal))
            SelectedScenarioSiteId = null;
        OnPropertyChanged(nameof(ScenarioSites));
        CancelDeleteScenarioSite();
        ControlPanel?.OnScenarioSitesChanged();
        MapWorkbench?.OnScenarioSitesChanged();
    }

    private bool CanConfirmDeleteScenarioSite()
    {
        return IsDeleteSiteDialogVisible && !string.IsNullOrWhiteSpace(SitePendingDeletionId);
    }

    private void CancelDeleteScenarioSite()
    {
        SitePendingDeletionId = null;
        IsDeleteSiteDialogVisible = false;
    }

    private void RefreshScenarioSiteRoleOptions()
    {
        var previousRole = SelectedPendingSiteRole?.Role;
        var loc = LocalizationService.Instance;

        ScenarioSiteRoleOptions.Clear();
        ScenarioSiteRoleOptions.Add(new PropagationSiteRoleOptionViewModel(PropagationSiteRole.BaseStation, loc.GetString("Ui.Propagation.SiteRole.Base")));
        ScenarioSiteRoleOptions.Add(new PropagationSiteRoleOptionViewModel(PropagationSiteRole.TargetNode, loc.GetString("Ui.Propagation.SiteRole.Target")));

        if (previousRole.HasValue)
        {
            SelectedPendingSiteRole = ScenarioSiteRoleOptions.FirstOrDefault(option => option.Role == previousRole.Value);
        }
    }

    private void RefreshScenarioSiteColorOptions()
    {
        var previousHex = SelectedPendingSiteColor?.ColorHex;
        var loc = LocalizationService.Instance;

        ScenarioSiteColorOptions.Clear();
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#7BEA49", loc.GetString("Ui.Propagation.SiteColor.Green")));
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#4AA3FF", loc.GetString("Ui.Propagation.SiteColor.Blue")));
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#FF6B6B", loc.GetString("Ui.Propagation.SiteColor.Red")));
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#F0D14A", loc.GetString("Ui.Propagation.SiteColor.Yellow")));
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#D8A8FF", loc.GetString("Ui.Propagation.SiteColor.Purple")));
        ScenarioSiteColorOptions.Add(new PropagationSiteColorOptionViewModel("#FF8C42", loc.GetString("Ui.Propagation.SiteColor.Orange")));

        if (!string.IsNullOrWhiteSpace(previousHex))
        {
            SelectedPendingSiteColor = ScenarioSiteColorOptions.FirstOrDefault(option => string.Equals(option.ColorHex, previousHex, StringComparison.OrdinalIgnoreCase));
        }
    }

    private PropagationSiteColorOptionViewModel ResolveDefaultColorOption(PropagationSiteRole role)
    {
        var colorHex = ResolveDefaultColor(role);
        return ResolveColorOption(colorHex, role);
    }

    private PropagationSiteColorOptionViewModel ResolveColorOption(string? colorHex, PropagationSiteRole role)
    {
        var resolved = string.IsNullOrWhiteSpace(colorHex) ? ResolveDefaultColor(role) : colorHex;
        return ScenarioSiteColorOptions.FirstOrDefault(option => string.Equals(option.ColorHex, resolved, StringComparison.OrdinalIgnoreCase))
               ?? ScenarioSiteColorOptions.First();
    }

    private static string ResolveDefaultColor(PropagationSiteRole role)
    {
        return role == PropagationSiteRole.BaseStation ? "#7BEA49" : "#4AA3FF";
    }

    private static double ResolveSpreadingFactorGainDb(string spreadingFactor)
    {
        if (string.IsNullOrWhiteSpace(spreadingFactor))
            return 0;

        return spreadingFactor.Trim().ToUpperInvariant() switch
        {
            "SF7" => 0,
            "SF8" => 1.5,
            "SF9" => 3,
            "SF10" => 4.5,
            "SF11" => 6,
            "SF12" => 7.5,
            _ => 0,
        };
    }

    private PropagationSiteInput? ResolveScenarioSite(string? siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return null;

        return _scenarioSites.FirstOrDefault(site => string.Equals(site.Id, siteId, StringComparison.Ordinal));
    }

    private void ApplyProgressUpdate(PropagationTaskItemViewModel task, PropagationSimulationUpdate update)
    {
        task.State = ToTaskState(update.State);
        task.StateText = LocalizeTaskState(task.State);
        task.StageText = update.Stage;
        task.ProgressPercent = update.ProgressPercent;
        task.CacheText = update.CacheHit
            ? LocalizationService.Instance.GetString("Status.Propagation.CacheHit")
            : LocalizationService.Instance.GetString("Status.Propagation.CacheMiss");
        task.DurationText = (DateTimeOffset.UtcNow - task.StartedAtUtc).ToString(@"mm\:ss");

        IsSimulationPaused = task.State == PropagationTaskState.Paused;
        SimulationStateText = task.StateText;
        CurrentStageText = update.Stage;
        if (!string.IsNullOrWhiteSpace(update.Message) && task.State is PropagationTaskState.Paused or PropagationTaskState.Canceled)
        {
            SimulationMessage = update.Message;
        }
    }

    private void ApplyResultToTask(PropagationTaskItemViewModel task, PropagationSimulationResult result)
    {
        task.State = ToTaskState(result.RunMeta.Status);
        task.StateText = LocalizeTaskState(task.State);
        task.ProgressPercent = result.RunMeta.ProgressPercent;
        task.CacheText = result.RunMeta.CacheHit
            ? LocalizationService.Instance.GetString("Status.Propagation.CacheHit")
            : LocalizationService.Instance.GetString("Status.Propagation.CacheMiss");
        task.DurationText = TimeSpan.FromMilliseconds(result.RunMeta.DurationMs).ToString(@"mm\:ss");
        task.DataVersionTag = $"{result.Provenance.DatasetBundle.DemVersion} / {result.Provenance.DatasetBundle.LandcoverVersion}";
        task.ParameterHashTag = result.Provenance.ParameterHash;
    }

    private void ApplyResultToPanels(PropagationSimulationResult result)
    {
        _suppressDirtyFlag = true;
        try
        {
            SelectedRunIdText = result.RunMeta.RunId;
            SelectedRunStatusText = result.RunMeta.Status.ToString();

            DownlinkRssiDbm = result.AnalysisOutputs.Link.DownlinkRssiDbm;
            UplinkRssiDbm = result.AnalysisOutputs.Link.UplinkRssiDbm;
            DownlinkMarginDb = result.AnalysisOutputs.Link.DownlinkMarginDb;
            UplinkMarginDb = result.AnalysisOutputs.Link.UplinkMarginDb;
            IsLinkFeasible = result.AnalysisOutputs.Link.LinkFeasible;

            Reliability95 = result.AnalysisOutputs.Reliability.P95;
            Reliability80 = result.AnalysisOutputs.Reliability.P80;
            Coverage95AreaKm2 = result.AnalysisOutputs.CoverageProbability.AreaP95Km2;
            Coverage80AreaKm2 = result.AnalysisOutputs.CoverageProbability.AreaP80Km2;

            FsplDb = result.AnalysisOutputs.LossBreakdown.FsplDb;
            DiffractionLossDb = result.AnalysisOutputs.LossBreakdown.DiffractionDb;
            VegetationLossDb = result.AnalysisOutputs.LossBreakdown.VegetationDb;
            ReflectionLossDb = result.AnalysisOutputs.LossBreakdown.ReflectionDb;
            ShadowLossDb = result.AnalysisOutputs.LossBreakdown.ShadowDb;

            SinrDb = result.AnalysisOutputs.Network.SinrDb;
            ConflictRate = result.AnalysisOutputs.Network.ConflictRate;
            MaxCapacity = result.AnalysisOutputs.Network.MaxCapacityNodes;

            ProfileDistanceKm = result.AnalysisOutputs.Profile.DistanceKm;
            ProfileFresnelRadiusM = result.AnalysisOutputs.Profile.FresnelRadiusM;
            ProfileMarginDb = result.AnalysisOutputs.Profile.MarginDb;
            ProfileMainObstacleLabel = result.AnalysisOutputs.Profile.MainObstacle.Label;
            ProfileMainObstacleV = result.AnalysisOutputs.Profile.MainObstacle.V;
            ProfileMainObstacleLdDb = result.AnalysisOutputs.Profile.MainObstacle.LdDb;

            DataVersionTag = $"{result.Provenance.DatasetBundle.DemVersion} / {result.Provenance.DatasetBundle.LandcoverVersion}";
            ModelVersionTag = result.Provenance.ModelVersion;
            ParameterHashTag = result.Provenance.ParameterHash;

            AssumptionFlagsText = result.QualityFlags.AssumptionFlags.Count == 0
                ? "--"
                : string.Join(" | ", result.QualityFlags.AssumptionFlags);
            ValidityWarningsText = result.QualityFlags.ValidityWarnings.Count == 0
                ? LocalizationService.Instance.GetString("Status.Propagation.NoValidityWarning")
                : string.Join(" | ", result.QualityFlags.ValidityWarnings);
            OutputLayerUrisText = string.Join(
                " | ",
                new[]
                {
                    result.ModelOutputs.MeanCoverageRasterUri,
                    result.ModelOutputs.Reliability95RasterUri,
                    result.ModelOutputs.InterferenceRasterUri,
                }.Where(static s => !string.IsNullOrWhiteSpace(s)));

            SimulationStateText = LocalizeTaskState(ToTaskState(result.RunMeta.Status));
            CurrentStageText = "completed";
        }
        finally
        {
            _suppressDirtyFlag = false;
        }
    }

    private async Task TryPushRequestToUnityAsync(
        string runId,
        PropagationSimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_unityBridge.IsAttached)
        {
            UnityLastAckText = LocalizationService.Instance.GetString("Status.Propagation.UnityNotAttached");
            return;
        }

        var ack = await _unityBridge.PushSimulationRequestAsync(runId, request, cancellationToken);
        UnityLastAckText = $"{ack.Action} {ack.TimestampUtc:HH:mm:ss} {ack.Detail}";
    }

    private async Task TryPushResultToUnityAsync(PropagationSimulationResult result, CancellationToken cancellationToken)
    {
        if (!_unityBridge.IsAttached)
            return;

        var ack = await _unityBridge.PushSimulationResultAsync(result, cancellationToken);
        UnityLastAckText = $"{ack.Action} {ack.TimestampUtc:HH:mm:ss} {ack.Detail}";
    }

    private PropagationTaskState ToTaskState(PropagationJobState state)
    {
        return state switch
        {
            PropagationJobState.Queued => PropagationTaskState.Queued,
            PropagationJobState.Running => PropagationTaskState.Running,
            PropagationJobState.Paused => PropagationTaskState.Paused,
            PropagationJobState.Completed => PropagationTaskState.Completed,
            PropagationJobState.Failed => PropagationTaskState.Failed,
            PropagationJobState.Canceled => PropagationTaskState.Canceled,
            _ => PropagationTaskState.Failed,
        };
    }

    private string LocalizeTaskState(PropagationTaskState state)
    {
        return state switch
        {
            PropagationTaskState.Queued => LocalizationService.Instance.GetString("Status.Propagation.Queued"),
            PropagationTaskState.Running => LocalizationService.Instance.GetString("Status.Propagation.Running"),
            PropagationTaskState.Paused => LocalizationService.Instance.GetString("Status.Propagation.Paused"),
            PropagationTaskState.Completed => LocalizationService.Instance.GetString("Status.Propagation.Completed"),
            PropagationTaskState.Canceled => LocalizationService.Instance.GetString("Status.Propagation.Canceled"),
            PropagationTaskState.Failed => LocalizationService.Instance.GetString("Status.Propagation.Failed"),
            _ => LocalizationService.Instance.GetString("Status.Propagation.Failed"),
        };
    }

    private void OnUnityBridgeStateChanged(object? sender, PropagationUnityBridgeStateChangedEventArgs e)
    {
        UnityBridgeStateText = e.IsAttached
            ? LocalizationService.Instance.GetString("Status.Propagation.UnityAttached")
            : LocalizationService.Instance.GetString("Status.Propagation.UnityDetached");
        IsUnityBridgeAttached = e.IsAttached;
        UnityBridgeTelemetryText = $"{e.Message} ({DateTimeOffset.UtcNow:HH:mm:ss})";
        if (e.IsAttached)
        {
            SimulationMessage = LocalizationService.Instance.GetString("Status.Propagation.UnityAttachDone");
        }
        UpdateUnityViewportOverlay();
        SyncUnityBridgeCommand.NotifyCanExecuteChanged();
        SyncUnityCameraCommand.NotifyCanExecuteChanged();
    }

    private void OnUnityBridgeTelemetryUpdated(object? sender, PropagationUnityBridgeTelemetryEventArgs e)
    {
        UnityBridgeTelemetryText = $"{e.EventType} {e.Message}".Trim();

        if (e.RttMs.HasValue)
        {
            UnityHeartbeatText = $"{e.RttMs.Value:F0} ms @ {e.TimestampUtc:HH:mm:ss}";
        }

        if (e.Attempt > 0 || e.EventType.StartsWith("reconnect", StringComparison.OrdinalIgnoreCase))
        {
            UnityReconnectText = e.Attempt > 0
                ? $"attempt {e.Attempt}: {e.EventType}"
                : e.EventType;
        }

        if (string.Equals(e.EventType, "reconnect_failed", StringComparison.OrdinalIgnoreCase))
        {
            SimulationMessage = $"Unity reconnect failed: {e.Message}";
        }

        if (string.Equals(e.EventType, "connect_failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.EventType, "heartbeat_timeout", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.EventType, "attach_timeout", StringComparison.OrdinalIgnoreCase))
        {
            SimulationMessage = e.Message;
            UpdateUnityViewportOverlay();
        }

        if (e.EventType.StartsWith("error_", StringComparison.OrdinalIgnoreCase))
        {
            SimulationMessage = $"Unity {e.EventType}: {e.Message}";
            if (e.EventType.Contains("layer", StringComparison.OrdinalIgnoreCase))
            {
                UnityLayerStatusText = LocalizationService.Instance.GetString("Ui.Propagation.LayerStatus.Failed");
                UnityLayerStatusColor = "#FF7373";
                UnityLayerProgressVisible = false;
                _isUnityLayerLoading = false;
                UpdateUnityViewportOverlay();
            }
        }

        if (e.EventType.StartsWith("interaction_", StringComparison.OrdinalIgnoreCase))
        {
            TryHandleInteractionTelemetry(e);
        }
    }

    private void OnUnityMapPointSelected(object? sender, PropagationUnityMapPointSelectedEventArgs e)
    {
        SelectedPointX = e.X;
        SelectedPointY = e.Y;
        SelectedNodeId = e.NodeId;
        UnitySelectedPointText = $"x={e.X:F1}, y={e.Y:F1}, node={e.NodeId}";
    }

    private void OnUnityProfileLineChanged(object? sender, PropagationUnityProfileLineChangedEventArgs e)
    {
        UnityProfileLineText = $"({e.StartX:F1},{e.StartY:F1}) -> ({e.EndX:F1},{e.EndY:F1})";
    }

    private void OnUnityLayerStateChanged(object? sender, PropagationUnityLayerStateChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.LayerId))
        {
            var match = LayerOptions.FirstOrDefault(option => option.Key == e.LayerId);
            UnityActiveLayerText = match?.Label ?? e.LayerId;
        }
        if (!string.IsNullOrWhiteSpace(e.Message))
        {
            UnityBridgeTelemetryText = e.Message;
        }

        UpdateLayerStatus(e);
    }

    private void OnUnityDiagnosticSnapshotReceived(object? sender, PropagationUnityDiagnosticSnapshotEventArgs e)
    {
        UnityDiagnosticFps = e.Fps;
        UnityDiagnosticFrameTimeP95Ms = e.FrameTimeP95Ms;
        UnityDiagnosticGpuMemoryMb = e.GpuMemoryMb;
        UnityDiagnosticLayerLoadMs = e.LayerLoadMs;
        UnityDiagnosticTileCacheHitRate = e.TileCacheHitRate;
        UnityDiagnosticText = $"{e.Message} @ {e.TimestampUtc:HH:mm:ss}";
        UpdateDiagnosticStatus(e);
    }

    private void OnUnityCameraStateChanged(object? sender, PropagationUnityCameraStateChangedEventArgs e)
    {
        UnityCameraX = e.CameraState.X;
        UnityCameraY = e.CameraState.Y;
        UnityCameraZ = e.CameraState.Z;
        UnityCameraYaw = e.CameraState.Yaw;
        UnityCameraPitch = e.CameraState.Pitch;
        UnityCameraRoll = e.CameraState.Roll;
        UnityCameraFov = e.CameraState.Fov;
        UnityCameraStateText = $"pos({e.CameraState.X:F1},{e.CameraState.Y:F1},{e.CameraState.Z:F1}) " +
                               $"rot({e.CameraState.Pitch:F1},{e.CameraState.Yaw:F1},{e.CameraState.Roll:F1}) " +
                               $"fov({e.CameraState.Fov:F0})";
        SyncUnityCameraCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLayerOptions()
    {
        var loc = LocalizationService.Instance;
        var previousKey = SelectedLayerOption?.Key;

        LayerOptions.Clear();
        LayerOptions.Add(new PropagationLayerOptionViewModel("coverage_mean", loc.GetString("Ui.Propagation.Layer.MeanCoverage")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("landcover", loc.GetString("Ui.Propagation.Layer.Landcover")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("reliability_95", loc.GetString("Ui.Propagation.Layer.Reliability95")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("reliability_80", loc.GetString("Ui.Propagation.Layer.Reliability80")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("interference", loc.GetString("Ui.Propagation.Layer.Interference")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("capacity", loc.GetString("Ui.Propagation.Layer.Capacity")));
        LayerOptions.Add(new PropagationLayerOptionViewModel("link_margin", loc.GetString("Ui.Propagation.Layer.Margin")));

        SelectedLayerOption = LayerOptions.FirstOrDefault(option => option.Key == previousKey)
            ?? LayerOptions.FirstOrDefault();
        UnityActiveLayerText = SelectedLayerOption?.Label ?? "--";
    }

    private void RefreshCameraPresets()
    {
        if (_suppressCameraPresetRefresh)
            return;

        var loc = LocalizationService.Instance;
        var previousKey = SelectedCameraPreset?.Key;

        _suppressCameraPresetRefresh = true;
        CameraPresets.Clear();
        CameraPresets.Add(new PropagationCameraPresetViewModel(
            "overview",
            loc.GetString("Ui.Propagation.CameraPreset.Overview"),
            x: 1200, y: 1800, z: 900,
            pitch: 20, yaw: 45, roll: 0, fov: 55));
        CameraPresets.Add(new PropagationCameraPresetViewModel(
            "ridge_focus",
            loc.GetString("Ui.Propagation.CameraPreset.RidgeFocus"),
            x: 1500, y: 2200, z: 700,
            pitch: 28, yaw: 35, roll: 0, fov: 50));
        CameraPresets.Add(new PropagationCameraPresetViewModel(
            "profile_follow",
            loc.GetString("Ui.Propagation.CameraPreset.ProfileFollow"),
            x: 1000, y: 1600, z: 600,
            pitch: 35, yaw: 60, roll: 0, fov: 60));

        SelectedCameraPreset = CameraPresets.FirstOrDefault(option => option.Key == previousKey)
            ?? CameraPresets.FirstOrDefault();
        _suppressCameraPresetRefresh = false;
    }

    private void ApplyDefaultCameraPreset()
    {
        if (SelectedCameraPreset is null)
            return;
        ApplyPresetValues(SelectedCameraPreset);
    }

    private void ApplyCameraPreset()
    {
        if (SelectedCameraPreset is null)
            return;
        ApplyPresetValues(SelectedCameraPreset);
    }

    private void ApplyPresetValues(PropagationCameraPresetViewModel preset)
    {
        UnityCameraX = preset.X;
        UnityCameraY = preset.Y;
        UnityCameraZ = preset.Z;
        UnityCameraPitch = preset.Pitch;
        UnityCameraYaw = preset.Yaw;
        UnityCameraRoll = preset.Roll;
        UnityCameraFov = preset.Fov;

        UnityCameraStateText = $"pos({preset.X:F1},{preset.Y:F1},{preset.Z:F1}) " +
                               $"rot({preset.Pitch:F1},{preset.Yaw:F1},{preset.Roll:F1}) " +
                               $"fov({preset.Fov:F0})";
        SyncUnityCameraCommand.NotifyCanExecuteChanged();
    }

    private async Task SetActiveLayerAsync(PropagationLayerOptionViewModel? option)
    {
        if (option is null)
            return;

        if (string.Equals(option.Key, "landcover", StringComparison.Ordinal))
            return;

        if (!_unityBridge.IsAttached)
        {
            UnityBridgeTelemetryText = LocalizationService.Instance.GetString("Status.Propagation.UnityNotAttached");
            return;
        }

        UnityLayerStatusText = LocalizationService.Instance.GetString("Ui.Propagation.LayerStatus.Requested");
        UnityLayerStatusColor = "#7CC5FF";
        UnityLayerLoadProgress = null;
        UnityLayerProgressVisible = false;
        UnityLayerTransitionMs = null;
        _isUnityLayerLoading = true;
        UpdateUnityViewportOverlay();

        var runId = SelectedTask?.RunId ?? _activeRunId ?? string.Empty;
        var ack = await _unityBridge.SetActiveLayerAsync(option.Key, runId, CancellationToken.None);
        UnityLastAckText = $"{ack.Action} {ack.TimestampUtc:HH:mm:ss} {ack.Detail}";
    }

    private void UpdateLayerStatus(PropagationUnityLayerStateChangedEventArgs e)
    {
        var state = string.IsNullOrWhiteSpace(e.State)
            ? "ready"
            : e.State.Trim().ToLowerInvariant();
        _isUnityLayerLoading = state is "loading" or "rendering";

        if (e.ProgressPercent.HasValue)
        {
            var progress = e.ProgressPercent.Value;
            if (progress > 0 && progress < 1)
            {
                progress *= 100;
            }
            else if (progress <= 1 && (state == "ready" || state == "active"))
            {
                progress = 100;
            }

            UnityLayerLoadProgress = progress;
            UnityLayerProgressVisible = state is "loading" or "rendering";
        }
        else
        {
            UnityLayerLoadProgress = null;
            UnityLayerProgressVisible = false;
        }

        UnityLayerTransitionMs = e.TransitionMs;

        var loc = LocalizationService.Instance;
        switch (state)
        {
            case "loading":
            case "rendering":
                UnityLayerStatusText = loc.GetString("Ui.Propagation.LayerStatus.Loading");
                UnityLayerStatusColor = "#FFCF48";
                break;
            case "ready":
            case "active":
                UnityLayerStatusText = loc.GetString("Ui.Propagation.LayerStatus.Ready");
                UnityLayerStatusColor = "#75E0A2";
                break;
            case "failed":
            case "error":
                UnityLayerStatusText = loc.GetString("Ui.Propagation.LayerStatus.Failed");
                UnityLayerStatusColor = "#FF7373";
                break;
            default:
                UnityLayerStatusText = state;
                UnityLayerStatusColor = "#9AA3AE";
                break;
        }

        UpdateUnityViewportOverlay();
    }

    private void TryHandleInteractionTelemetry(PropagationUnityBridgeTelemetryEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Message))
            return;

        try
        {
            using var doc = JsonDocument.Parse(e.Message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data))
                return;

            switch (e.EventType)
            {
                case "interaction_measurement_completed":
                    {
                        var distance = ReadJsonDouble(data, "distance_m");
                        if (distance.HasValue)
                            UnityProfileLineText = $"Measurement: {distance.Value:F1} m";
                        break;
                    }
                case "interaction_annotation_added":
                    {
                        var id = ReadJsonString(data, "id");
                        var x = ReadJsonDouble(data, "x");
                        var y = ReadJsonDouble(data, "y");
                        if (!string.IsNullOrWhiteSpace(id) && x.HasValue && y.HasValue)
                            UnitySelectedPointText = $"{id} @ x={x.Value:F1}, y={y.Value:F1}";
                        break;
                    }
                case "interaction_hotspot_stats":
                    {
                        var avg = ReadJsonDouble(data, "elevation_avg");
                        var min = ReadJsonDouble(data, "elevation_min");
                        var max = ReadJsonDouble(data, "elevation_max");
                        if (avg.HasValue && min.HasValue && max.HasValue)
                            UnityBridgeTelemetryText = $"hotspot elev avg={avg.Value:F1}m min={min.Value:F1}m max={max.Value:F1}m";
                        break;
                    }
                case "interaction_profile_curve_summary":
                    {
                        var distance = ReadJsonDouble(data, "distance_m");
                        var min = ReadJsonDouble(data, "elevation_min");
                        var max = ReadJsonDouble(data, "elevation_max");
                        if (distance.HasValue && min.HasValue && max.HasValue)
                            UnityProfileLineText = $"Profile {distance.Value:F1} m, elev {min.Value:F1}-{max.Value:F1} m";
                        break;
                    }
            }
        }
        catch
        {
            // Ignore malformed interaction payloads from custom Unity builds.
        }
    }

    private static string ReadJsonString(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static double? ReadJsonDouble(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number &&
                property.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private void UpdateUnityViewportOverlay()
    {
        var loc = LocalizationService.Instance;

        if (!IsUnityBridgeAttached)
        {
            UnityViewportOverlayVisible = true;
            UnityViewportOverlayTitle = loc.GetString("Ui.Propagation.ViewportOverlay.Detached");
            UnityViewportOverlayDetail = ResolveUnityViewportDetachedDetail(loc);
            return;
        }

        if (_isUnityLayerLoading)
        {
            UnityViewportOverlayVisible = true;
            UnityViewportOverlayTitle = loc.GetString("Ui.Propagation.ViewportOverlay.Loading");

            var detail = string.IsNullOrWhiteSpace(UnityActiveLayerText) ? "--" : UnityActiveLayerText;
            if (UnityLayerLoadProgress.HasValue)
            {
                detail = $"{detail} - {UnityLayerLoadProgress.Value:F0}%";
            }
            UnityViewportOverlayDetail = detail;
            return;
        }

        UnityViewportOverlayVisible = false;
        UnityViewportOverlayTitle = string.Empty;
        UnityViewportOverlayDetail = string.Empty;
    }

    private string ResolveUnityViewportDetachedDetail(LocalizationService loc)
    {
        var processMessage = string.IsNullOrWhiteSpace(_unityProcessMessage) ? string.Empty : _unityProcessMessage.Trim();
        var bridgeMessage = string.IsNullOrWhiteSpace(UnityBridgeTelemetryText) || UnityBridgeTelemetryText == "--"
            ? string.Empty
            : UnityBridgeTelemetryText.Trim();

        return _unityProcessState switch
        {
            PropagationUnityProcessState.ExternalManaged =>
                string.IsNullOrWhiteSpace(processMessage)
                    ? "Unity is in external-managed mode. Start the Unity player manually or set TRAILMATE_PROPAGATION_UNITY_EXECUTABLE."
                    : $"{processMessage} Start the Unity player manually or set TRAILMATE_PROPAGATION_UNITY_EXECUTABLE.",
            PropagationUnityProcessState.Starting =>
                string.IsNullOrWhiteSpace(processMessage)
                    ? "Starting Unity runtime..."
                    : processMessage,
            PropagationUnityProcessState.Faulted =>
                string.IsNullOrWhiteSpace(processMessage)
                    ? "Unity runtime failed to start."
                    : processMessage,
            PropagationUnityProcessState.Running when !string.IsNullOrWhiteSpace(bridgeMessage) => bridgeMessage,
            PropagationUnityProcessState.Stopping =>
                string.IsNullOrWhiteSpace(processMessage)
                    ? "Stopping Unity runtime..."
                    : processMessage,
            _ => loc.GetString("Ui.Propagation.ViewportOverlay.DetachedHint"),
        };
    }

    private PropagationUnityCameraState? BuildCameraState()
    {
        if (!UnityCameraX.HasValue || !UnityCameraY.HasValue || !UnityCameraZ.HasValue)
            return null;

        return new PropagationUnityCameraState
        {
            X = UnityCameraX.Value,
            Y = UnityCameraY.Value,
            Z = UnityCameraZ.Value,
            Pitch = UnityCameraPitch ?? 0,
            Yaw = UnityCameraYaw ?? 0,
            Roll = UnityCameraRoll ?? 0,
            Fov = UnityCameraFov ?? 55,
        };
    }

    private void UpdateDiagnosticStatus(PropagationUnityDiagnosticSnapshotEventArgs snapshot)
    {
        var fpsSeverity = EvaluateHighBetter(snapshot.Fps, ok: 50, warn: 35);
        var frameSeverity = EvaluateLowBetter(snapshot.FrameTimeP95Ms, ok: 33, warn: 50);
        var gpuSeverity = EvaluateLowBetter(snapshot.GpuMemoryMb, ok: 1000, warn: 1500);
        var layerSeverity = EvaluateLowBetter(snapshot.LayerLoadMs, ok: 250, warn: 500);
        var hitSeverity = EvaluateHighBetter(snapshot.TileCacheHitRate, ok: 0.75, warn: 0.5);

        UnityDiagnosticFpsColor = ResolveDiagnosticColor(fpsSeverity);
        UnityDiagnosticFrameTimeColor = ResolveDiagnosticColor(frameSeverity);
        UnityDiagnosticGpuColor = ResolveDiagnosticColor(gpuSeverity);
        UnityDiagnosticLayerLoadColor = ResolveDiagnosticColor(layerSeverity);
        UnityDiagnosticTileHitColor = ResolveDiagnosticColor(hitSeverity);

        var overall = MaxSeverity(fpsSeverity, frameSeverity, gpuSeverity, layerSeverity, hitSeverity);
        var loc = LocalizationService.Instance;
        UnityDiagnosticStatusText = overall switch
        {
            DiagnosticSeverity.Ok => loc.GetString("Ui.Propagation.DiagnosticStatus.Stable"),
            DiagnosticSeverity.Warn => loc.GetString("Ui.Propagation.DiagnosticStatus.Degraded"),
            _ => loc.GetString("Ui.Propagation.DiagnosticStatus.Critical"),
        };
        UnityDiagnosticStatusColor = ResolveDiagnosticColor(overall);
    }

    private static DiagnosticSeverity EvaluateHighBetter(double value, double ok, double warn)
    {
        if (value >= ok)
            return DiagnosticSeverity.Ok;
        if (value >= warn)
            return DiagnosticSeverity.Warn;
        return DiagnosticSeverity.Crit;
    }

    private static DiagnosticSeverity EvaluateLowBetter(double value, double ok, double warn)
    {
        if (value <= ok)
            return DiagnosticSeverity.Ok;
        if (value <= warn)
            return DiagnosticSeverity.Warn;
        return DiagnosticSeverity.Crit;
    }

    private static DiagnosticSeverity MaxSeverity(params DiagnosticSeverity[] severities)
    {
        var max = DiagnosticSeverity.Ok;
        foreach (var severity in severities)
        {
            if (severity > max)
                max = severity;
        }
        return max;
    }

    private static string ResolveDiagnosticColor(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Ok => "#75E0A2",
            DiagnosticSeverity.Warn => "#FFCF48",
            _ => "#FF7373",
        };
    }

    private enum DiagnosticSeverity
    {
        Ok = 0,
        Warn = 1,
        Crit = 2,
    }

    public async Task EnsureUnityViewportAttachedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unityBridge.AttachViewportAsync("propagation-main-slot", cancellationToken);
            if (!_unityBridge.IsAttached)
            {
                SimulationMessage = "Unity bridge connect timed out. Ensure Unity is running and the pipe is available.";
                UpdateUnityViewportOverlay();
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            SimulationMessage = "Unity bridge connect timed out. Ensure Unity is running and the pipe is available.";
            UpdateUnityViewportOverlay();
        }
        catch (TimeoutException)
        {
            SimulationMessage = "Unity bridge connect timed out. Ensure Unity is running and the pipe is available.";
            UpdateUnityViewportOverlay();
        }
        catch (Exception ex)
        {
            SimulationMessage = $"Unity bridge attach failed: {ex.Message}";
            UpdateUnityViewportOverlay();
        }
    }

    public async Task DisconnectUnityBridgeAsync(CancellationToken cancellationToken)
    {
        await _unityBridge.DisconnectAsync(cancellationToken);
    }

    public void ApplyUnityProcessSnapshot(PropagationUnityProcessSnapshot snapshot)
    {
        _unityProcessState = snapshot.ProcessState;
        _unityProcessMessage = snapshot.Message ?? string.Empty;
        UnityProcessStateText = snapshot.ProcessState.ToString();
        UnityProcessPidText = snapshot.ProcessId?.ToString() ?? "--";
        if (!string.IsNullOrWhiteSpace(snapshot.Message))
        {
            UnityBridgeTelemetryText = snapshot.Message;
        }
        UpdateUnityViewportOverlay();
    }

    public void ApplyUnityProcessState(PropagationUnityProcessStateChangedEventArgs state)
    {
        _unityProcessState = state.ProcessState;
        _unityProcessMessage = state.Message ?? string.Empty;
        UnityProcessStateText = state.ProcessState.ToString();
        UnityProcessPidText = state.ProcessId?.ToString() ?? "--";

        if (state.ProcessState == PropagationUnityProcessState.Faulted)
        {
            SimulationMessage = state.Message;
        }

        UpdateUnityViewportOverlay();
    }
}
