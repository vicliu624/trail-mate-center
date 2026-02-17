using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Services;
using TrailMateCenter.StateMachine;
using TrailMateCenter.Storage;
using TrailMateCenter.Styling;
using TrailMateCenter.Transport;

namespace TrailMateCenter.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const int DashboardTabIndex = 0;
    private const int ChatTabIndex = 1;
    private const int LogsTabIndex = 3;
    private const int EventsTabIndex = 4;
    private const int ExportTabIndex = 6;
    private static readonly string[] LocationMessageKeywords =
    [
        "shared their position",
        "shared location",
        "share location",
        "share your location",
        "位置",
        "分享位置",
        "共享位置",
    ];
    private static readonly HashSet<string> AutoSaveProperties = new(StringComparer.Ordinal)
    {
        nameof(AutoReconnect),
        nameof(AutoConnectOnDetect),
        nameof(ReplayFile),
        nameof(AckTimeoutMs),
        nameof(MaxRetries),
        nameof(OnlineThresholdSeconds),
        nameof(WeakThresholdSeconds),
        nameof(CommandChannel),
        nameof(ContoursEnabled),
        nameof(ContoursUltraFine),
        nameof(EarthdataToken),
        nameof(OfflineMode),
        nameof(MapShowLogs),
        nameof(MapShowMqtt),
        nameof(MapShowGibs),
        nameof(SelectedMapBaseLayer),
        nameof(AprsEnabled),
        nameof(AprsServerHost),
        nameof(AprsServerPort),
        nameof(AprsIgateCallsign),
        nameof(AprsIgateSsid),
        nameof(AprsPasscode),
        nameof(AprsToCall),
        nameof(AprsPath),
        nameof(AprsFilter),
        nameof(AprsTxMinIntervalSec),
        nameof(AprsDedupeWindowSec),
        nameof(AprsPositionIntervalSec),
        nameof(AprsSymbolTable),
        nameof(AprsSymbolCode),
        nameof(AprsUseCompressed),
        nameof(AprsEmitStatus),
        nameof(AprsEmitTelemetry),
        nameof(AprsEmitWeather),
        nameof(AprsEmitMessages),
        nameof(AprsEmitWaypoints),
    };
    private readonly HostLinkClient _client;
    private readonly MeshtasticMqttClient _meshtasticMqtt;
    private readonly AprsGatewayService _aprsGateway;
    private readonly AprsIsClient _aprsClient;
    private readonly ISerialPortEnumerator _portEnumerator;
    private readonly SqliteStore _sqliteStore;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _sessionStore;
    private readonly ILogger<MainWindowViewModel> _logger;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _presenceTimer;
    private readonly DispatcherTimer _portScanTimer;
    private readonly HashSet<MapCacheRegionViewModel> _trackedOfflineCacheRegions = new();
    private bool _autoConnectBusy;
    private DateTimeOffset _lastAutoConnectAttempt = DateTimeOffset.MinValue;
    private readonly HashSet<string> _knownPortNames = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastHotplugPortName;
    private readonly HashSet<uint> _subjectListeners = new();
    private uint? _selfNodeId;
    private bool _settingsLoaded;
    private DeviceInfo? _lastDeviceInfo;
    private StatusInfo? _lastStatusInfo;
    private AprsIsStatus? _lastAprsStatus;
    private AprsGatewayStats? _lastAprsStats;
    private TeamStateEvent? _lastTeamState;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = Array.Empty<object>();
    private string? _earthdataStatusKey;
    private object[] _earthdataStatusArgs = Array.Empty<object>();
    private string? _earthdataStatusDetail;
    private bool _earthdataTestBusy;
    private CancellationTokenSource? _settingsSaveDebounce;
    private bool _eventsHistoryLoadTriggered;
    private bool _eventsHistoryLoadInProgress;
    private bool _logsHistoryLoadTriggered;
    private bool _logsHistoryLoadInProgress;

    public MainWindowViewModel(
        HostLinkClient client,
        MeshtasticMqttClient meshtasticMqtt,
        AprsGatewayService aprsGateway,
        AprsIsClient aprsClient,
        ISerialPortEnumerator portEnumerator,
        SqliteStore sqliteStore,
        SettingsStore settingsStore,
        LogStore logStore,
        SessionStore sessionStore,
        ExportService exportService,
        ILogger<MainWindowViewModel> logger)
    {
        _client = client;
        _meshtasticMqtt = meshtasticMqtt;
        _aprsGateway = aprsGateway;
        _aprsClient = aprsClient;
        _portEnumerator = portEnumerator;
        _sqliteStore = sqliteStore;
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _logger = logger;

        Config = new ConfigViewModel(_client, logger);
        Logs = new LogsViewModel(logStore);
        Events = new EventsViewModel(sessionStore);
        RawFrames = new RawFramesViewModel(sessionStore);
        Export = new ExportViewModel(exportService, sessionStore);

        RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        RetryCommand = new AsyncRelayCommand(RetryAsync, CanRetry);
        QuickSendCommand = new AsyncRelayCommand(QuickSendAsync, CanQuickSend);
        SetTargetToSelectedSubjectCommand = new RelayCommand(SetTargetToSelectedSubject, CanSetTargetToSelectedSubject);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        AddMqttSourceCommand = new RelayCommand(AddMqttSource);
        RemoveMqttSourceCommand = new RelayCommand(RemoveSelectedMqttSource, CanRemoveSelectedMqttSource);
        SaveOfflineCacheRegionCommand = new RelayCommand(SaveOfflineCacheRegionFromSelection, CanSaveOfflineCacheRegionFromSelection);
        ApplyOfflineCacheRegionCommand = new RelayCommand(ApplySelectedOfflineCacheRegion, CanApplySelectedOfflineCacheRegion);
        BuildOfflineCacheRegionCommand = new AsyncRelayCommand(BuildSelectedOfflineCacheRegionAsync, CanBuildSelectedOfflineCacheRegion);
        RemoveOfflineCacheRegionCommand = new RelayCommand(RemoveSelectedOfflineCacheRegion, CanRemoveSelectedOfflineCacheRegion);
        TestEarthdataCommand = new AsyncRelayCommand(TestEarthdataAsync, CanTestEarthdata);
        RallyCommand = new AsyncRelayCommand(() => SendTeamCommandAsync(TeamCommandType.RallyTo));
        MoveCommand = new AsyncRelayCommand(() => SendTeamCommandAsync(TeamCommandType.MoveTo));
        HoldCommand = new AsyncRelayCommand(() => SendTeamCommandAsync(TeamCommandType.Hold));

        _client.ConnectionStateChanged += OnConnectionStateChanged;
        _client.MessageAdded += OnMessageAdded;
        _client.MessageUpdated += OnMessageUpdated;
        _client.DeviceInfoReceived += OnDeviceInfo;
        _client.StatusUpdated += OnStatusUpdated;
        _client.GpsUpdated += OnGpsUpdated;
        _client.PositionUpdated += OnPositionUpdated;
        _client.NodeInfoUpdated += OnNodeInfoUpdated;
        _client.TeamStateUpdated += OnTeamStateUpdated;
        _client.TacticalEventReceived += OnTacticalEventReceived;
        _meshtasticMqtt.MessageReceived += OnMqttMessageReceived;
        _meshtasticMqtt.PositionReceived += OnMqttPositionReceived;
        _meshtasticMqtt.NodeInfoReceived += OnMqttNodeInfoReceived;
        _meshtasticMqtt.TacticalEventReceived += OnMqttTacticalEventReceived;
        _aprsGateway.StatsChanged += OnAprsStatsChanged;
        _aprsClient.StatusChanged += OnAprsStatusChanged;

        var loc = LocalizationService.Instance;
        ConnectionStateText = loc.GetString("Status.Connection.Disconnected");
        AprsConnectionText = loc.GetString("Status.Aprs.Disconnected");

        Languages.Add(new LanguageOptionViewModel("en-US", "Ui.Language.English"));
        Languages.Add(new LanguageOptionViewModel("zh-CN", "Ui.Language.Chinese"));
        foreach (var theme in ThemeService.BuiltInThemes)
        {
            Themes.Add(new ThemeOptionViewModel(theme));
        }
        MapBaseLayers.Add(new MapBaseLayerOptionViewModel(MapBaseLayerKind.Osm, "Ui.Dashboard.BaseMap.Osm"));
        MapBaseLayers.Add(new MapBaseLayerOptionViewModel(MapBaseLayerKind.Terrain, "Ui.Dashboard.BaseMap.Terrain"));
        MapBaseLayers.Add(new MapBaseLayerOptionViewModel(MapBaseLayerKind.Satellite, "Ui.Dashboard.BaseMap.Satellite"));
        SelectedMapBaseLayer = MapBaseLayers.FirstOrDefault();
        SelectedLanguage = Languages.FirstOrDefault(l => l.Culture.Name == loc.CurrentCulture.Name)
            ?? Languages.FirstOrDefault(l => l.Culture.TwoLetterISOLanguageName == loc.CurrentCulture.TwoLetterISOLanguageName)
            ?? Languages.FirstOrDefault();
        SelectedTheme = Themes.FirstOrDefault(t => t.Definition.Id == ThemeService.Instance.CurrentTheme.Id)
            ?? Themes.FirstOrDefault();
        LocalizationService.Instance.CultureChanged += (_, _) => RefreshLocalization();

        LoadMqttSources(null);
        _ = LoadSettingsAsync();
        _ = RefreshPortsAsync();

        Map.EnableCluster = MapEnableCluster;
        Map.FollowLatest = MapFollowLatest;
        Map.SetMqttVisibility(MapShowMqtt);
        Map.SetGibsVisibility(MapShowGibs);
        Map.PropertyChanged += OnMapPropertyChanged;

        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _presenceTimer.Tick += (_, _) => RefreshSubjectStatuses();
        _presenceTimer.Start();

        LoadSeverityRules(new Dictionary<TacticalEventKind, TacticalSeverity>());

        _portScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _portScanTimer.Tick += async (_, _) => await AutoScanAsync();

        SelectedChannel = Channels.FirstOrDefault();

        LoadHistoryFromSession();

        PropertyChanged += OnSettingsPropertyChanged;

        Teams.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTeams));
            OnPropertyChanged(nameof(HasNoTeams));
        };
    }

    public ObservableCollection<SerialPortInfoViewModel> Ports { get; } = new();
    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
    public ObservableCollection<MessageItemViewModel> FilteredMessages { get; } = new();
    public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new();
    public ObservableCollection<ThemeOptionViewModel> Themes { get; } = new();
    public ObservableCollection<MapBaseLayerOptionViewModel> MapBaseLayers { get; } = new();
    public ObservableCollection<ChannelOptionViewModel> Channels { get; } = new()
    {
        new ChannelOptionViewModel(0),
        new ChannelOptionViewModel(1),
        new ChannelOptionViewModel(2),
    };
    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = new();
    public ObservableCollection<TacticalEventViewModel> TacticalEvents { get; } = new();
    public ObservableCollection<SubjectViewModel> Subjects { get; } = new();
    public ObservableCollection<TeamGroupViewModel> Teams { get; } = new();
    public ObservableCollection<SubjectViewModel> UngroupedSubjects { get; } = new();
    public ObservableCollection<SeverityRuleViewModel> SeverityRules { get; } = new();
    public ObservableCollection<MqttSourceViewModel> MqttSources { get; } = new();
    public ObservableCollection<MapCacheRegionViewModel> OfflineCacheRegions { get; } = new();

    public MapViewModel Map { get; } = new();

    public ConfigViewModel Config { get; }
    public LogsViewModel Logs { get; }
    public EventsViewModel Events { get; }
    public RawFramesViewModel RawFrames { get; }
    public ExportViewModel Export { get; }

    [ObservableProperty]
    private SerialPortInfoViewModel? _selectedPort;

    [ObservableProperty]
    private bool _useReplay;

    [ObservableProperty]
    private string _replayFile = string.Empty;

    [ObservableProperty]
    private bool _autoReconnect = true;

    [ObservableProperty]
    private bool _autoConnectOnDetect;

    [ObservableProperty]
    private string _connectionStateText = string.Empty;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _target = string.Empty;

    [ObservableProperty]
    private ChannelOptionViewModel? _selectedChannel;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedConversation;

    [ObservableProperty]
    private string _outgoingText = string.Empty;

    [ObservableProperty]
    private MessageItemViewModel? _selectedMessage;

    [ObservableProperty]
    private string _deviceInfo = string.Empty;

    [ObservableProperty]
    private string _statusPanel = string.Empty;

    [ObservableProperty]
    private string _capabilitiesInfo = string.Empty;

    [ObservableProperty]
    private int _ackTimeoutMs = 1500;

    [ObservableProperty]
    private int _maxRetries = 2;

    [ObservableProperty]
    private int _onlineThresholdSeconds = 30;

    [ObservableProperty]
    private int _weakThresholdSeconds = 120;

    [ObservableProperty]
    private int _commandChannel = 0;

    [ObservableProperty]
    private int _commandRadiusMeters = 50;

    [ObservableProperty]
    private int _commandPriority = 0;

    [ObservableProperty]
    private string _commandNote = string.Empty;

    [ObservableProperty]
    private bool _mapFollowLatest;

    [ObservableProperty]
    private bool _mapEnableCluster = true;

    [ObservableProperty]
    private bool _contoursEnabled = true;

    [ObservableProperty]
    private bool _contoursUltraFine;

    [ObservableProperty]
    private string _earthdataToken = string.Empty;

    [ObservableProperty]
    private bool _offlineMode;

    [ObservableProperty]
    private bool _mapShowLogs;

    [ObservableProperty]
    private bool _mapShowMqtt = true;

    [ObservableProperty]
    private bool _mapShowGibs;

    [ObservableProperty]
    private MapBaseLayerOptionViewModel? _selectedMapBaseLayer;

    [ObservableProperty]
    private MqttSourceViewModel? _selectedMqttSource;

    [ObservableProperty]
    private MapCacheRegionViewModel? _selectedOfflineCacheRegion;

    [ObservableProperty]
    private string _offlineCacheRegionName = string.Empty;

    [ObservableProperty]
    private bool _isOfflineCacheHealthRefreshing;

    [ObservableProperty]
    private bool _isOfflineCacheExporting;

    [ObservableProperty]
    private string _offlineCacheExportStatusText = string.Empty;

    [ObservableProperty]
    private bool _isMessagePanelExpanded = true;

    [ObservableProperty]
    private SubjectViewModel? _selectedSubject;

    [ObservableProperty]
    private string _selectedSubjectDisplayId = "--";

    [ObservableProperty]
    private TacticalEventViewModel? _selectedTacticalEvent;

    [ObservableProperty]
    private string _quickMessageText = string.Empty;

    [ObservableProperty]
    private bool _hasSelectedSubject;

    [ObservableProperty]
    private bool _aprsEnabled;

    [ObservableProperty]
    private string _aprsServerHost = string.Empty;

    [ObservableProperty]
    private int _aprsServerPort = 14580;

    [ObservableProperty]
    private string _aprsIgateCallsign = string.Empty;

    [ObservableProperty]
    private int _aprsIgateSsid;

    [ObservableProperty]
    private string _aprsPasscode = string.Empty;

    [ObservableProperty]
    private string _aprsToCall = string.Empty;

    [ObservableProperty]
    private string _aprsPath = string.Empty;

    [ObservableProperty]
    private string _aprsFilter = string.Empty;

    [ObservableProperty]
    private int _aprsTxMinIntervalSec = 30;

    [ObservableProperty]
    private int _aprsDedupeWindowSec = 30;

    [ObservableProperty]
    private int _aprsPositionIntervalSec = 60;

    [ObservableProperty]
    private string _aprsSymbolTable = "/";

    [ObservableProperty]
    private string _aprsSymbolCode = ">";

    [ObservableProperty]
    private bool _aprsUseCompressed = true;

    [ObservableProperty]
    private bool _aprsEmitStatus = true;

    [ObservableProperty]
    private bool _aprsEmitTelemetry = true;

    [ObservableProperty]
    private bool _aprsEmitWeather = true;

    [ObservableProperty]
    private bool _aprsEmitMessages = true;

    [ObservableProperty]
    private bool _aprsEmitWaypoints = true;

    [ObservableProperty]
    private string _aprsConnectionText = string.Empty;

    [ObservableProperty]
    private string _aprsConnectionColor = "#9AA3AE";

    [ObservableProperty]
    private string _aprsStatsText = string.Empty;

    [ObservableProperty]
    private string _earthdataStatusText = string.Empty;

    [ObservableProperty]
    private string _earthdataStatusColor = "#9AA3AE";

    public bool HasNoSelectedSubject => !HasSelectedSubject;

    [ObservableProperty]
    private LanguageOptionViewModel? _selectedLanguage;

    [ObservableProperty]
    private ThemeOptionViewModel? _selectedTheme;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private int _unreadChatCount;

    public bool HasUnreadChat => UnreadChatCount > 0;
    public bool CanUseExternalFeeds => !OfflineMode;
    public bool CanContinueSelectedOfflineCacheRegion => CanBuildRegion(SelectedOfflineCacheRegion);
    public bool CanExportSelectedOfflineCacheRegion => SelectedOfflineCacheRegion is not null &&
                                                      !Map.IsOfflineCacheRunning &&
                                                      !IsOfflineCacheExporting;
    public bool HasTeams => Teams.Count > 0;
    public bool HasNoTeams => Teams.Count == 0;
    public bool SupportsTeamAppPosting => _client.SupportsTxAppDataCommand;

    public IAsyncRelayCommand RefreshPortsCommand { get; }
    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand DisconnectCommand { get; }
    public IAsyncRelayCommand SendCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand QuickSendCommand { get; }
    public IRelayCommand SetTargetToSelectedSubjectCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IRelayCommand AddMqttSourceCommand { get; }
    public IRelayCommand RemoveMqttSourceCommand { get; }
    public IRelayCommand SaveOfflineCacheRegionCommand { get; }
    public IRelayCommand ApplyOfflineCacheRegionCommand { get; }
    public IAsyncRelayCommand BuildOfflineCacheRegionCommand { get; }
    public IRelayCommand RemoveOfflineCacheRegionCommand { get; }
    public IAsyncRelayCommand TestEarthdataCommand { get; }
    public IAsyncRelayCommand RallyCommand { get; }
    public IAsyncRelayCommand MoveCommand { get; }
    public IAsyncRelayCommand HoldCommand { get; }

    partial void OnSelectedLanguageChanged(LanguageOptionViewModel? value)
    {
        if (value is null)
            return;

        var current = LocalizationService.Instance.CurrentCulture;
        if (string.Equals(current.Name, value.Culture.Name, StringComparison.OrdinalIgnoreCase))
            return;

        LocalizationService.Instance.ApplyCulture(value.Culture);

        if (_settingsLoaded)
        {
            _ = SaveSettingsAsync();
        }
    }

    partial void OnSelectedThemeChanged(ThemeOptionViewModel? value)
    {
        if (value is null)
            return;

        if (ThemeService.Instance.CurrentTheme.Id == value.Definition.Id)
            return;

        ThemeService.Instance.ApplyTheme(value.Definition);

        if (_settingsLoaded)
        {
            _ = SaveSettingsAsync();
        }
    }

    partial void OnContoursEnabledChanged(bool value)
    {
        Map.UpdateContourSettings(BuildContourSettings());
    }

    partial void OnContoursUltraFineChanged(bool value)
    {
        Map.UpdateContourSettings(BuildContourSettings());
    }

    partial void OnEarthdataTokenChanged(string value)
    {
        Map.UpdateContourSettings(BuildContourSettings());
        ScheduleSettingsSave();
    }

    private void ScheduleSettingsSave()
    {
        if (!_settingsLoaded)
            return;

        _settingsSaveDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _settingsSaveDebounce = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await SaveSettingsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-save settings");
                }
            });
        });
    }

    private void SetLastError(string message)
    {
        _lastErrorKey = null;
        _lastErrorArgs = Array.Empty<object>();
        LastError = message;
    }

    private void SetLastErrorLocalized(string key, params object[] args)
    {
        _lastErrorKey = key;
        _lastErrorArgs = args ?? Array.Empty<object>();
        LastError = _lastErrorArgs.Length == 0
            ? LocalizationService.Instance.GetString(key)
            : LocalizationService.Instance.Format(key, _lastErrorArgs);
    }

    private void SetEarthdataStatus(string key, string color, string? detail, params object[] args)
    {
        _earthdataStatusKey = key;
        _earthdataStatusArgs = args ?? Array.Empty<object>();
        _earthdataStatusDetail = detail;
        EarthdataStatusColor = color;
        EarthdataStatusText = _earthdataStatusArgs.Length == 0
            ? LocalizationService.Instance.GetString(key)
            : LocalizationService.Instance.Format(key, _earthdataStatusArgs);
        if (!string.IsNullOrWhiteSpace(_earthdataStatusDetail))
        {
            EarthdataStatusText = $"{EarthdataStatusText}\n{_earthdataStatusDetail}";
        }
    }

    private void SetEarthdataStatus(string key, string color, params object[] args)
    {
        SetEarthdataStatus(key, color, null, args);
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync(CancellationToken.None);
            AutoReconnect = _settings.AutoReconnect;
            AutoConnectOnDetect = _settings.AutoConnectOnDetect;
            ReplayFile = _settings.LastReplayFile ?? string.Empty;
            AckTimeoutMs = _settings.AckTimeoutMs;
            MaxRetries = _settings.MaxRetries;

            OnlineThresholdSeconds = _settings.Tactical.OnlineThresholdSeconds;
            WeakThresholdSeconds = _settings.Tactical.WeakThresholdSeconds;
            CommandChannel = _settings.Tactical.CommandChannel;
            LoadSeverityRules(_settings.Tactical.SeverityOverrides);

            AprsEnabled = _settings.Aprs.Enabled;
            AprsServerHost = _settings.Aprs.ServerHost;
            AprsServerPort = _settings.Aprs.ServerPort;
            AprsIgateCallsign = _settings.Aprs.IgateCallsign;
            AprsIgateSsid = _settings.Aprs.IgateSsid;
            AprsPasscode = _settings.Aprs.Passcode;
            AprsToCall = _settings.Aprs.ToCall;
            AprsPath = _settings.Aprs.Path;
            AprsFilter = _settings.Aprs.Filter;
            AprsTxMinIntervalSec = _settings.Aprs.TxMinIntervalSec;
            AprsDedupeWindowSec = _settings.Aprs.DedupeWindowSec;
            AprsPositionIntervalSec = _settings.Aprs.PositionIntervalSec;
            AprsSymbolTable = _settings.Aprs.SymbolTable.ToString();
            AprsSymbolCode = _settings.Aprs.SymbolCode.ToString();
            AprsUseCompressed = _settings.Aprs.UseCompressed;
            AprsEmitStatus = _settings.Aprs.EmitStatus;
            AprsEmitTelemetry = _settings.Aprs.EmitTelemetry;
            AprsEmitWeather = _settings.Aprs.EmitWeather;
            AprsEmitMessages = _settings.Aprs.EmitMessages;
            AprsEmitWaypoints = _settings.Aprs.EmitWaypoints;

            OfflineMode = _settings.Ui.OfflineMode;
            _aprsGateway.ApplySettings(BuildEffectiveAprsSettings(_settings.Aprs));

            ContoursEnabled = _settings.Contours.Enabled;
            ContoursUltraFine = _settings.Contours.EnableUltraFine;
            EarthdataToken = _settings.Contours.Earthdata.Token;
            Map.UpdateContourSettings(_settings.Contours);

            if (!string.IsNullOrWhiteSpace(_settings.Ui.Language))
            {
                var language = Languages.FirstOrDefault(l => string.Equals(l.Culture.Name, _settings.Ui.Language, StringComparison.OrdinalIgnoreCase))
                    ?? Languages.FirstOrDefault(l => string.Equals(l.Culture.TwoLetterISOLanguageName, _settings.Ui.Language, StringComparison.OrdinalIgnoreCase));
                if (language is not null)
                {
                    SelectedLanguage = language;
                }
            }

            if (!string.IsNullOrWhiteSpace(_settings.Ui.Theme))
            {
                var theme = Themes.FirstOrDefault(t => string.Equals(t.Definition.Id, _settings.Ui.Theme, StringComparison.OrdinalIgnoreCase));
                if (theme is not null)
                {
                    SelectedTheme = theme;
                }
            }

            MapShowLogs = _settings.Ui.ShowMapLogs;
            MapShowMqtt = _settings.Ui.ShowMapMqtt;
            MapShowGibs = _settings.Ui.ShowMapGibs;
            var desiredBaseLayer = ParseMapBaseLayer(_settings.Ui.MapBaseLayer);
            SelectedMapBaseLayer = MapBaseLayers.FirstOrDefault(item => item.Kind == desiredBaseLayer)
                ?? MapBaseLayers.FirstOrDefault();
            var mqttSettings = await LoadMqttSettingsAsync(CancellationToken.None);
            _settings = _settings with { Mqtt = mqttSettings };
            LoadMqttSources(mqttSettings.Sources);
            _meshtasticMqtt.ApplySettings(BuildEffectiveMqttSettings(mqttSettings));

            var cacheRegions = await _sqliteStore.LoadMapCacheRegionsAsync(CancellationToken.None);
            LoadOfflineCacheRegions(cacheRegions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings");
        }
        finally
        {
            _settingsLoaded = true;
        }
    }

    private async Task RefreshPortsAsync()
    {
        var ports = await _portEnumerator.GetPortsAsync(CancellationToken.None);
        var deduped = ports
            .GroupBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.FriendlyName))
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.Description))
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.VendorId) || !string.IsNullOrWhiteSpace(p.ProductId))
                .First())
            .OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var added = deduped
            .Where(p => !_knownPortNames.Contains(p.PortName))
            .OrderByDescending(p => IsLikelyExternalSerialPort(p.PortName))
            .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.VendorId) || !string.IsNullOrWhiteSpace(p.ProductId))
            .ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (added.Count > 0)
        {
            _lastHotplugPortName = added[0].PortName;
            _logger.LogInformation("Detected serial port: {Port}", _lastHotplugPortName);
        }

        _knownPortNames.Clear();
        foreach (var port in deduped)
            _knownPortNames.Add(port.PortName);

        var previous = SelectedPort?.PortName;
        Ports.Clear();
        foreach (var port in deduped)
            Ports.Add(new SerialPortInfoViewModel(port));

        var target = Ports.FirstOrDefault(p => string.Equals(p.PortName, previous, StringComparison.OrdinalIgnoreCase))
            ?? Ports.FirstOrDefault(p => string.Equals(p.PortName, _settings.LastPort, StringComparison.OrdinalIgnoreCase))
            ?? Ports.FirstOrDefault(p => string.Equals(p.PortName, _lastHotplugPortName, StringComparison.OrdinalIgnoreCase))
            ?? Ports.FirstOrDefault();
        SelectedPort = target;
    }

    private bool CanConnect()
    {
        return UseReplay || SelectedPort is not null;
    }

    private async Task ConnectAsync()
    {
        try
        {
            var options = new ConnectionOptions
            {
                AutoReconnect = AutoReconnect,
                AckTimeout = TimeSpan.FromMilliseconds(AckTimeoutMs),
                MaxRetries = MaxRetries,
            };

            TransportEndpoint endpoint = UseReplay
                ? new ReplayEndpoint(ReplayFile, _settings.ReplaySpeed)
                : new SerialEndpoint(SelectedPort?.PortName ?? string.Empty);

            await _client.ConnectAsync(endpoint, options, CancellationToken.None);
            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
        }
    }

    private async Task SaveSettingsAsync()
    {
        var overrides = SeverityRules.ToDictionary(r => r.Kind, r => r.Severity);
        var mqttSettings = BuildMqttSettings();
        var newSettings = _settings with
        {
            AutoReconnect = AutoReconnect,
            AutoConnectOnDetect = AutoConnectOnDetect,
            LastPort = SelectedPort?.PortName,
            LastReplayFile = ReplayFile,
            AckTimeoutMs = AckTimeoutMs,
            MaxRetries = MaxRetries,
            Ui = _settings.Ui with
            {
                Language = SelectedLanguage?.Culture.Name ?? string.Empty,
                Theme = SelectedTheme?.Definition.Id ?? string.Empty,
                OfflineMode = OfflineMode,
                ShowMapLogs = MapShowLogs,
                ShowMapMqtt = MapShowMqtt,
                ShowMapGibs = MapShowGibs,
                MapBaseLayer = (SelectedMapBaseLayer?.Kind ?? MapBaseLayerKind.Osm).ToString(),
            },
            Tactical = _settings.Tactical with
            {
                OnlineThresholdSeconds = OnlineThresholdSeconds,
                WeakThresholdSeconds = WeakThresholdSeconds,
                CommandChannel = (byte)Math.Clamp(CommandChannel, 0, 255),
                SeverityOverrides = overrides,
            },
            Aprs = _settings.Aprs with
            {
                Enabled = AprsEnabled,
                ServerHost = AprsServerHost.Trim(),
                ServerPort = AprsServerPort,
                IgateCallsign = AprsIgateCallsign.Trim(),
                IgateSsid = (byte)Math.Clamp(AprsIgateSsid, 0, 15),
                Passcode = AprsPasscode.Trim(),
                ToCall = AprsToCall.Trim(),
                Path = AprsPath.Trim(),
                Filter = AprsFilter.Trim(),
                TxMinIntervalSec = Math.Max(1, AprsTxMinIntervalSec),
                DedupeWindowSec = Math.Max(1, AprsDedupeWindowSec),
                PositionIntervalSec = Math.Max(5, AprsPositionIntervalSec),
                SymbolTable = string.IsNullOrWhiteSpace(AprsSymbolTable) ? '/' : AprsSymbolTable[0],
                SymbolCode = string.IsNullOrWhiteSpace(AprsSymbolCode) ? '>' : AprsSymbolCode[0],
                UseCompressed = AprsUseCompressed,
                EmitStatus = AprsEmitStatus,
                EmitTelemetry = AprsEmitTelemetry,
                EmitWeather = AprsEmitWeather,
                EmitMessages = AprsEmitMessages,
                EmitWaypoints = AprsEmitWaypoints,
            },
            // MQTT source definitions are stored in SQLite.
            Mqtt = new MeshtasticMqttSettings
            {
                Sources = new List<MeshtasticMqttSourceSettings>(),
            },
            Contours = _settings.Contours with
            {
                Enabled = ContoursEnabled,
                EnableUltraFine = ContoursUltraFine,
                Earthdata = _settings.Contours.Earthdata with
                {
                    Token = EarthdataToken.Trim(),
                },
            },
        };
        _settings = newSettings with { Mqtt = mqttSettings };
        await _settingsStore.SaveAsync(newSettings, CancellationToken.None);
        await _sqliteStore.SaveMqttSourcesAsync(mqttSettings.Sources, CancellationToken.None);
        _aprsGateway.ApplySettings(BuildEffectiveAprsSettings(newSettings.Aprs));
        _meshtasticMqtt.ApplySettings(BuildEffectiveMqttSettings(mqttSettings));
        Map.UpdateContourSettings(newSettings.Contours);
    }

    private bool CanTestEarthdata()
    {
        return !_earthdataTestBusy;
    }

    private async Task TestEarthdataAsync()
    {
        if (_earthdataTestBusy)
            return;

        _earthdataTestBusy = true;
        TestEarthdataCommand.NotifyCanExecuteChanged();
        SetEarthdataStatus("Status.Earthdata.Testing", "#9AA3AE");
        try
        {
            var result = await Map.TestEarthdataCredentialsAsync();
            switch (result.Status)
            {
                case EarthdataTestStatus.Success:
                    SetEarthdataStatus("Status.Earthdata.Ok", "#9FE870", result.Detail);
                    break;
                case EarthdataTestStatus.MissingCredentials:
                    SetEarthdataStatus("Status.Earthdata.Missing", "#F59E0B", result.Detail);
                    break;
                case EarthdataTestStatus.NoDataInView:
                    SetEarthdataStatus("Status.Earthdata.NoData", "#A4AFBC", result.Detail);
                    break;
                case EarthdataTestStatus.NoViewport:
                    SetEarthdataStatus("Status.Earthdata.NoViewport", "#A4AFBC", result.Detail);
                    break;
                case EarthdataTestStatus.Unauthorized:
                    SetEarthdataStatus("Status.Earthdata.Unauthorized", "#EF4444", result.Detail);
                    break;
                case EarthdataTestStatus.AccessDenied:
                    SetEarthdataStatus("Status.Earthdata.AccessDenied", "#F59E0B", result.Detail);
                    break;
                case EarthdataTestStatus.Error:
                    SetEarthdataStatus("Status.Earthdata.Error", "#EF4444", result.Detail ?? "Unknown");
                    break;
            }
        }
        finally
        {
            _earthdataTestBusy = false;
            TestEarthdataCommand.NotifyCanExecuteChanged();
        }
    }

    private ContourSettings BuildContourSettings()
    {
        var baseSettings = _settings.Contours;
        return baseSettings with
        {
            Enabled = ContoursEnabled,
            EnableUltraFine = ContoursUltraFine,
            Earthdata = baseSettings.Earthdata with
            {
                Token = EarthdataToken.Trim(),
            },
        };
    }

    private static MapBaseLayerKind ParseMapBaseLayer(string? value)
    {
        if (Enum.TryParse<MapBaseLayerKind>(value, true, out var parsed))
            return parsed;

        return MapBaseLayerKind.Osm;
    }

    private MeshtasticMqttSettings BuildMqttSettings()
    {
        var sources = MqttSources
            .Select(s => s.ToSettings())
            .ToList();

        if (sources.Count == 0)
            sources.Add(MeshtasticMqttSourceSettings.CreateDefault());

        return new MeshtasticMqttSettings
        {
            Sources = sources,
        };
    }

    private AprsSettings BuildEffectiveAprsSettings(AprsSettings settings)
    {
        return OfflineMode
            ? settings with { Enabled = false }
            : settings;
    }

    private MeshtasticMqttSettings BuildEffectiveMqttSettings(MeshtasticMqttSettings settings)
    {
        if (!OfflineMode)
            return settings;

        return settings with
        {
            Sources = (settings.Sources ?? new List<MeshtasticMqttSourceSettings>())
                .Select(source => source with { Enabled = false })
                .ToList(),
        };
    }

    private async Task<MeshtasticMqttSettings> LoadMqttSettingsAsync(CancellationToken cancellationToken)
    {
        var sqliteSources = await _sqliteStore.LoadMqttSourcesAsync(cancellationToken);
        if (sqliteSources.Count > 0)
        {
            return new MeshtasticMqttSettings
            {
                Sources = sqliteSources.ToList(),
            };
        }

        var fallbackSources = (_settings.Mqtt.Sources ?? new List<MeshtasticMqttSourceSettings>())
            .ToList();
        if (fallbackSources.Count == 0)
            fallbackSources = MeshtasticMqttSettings.CreateDefault().Sources.ToList();

        await _sqliteStore.SaveMqttSourcesAsync(fallbackSources, cancellationToken);

        return new MeshtasticMqttSettings
        {
            Sources = fallbackSources,
        };
    }

    private void LoadMqttSources(IEnumerable<MeshtasticMqttSourceSettings>? sources)
    {
        foreach (var source in MqttSources)
        {
            source.PropertyChanged -= OnMqttSourcePropertyChanged;
        }

        MqttSources.Clear();

        var list = (sources ?? Array.Empty<MeshtasticMqttSourceSettings>()).ToList();
        if (list.Count == 0)
        {
            list.Add(MeshtasticMqttSourceSettings.CreateDefault());
        }

        foreach (var source in list)
        {
            var vm = MqttSourceViewModel.FromSettings(source);
            vm.PropertyChanged += OnMqttSourcePropertyChanged;
            MqttSources.Add(vm);
        }

        SelectedMqttSource = MqttSources.FirstOrDefault();
        RemoveMqttSourceCommand.NotifyCanExecuteChanged();
    }

    private void AddMqttSource()
    {
        var source = MqttSourceViewModel.CreateDefault();
        source.Name = $"MQTT {MqttSources.Count + 1}";
        source.PropertyChanged += OnMqttSourcePropertyChanged;
        MqttSources.Add(source);
        SelectedMqttSource = source;
        RemoveMqttSourceCommand.NotifyCanExecuteChanged();
        ScheduleSettingsSave();
    }

    private bool CanRemoveSelectedMqttSource()
    {
        return SelectedMqttSource is not null && MqttSources.Count > 1;
    }

    private void RemoveSelectedMqttSource()
    {
        if (SelectedMqttSource is null)
            return;
        if (MqttSources.Count <= 1)
            return;

        var index = MqttSources.IndexOf(SelectedMqttSource);
        if (index < 0)
            return;

        SelectedMqttSource.PropertyChanged -= OnMqttSourcePropertyChanged;
        MqttSources.RemoveAt(index);
        if (MqttSources.Count == 0)
        {
            AddMqttSource();
        }
        else
        {
            SelectedMqttSource = MqttSources[Math.Min(index, MqttSources.Count - 1)];
            ScheduleSettingsSave();
        }
        RemoveMqttSourceCommand.NotifyCanExecuteChanged();
    }

    private void OnMqttSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
            return;
        ScheduleSettingsSave();
    }

    private void LoadOfflineCacheRegions(IEnumerable<MapCacheRegionSettings>? regions)
    {
        foreach (var tracked in _trackedOfflineCacheRegions.ToArray())
        {
            UntrackOfflineCacheRegion(tracked);
        }

        OfflineCacheRegions.Clear();
        foreach (var region in (regions ?? Array.Empty<MapCacheRegionSettings>()))
        {
            var vm = MapCacheRegionViewModel.FromSettings(region);
            TrackOfflineCacheRegion(vm);
            OfflineCacheRegions.Add(vm);
        }

        SelectedOfflineCacheRegion = OfflineCacheRegions.FirstOrDefault();
        ApplyOfflineCacheRegionCommand.NotifyCanExecuteChanged();
        NotifyOfflineCacheBuildAvailabilityChanged();
        RemoveOfflineCacheRegionCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveOfflineCacheRegionsAsync()
    {
        var regions = OfflineCacheRegions
            .Select(item => item.ToSettings())
            .ToList();
        await _sqliteStore.SaveMapCacheRegionsAsync(regions, CancellationToken.None);
    }

    private bool CanSaveOfflineCacheRegionFromSelection()
    {
        return Map.HasOfflineCacheSelection;
    }

    private void SaveOfflineCacheRegionFromSelection()
    {
        SaveOfflineCacheRegionFromSelectionCore(OfflineCacheRegionName);
    }

    public void SaveOfflineCacheRegionFromCurrentSelection(string? preferredName, OfflineCacheBuildOptions? options = null)
    {
        SaveOfflineCacheRegionFromSelectionCore(preferredName, options);
    }

    private void SaveOfflineCacheRegionFromSelectionCore(string? preferredName, OfflineCacheBuildOptions? options = null)
    {
        if (!Map.TryGetOfflineCacheSelectionBounds(out var bounds))
            return;

        var name = preferredName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = SelectedOfflineCacheRegion?.Name;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Area {OfflineCacheRegions.Count + 1}";
        }

        MapCacheRegionViewModel region;
        if (SelectedOfflineCacheRegion is not null &&
            string.Equals(SelectedOfflineCacheRegion.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            region = SelectedOfflineCacheRegion;
            region.UpdateBounds(bounds);
            region.Name = name;
            if (options is not null)
            {
                region.ApplyBuildOptions(options);
            }
        }
        else
        {
            region = new MapCacheRegionViewModel
            {
                Name = name,
            };
            region.UpdateBounds(bounds);
            if (options is not null)
            {
                region.ApplyBuildOptions(options);
            }
            TrackOfflineCacheRegion(region);
            OfflineCacheRegions.Add(region);
        }

        SelectedOfflineCacheRegion = region;
        OfflineCacheRegionName = region.Name;
        Map.SetOfflineCacheSelectionBounds(bounds, region.Name);

        _ = PersistOfflineCacheRegionsSafeAsync();
    }

    public Task RunOfflineCacheForCurrentSelectionAsync(OfflineCacheBuildOptions options)
    {
        return Map.RunOfflineCacheForSelectionAsync(options);
    }

    public bool FocusSelectedOfflineCacheRegionOnMap()
    {
        return TryApplySelectedOfflineCacheRegion(focusMap: true);
    }

    private bool CanApplySelectedOfflineCacheRegion()
    {
        return SelectedOfflineCacheRegion is not null && !Map.IsOfflineCacheRunning;
    }

    private void ApplySelectedOfflineCacheRegion()
    {
        TryApplySelectedOfflineCacheRegion(focusMap: false);
    }

    private bool CanBuildSelectedOfflineCacheRegion()
    {
        return CanBuildRegion(SelectedOfflineCacheRegion);
    }

    private async Task BuildSelectedOfflineCacheRegionAsync()
    {
        if (SelectedOfflineCacheRegion is null)
            return;

        // Preflight: always re-check local cache coverage before starting a new run.
        await RefreshOfflineCacheRegionHealthAsync(SelectedOfflineCacheRegion, CancellationToken.None);
        NotifyOfflineCacheBuildAvailabilityChanged();
        if (!CanBuildRegion(SelectedOfflineCacheRegion))
            return;

        ApplySelectedOfflineCacheRegion();
        await Map.RunOfflineCacheForSelectionAsync(SelectedOfflineCacheRegion.ToBuildOptions());
        await RefreshOfflineCacheRegionHealthAsync(SelectedOfflineCacheRegion, CancellationToken.None);
        NotifyOfflineCacheBuildAvailabilityChanged();
    }

    public async Task ContinueSelectedOfflineCacheRegionAsync()
    {
        if (SelectedOfflineCacheRegion is null || Map.IsOfflineCacheRunning || IsOfflineCacheHealthRefreshing)
            return;

        await BuildSelectedOfflineCacheRegionAsync();
    }

    public async Task<OfflineCacheRegionExportResult> ExportSelectedOfflineCacheRegionAsync(
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        if (SelectedOfflineCacheRegion is null)
        {
            return OfflineCacheRegionExportResult.Fail("No cache region selected.");
        }

        var targetRoot = destinationRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return OfflineCacheRegionExportResult.Fail("Destination folder is empty.");
        }

        if (Map.IsOfflineCacheRunning)
        {
            return OfflineCacheRegionExportResult.Fail("Offline cache is running.");
        }

        if (IsOfflineCacheExporting)
        {
            return OfflineCacheRegionExportResult.Fail("Export is already running.");
        }

        var region = SelectedOfflineCacheRegion.ToSettings();
        var loc = LocalizationService.Instance;
        IsOfflineCacheExporting = true;
        OfflineCacheExportStatusText = loc.GetString("Status.OfflineCache.ExportInProgress");
        try
        {
            var result = await Task.Run(
                () => ExportOfflineCacheRegion(region, GetOfflineCacheRoot(), targetRoot, cancellationToken),
                cancellationToken);

            if (result.Success)
            {
                OfflineCacheExportStatusText = loc.Format(
                    "Status.OfflineCache.ExportDone",
                    result.CopiedTiles,
                    result.SourceTiles,
                    result.SkippedTiles);
                _logger.LogInformation(
                    "Offline cache region '{RegionName}' exported to '{TargetRoot}'. copied={Copied}, source={Source}, skipped={Skipped}",
                    region.Name,
                    result.TargetMapRoot,
                    result.CopiedTiles,
                    result.SourceTiles,
                    result.SkippedTiles);
            }
            else
            {
                OfflineCacheExportStatusText = loc.Format(
                    "Status.OfflineCache.ExportFailed",
                    result.ErrorMessage ?? "unknown");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            OfflineCacheExportStatusText = loc.GetString("Status.OfflineCache.ExportCanceled");
            return OfflineCacheRegionExportResult.Fail("Canceled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export offline cache region '{RegionName}'", region.Name);
            OfflineCacheExportStatusText = loc.Format("Status.OfflineCache.ExportFailed", ex.Message);
            return OfflineCacheRegionExportResult.Fail(ex.Message);
        }
        finally
        {
            IsOfflineCacheExporting = false;
        }
    }

    private bool TryApplySelectedOfflineCacheRegion(bool focusMap)
    {
        if (SelectedOfflineCacheRegion is null)
            return false;

        var bounds = (
            SelectedOfflineCacheRegion.West,
            SelectedOfflineCacheRegion.South,
            SelectedOfflineCacheRegion.East,
            SelectedOfflineCacheRegion.North);
        Map.SetOfflineCacheSelectionBounds(bounds, SelectedOfflineCacheRegion.Name);

        if (focusMap)
        {
            Map.IsOfflineCacheSelectionMode = false;
            Map.FocusOnBounds(bounds);
        }

        return true;
    }

    private static OfflineCacheRegionExportResult ExportOfflineCacheRegion(
        MapCacheRegionSettings region,
        string cacheRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var mapsRoot = ResolveMapsExportRoot(destinationRoot);
            var bounds = (region.West, region.South, region.East, region.North);
            var stats = new ExportCopyStats();

            if (region.IncludeOsm)
            {
                ExportBaseTiles(
                    sourceRoot: Path.Combine(cacheRoot, "tilecache"),
                    targetRoot: Path.Combine(mapsRoot, "base", "osm"),
                    extension: "png",
                    minZoom: 0,
                    maxZoom: 18,
                    bounds: bounds,
                    stats: stats,
                    cancellationToken: cancellationToken);
            }

            if (region.IncludeTerrain)
            {
                ExportBaseTiles(
                    sourceRoot: Path.Combine(cacheRoot, "terrain-cache"),
                    targetRoot: Path.Combine(mapsRoot, "base", "terrain"),
                    extension: "png",
                    minZoom: 0,
                    maxZoom: 17,
                    bounds: bounds,
                    stats: stats,
                    cancellationToken: cancellationToken);
            }

            if (region.IncludeSatellite)
            {
                ExportBaseTiles(
                    sourceRoot: Path.Combine(cacheRoot, "satellite-cache"),
                    targetRoot: Path.Combine(mapsRoot, "base", "satellite"),
                    extension: "jpg",
                    minZoom: 0,
                    maxZoom: 18,
                    bounds: bounds,
                    stats: stats,
                    cancellationToken: cancellationToken);
            }

            if (region.IncludeContours)
            {
                ExportTrailMateContours(
                    contourRoot: Path.Combine(cacheRoot, "contours", "tiles"),
                    targetRoot: Path.Combine(mapsRoot, "contour"),
                    bounds: bounds,
                    stats: stats,
                    cancellationToken: cancellationToken);
            }

            return OfflineCacheRegionExportResult.Ok(
                mapsRoot,
                stats.SourceTiles,
                stats.CopiedTiles,
                stats.SkippedTiles);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OfflineCacheRegionExportResult.Fail(ex.Message);
        }
    }

    private static string ResolveMapsExportRoot(string destinationRoot)
    {
        var normalizedRoot = destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            normalizedRoot = destinationRoot;
        }
        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            throw new InvalidOperationException("Destination folder is empty.");
        }

        if (string.Equals(Path.GetFileName(normalizedRoot), "maps", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(normalizedRoot);
            return normalizedRoot;
        }

        var mapsRoot = Path.Combine(normalizedRoot, "maps");
        Directory.CreateDirectory(mapsRoot);
        return mapsRoot;
    }

    private static void ExportBaseTiles(
        string sourceRoot,
        string targetRoot,
        string extension,
        int minZoom,
        int maxZoom,
        (double West, double South, double East, double North) bounds,
        ExportCopyStats stats,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);

        for (var zoom = minZoom; zoom <= maxZoom; zoom++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var range = GetOfflineRange(bounds, zoom);
            if (range.IsEmpty)
                continue;

            CopyTilesInRange(sourceRoot, targetRoot, extension, zoom, range, stats, cancellationToken);
        }
    }

    private static void ExportTrailMateContours(
        string contourRoot,
        string targetRoot,
        (double West, double South, double East, double North) bounds,
        ExportCopyStats stats,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);

        for (var zoom = 0; zoom <= 18; zoom++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profile = GetTrailMateContourProfileForZoom(zoom);
            if (profile is null)
                continue;

            var range = GetOfflineRange(bounds, zoom);
            if (range.IsEmpty)
                continue;

            var profileSourceRoot = Path.Combine(contourRoot, profile);
            var profileTargetRoot = Path.Combine(targetRoot, profile);
            CopyTilesInRange(profileSourceRoot, profileTargetRoot, "png", zoom, range, stats, cancellationToken);
        }
    }

    private static string? GetTrailMateContourProfileForZoom(int zoom)
    {
        if (zoom <= 7)
            return null;
        if (zoom is 8 or 10)
            return "major-500";
        if (zoom is 9 or 11)
            return "major-200";
        if (zoom is >= 12 and <= 14)
            return "major-100";
        if (zoom is 15 or 16)
            return "major-50";

        return "major-25";
    }

    private static void CopyTilesInRange(
        string sourceRoot,
        string targetRoot,
        string extension,
        int zoom,
        TileRange range,
        ExportCopyStats stats,
        CancellationToken cancellationToken)
    {
        var zoomText = zoom.ToString(CultureInfo.InvariantCulture);
        var sourceZoomRoot = Path.Combine(sourceRoot, zoomText);
        if (!Directory.Exists(sourceZoomRoot))
            return;

        var searchPattern = $"*.{extension}";
        var targetZoomRoot = Path.Combine(targetRoot, zoomText);
        for (var x = range.MinX; x <= range.MaxX; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var xText = x.ToString(CultureInfo.InvariantCulture);
            var sourceXRoot = Path.Combine(sourceZoomRoot, xText);
            if (!Directory.Exists(sourceXRoot))
                continue;

            string? targetXRoot = null;
            foreach (var sourceFile in Directory.EnumerateFiles(sourceXRoot, searchPattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(sourceFile);
                if (!int.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                    continue;
                if (y < range.MinY || y > range.MaxY)
                    continue;

                stats.SourceTiles++;
                targetXRoot ??= Path.Combine(targetZoomRoot, xText);
                Directory.CreateDirectory(targetXRoot);
                var targetFile = Path.Combine(targetXRoot, $"{y}.{extension}");
                if (File.Exists(targetFile) && IsSameSizeTile(sourceFile, targetFile))
                {
                    stats.SkippedTiles++;
                    continue;
                }

                File.Copy(sourceFile, targetFile, overwrite: true);
                stats.CopiedTiles++;
            }
        }
    }

    private static bool IsSameSizeTile(string sourceFile, string targetFile)
    {
        try
        {
            var sourceInfo = new FileInfo(sourceFile);
            var targetInfo = new FileInfo(targetFile);
            return sourceInfo.Exists && targetInfo.Exists && sourceInfo.Length == targetInfo.Length;
        }
        catch
        {
            return false;
        }
    }

    private bool CanRemoveSelectedOfflineCacheRegion()
    {
        return SelectedOfflineCacheRegion is not null;
    }

    private void RemoveSelectedOfflineCacheRegion()
    {
        if (SelectedOfflineCacheRegion is null)
            return;

        var removed = SelectedOfflineCacheRegion;
        var index = OfflineCacheRegions.IndexOf(removed);
        if (index < 0)
            return;

        OfflineCacheRegions.RemoveAt(index);
        UntrackOfflineCacheRegion(removed);
        SelectedOfflineCacheRegion = OfflineCacheRegions.Count == 0
            ? null
            : OfflineCacheRegions[Math.Min(index, OfflineCacheRegions.Count - 1)];

        _ = PersistOfflineCacheRegionsSafeAsync();
    }

    private async Task PersistOfflineCacheRegionsSafeAsync()
    {
        try
        {
            await SaveOfflineCacheRegionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save map cache regions");
        }
    }

    public async Task RefreshOfflineCacheRegionsHealthAsync(bool selectedOnly = false)
    {
        if (IsOfflineCacheHealthRefreshing)
            return;

        IsOfflineCacheHealthRefreshing = true;
        try
        {
            if (selectedOnly)
            {
                if (SelectedOfflineCacheRegion is not null)
                {
                    await RefreshOfflineCacheRegionHealthAsync(SelectedOfflineCacheRegion, CancellationToken.None);
                }

                return;
            }

            foreach (var region in OfflineCacheRegions.ToArray())
            {
                await RefreshOfflineCacheRegionHealthAsync(region, CancellationToken.None);
            }
        }
        finally
        {
            IsOfflineCacheHealthRefreshing = false;
            NotifyOfflineCacheBuildAvailabilityChanged();
        }
    }

    private bool CanBuildRegion(MapCacheRegionViewModel? region)
    {
        if (region is null || Map.IsOfflineCacheRunning || IsOfflineCacheExporting)
            return false;

        var hasAnyLayer = region.IncludeOsm ||
                          region.IncludeTerrain ||
                          region.IncludeSatellite ||
                          region.IncludeContours;
        if (!hasAnyLayer)
            return false;

        var isComplete = region.CacheExpectedTiles > 0 &&
                         region.CacheExistingTiles >= region.CacheExpectedTiles;
        return !isComplete;
    }

    private void NotifyOfflineCacheBuildAvailabilityChanged()
    {
        BuildOfflineCacheRegionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanContinueSelectedOfflineCacheRegion));
        OnPropertyChanged(nameof(CanExportSelectedOfflineCacheRegion));
    }

    private void TrackOfflineCacheRegion(MapCacheRegionViewModel region)
    {
        if (!_trackedOfflineCacheRegions.Add(region))
            return;

        region.PropertyChanged += OnOfflineCacheRegionPropertyChanged;
    }

    private void UntrackOfflineCacheRegion(MapCacheRegionViewModel region)
    {
        if (!_trackedOfflineCacheRegions.Remove(region))
            return;

        region.PropertyChanged -= OnOfflineCacheRegionPropertyChanged;
    }

    private void OnOfflineCacheRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
            return;

        if (e.PropertyName is nameof(MapCacheRegionViewModel.CacheExistingTiles) or
            nameof(MapCacheRegionViewModel.CacheExpectedTiles) or
            nameof(MapCacheRegionViewModel.IncludeOsm) or
            nameof(MapCacheRegionViewModel.IncludeTerrain) or
            nameof(MapCacheRegionViewModel.IncludeSatellite) or
            nameof(MapCacheRegionViewModel.IncludeContours))
        {
            NotifyOfflineCacheBuildAvailabilityChanged();
        }
    }

    private async Task RefreshOfflineCacheRegionHealthAsync(
        MapCacheRegionViewModel region,
        CancellationToken cancellationToken)
    {
        region.SetCacheHealthChecking(true);
        try
        {
            var health = await Task.Run(() => InspectRegionCache(region, cancellationToken), cancellationToken);
            region.SetCacheHealth(
                health.ExistingTiles,
                health.ExpectedTiles,
                (health.OsmExistingTiles, health.OsmExpectedTiles),
                (health.TerrainExistingTiles, health.TerrainExpectedTiles),
                (health.SatelliteExistingTiles, health.SatelliteExpectedTiles),
                (health.ContourExistingTiles, health.ContourExpectedTiles));
        }
        catch (OperationCanceledException)
        {
            region.CacheHealthText = "Canceled";
            region.CacheHealthDetailText = "Cache inspection canceled.";
            region.CacheHealthColor = "#9AA3AE";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect cache health for region {RegionName}", region.Name);
            region.CacheHealthText = "Inspect failed";
            region.CacheHealthDetailText = ex.Message;
            region.CacheHealthColor = "#FF7373";
        }
        finally
        {
            region.IsCacheHealthChecking = false;
        }
    }

    private static CacheCoverage InspectRegionCache(MapCacheRegionViewModel region, CancellationToken cancellationToken)
    {
        var bounds = (
            region.West,
            region.South,
            region.East,
            region.North);
        var options = region.ToBuildOptions();
        var root = GetOfflineCacheRoot();

        var osm = InspectBaseLayer(
            Path.Combine(root, "tilecache"),
            "png",
            0,
            18,
            options.IncludeOsm,
            bounds,
            cancellationToken);
        var terrain = InspectBaseLayer(
            Path.Combine(root, "terrain-cache"),
            "png",
            0,
            17,
            options.IncludeTerrain,
            bounds,
            cancellationToken);
        var satellite = InspectBaseLayer(
            Path.Combine(root, "satellite-cache"),
            "jpg",
            0,
            18,
            options.IncludeSatellite,
            bounds,
            cancellationToken);
        var contour = InspectContourLayer(root, bounds, options.IncludeContours, options.IncludeUltraFineContours, cancellationToken);

        var existingTiles = osm.ExistingTiles + terrain.ExistingTiles + satellite.ExistingTiles + contour.ExistingTiles;
        var expectedTiles = osm.ExpectedTiles + terrain.ExpectedTiles + satellite.ExpectedTiles + contour.ExpectedTiles;
        return new CacheCoverage(
            existingTiles,
            expectedTiles,
            osm.ExistingTiles,
            osm.ExpectedTiles,
            terrain.ExistingTiles,
            terrain.ExpectedTiles,
            satellite.ExistingTiles,
            satellite.ExpectedTiles,
            contour.ExistingTiles,
            contour.ExpectedTiles);
    }

    private static CacheLayerCoverage InspectBaseLayer(
        string cacheRoot,
        string extension,
        int minZoom,
        int maxZoom,
        bool enabled,
        (double West, double South, double East, double North) bounds,
        CancellationToken cancellationToken)
    {
        if (!enabled)
            return new CacheLayerCoverage(0, 0);

        long expectedTiles = 0;
        long existingTiles = 0;
        for (var zoom = minZoom; zoom <= maxZoom; zoom++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var range = GetOfflineRange(bounds, zoom);
            if (range.IsEmpty)
                continue;

            expectedTiles += range.TileCount;
            existingTiles += CountExistingTiles(cacheRoot, extension, zoom, range, cancellationToken);
        }

        return new CacheLayerCoverage(existingTiles, expectedTiles);
    }

    private static CacheLayerCoverage InspectContourLayer(
        string root,
        (double West, double South, double East, double North) bounds,
        bool includeContours,
        bool includeUltraFineContours,
        CancellationToken cancellationToken)
    {
        if (!includeContours)
            return new CacheLayerCoverage(0, 0);

        var contourRoot = Path.Combine(root, "contours", "tiles");
        long expectedTiles = 0;
        long existingTiles = 0;
        for (var zoom = 0; zoom <= 18; zoom++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profiles = GetContourProfilesForZoom(zoom, includeUltraFineContours);
            if (profiles.Count == 0)
                continue;

            var range = GetOfflineRange(bounds, zoom);
            if (range.IsEmpty)
                continue;

            expectedTiles += range.TileCount * profiles.Count;
            foreach (var profile in profiles)
            {
                var profileRoot = Path.Combine(contourRoot, profile);
                existingTiles += CountExistingTiles(profileRoot, "png", zoom, range, cancellationToken);
            }
        }

        return new CacheLayerCoverage(existingTiles, expectedTiles);
    }

    private static long CountExistingTiles(
        string cacheRoot,
        string extension,
        int zoom,
        TileRange range,
        CancellationToken cancellationToken)
    {
        var count = 0L;
        var zoomDir = Path.Combine(cacheRoot, zoom.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(zoomDir))
            return 0;

        var searchPattern = $"*.{extension}";
        for (var x = range.MinX; x <= range.MaxX; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var xDir = Path.Combine(zoomDir, x.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(xDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(xDir, searchPattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                    continue;
                if (y < range.MinY || y > range.MaxY)
                    continue;
                count++;
            }
        }

        return count;
    }

    private static TileRange GetOfflineRange(
        (double West, double South, double East, double North) bounds,
        int zoom)
    {
        var xMin = ContourTileMath.LonToTileX(bounds.West, zoom);
        var xMax = ContourTileMath.LonToTileX(bounds.East, zoom);
        var yMin = ContourTileMath.LatToTileY(bounds.North, zoom);
        var yMax = ContourTileMath.LatToTileY(bounds.South, zoom);

        if (xMin > xMax)
            (xMin, xMax) = (xMax, xMin);
        if (yMin > yMax)
            (yMin, yMax) = (yMax, yMin);

        return new TileRange(xMin, xMax, yMin, yMax);
    }

    private static IReadOnlyList<string> GetContourProfilesForZoom(int zoom, bool allowUltraFineContours)
    {
        if (zoom <= 7)
            return Array.Empty<string>();
        if (zoom == 8)
            return ["major-500"];
        if (zoom == 9)
            return ["major-200"];
        if (zoom == 10)
            return ["major-500", "minor-100"];
        if (zoom == 11)
            return ["major-200", "minor-50"];
        if (zoom == 12)
            return ["major-100", "minor-50"];
        if (zoom is 13 or 14)
            return ["major-100", "minor-20"];
        if (zoom is 15 or 16)
            return ["major-50", "minor-10"];
        if (zoom >= 17)
            return allowUltraFineContours
                ? ["major-25", "minor-5"]
                : ["major-25"];

        return Array.Empty<string>();
    }

    private static string GetOfflineCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TrailMateCenter");
    }

    private readonly record struct CacheCoverage(
        long ExistingTiles,
        long ExpectedTiles,
        long OsmExistingTiles,
        long OsmExpectedTiles,
        long TerrainExistingTiles,
        long TerrainExpectedTiles,
        long SatelliteExistingTiles,
        long SatelliteExpectedTiles,
        long ContourExistingTiles,
        long ContourExpectedTiles);

    private readonly record struct CacheLayerCoverage(
        long ExistingTiles,
        long ExpectedTiles);

    private readonly record struct TileRange(
        int MinX,
        int MaxX,
        int MinY,
        int MaxY)
    {
        public bool IsEmpty => MaxX < MinX || MaxY < MinY;
        public long TileCount => IsEmpty ? 0 : (long)(MaxX - MinX + 1) * (MaxY - MinY + 1);
    }

    public readonly record struct OfflineCacheRegionExportResult(
        bool Success,
        string TargetMapRoot,
        long SourceTiles,
        long CopiedTiles,
        long SkippedTiles,
        string? ErrorMessage)
    {
        public static OfflineCacheRegionExportResult Ok(
            string targetMapRoot,
            long sourceTiles,
            long copiedTiles,
            long skippedTiles)
        {
            return new OfflineCacheRegionExportResult(
                Success: true,
                TargetMapRoot: targetMapRoot,
                SourceTiles: sourceTiles,
                CopiedTiles: copiedTiles,
                SkippedTiles: skippedTiles,
                ErrorMessage: null);
        }

        public static OfflineCacheRegionExportResult Fail(string errorMessage)
        {
            return new OfflineCacheRegionExportResult(
                Success: false,
                TargetMapRoot: string.Empty,
                SourceTiles: 0,
                CopiedTiles: 0,
                SkippedTiles: 0,
                ErrorMessage: errorMessage);
        }
    }

    private sealed class ExportCopyStats
    {
        public long SourceTiles { get; set; }
        public long CopiedTiles { get; set; }
        public long SkippedTiles { get; set; }
    }

    private async Task DisconnectAsync()
    {
        await _client.DisconnectAsync(CancellationToken.None);
    }

    private bool CanSend()
    {
        return IsConnected && !string.IsNullOrWhiteSpace(OutgoingText);
    }

    private async Task SendAsync()
    {
        if (!TryParseNodeId(Target, out var toId))
        {
            SetLastErrorLocalized("Error.InvalidTargetFormat");
            return;
        }

        var request = new MessageSendRequest
        {
            ToId = toId,
            Channel = SelectedChannel?.Id ?? (byte)0,
            Flags = 0,
            Text = OutgoingText,
        };

        await _client.SendMessageAsync(request, CancellationToken.None);
        OutgoingText = string.Empty;
    }

    private bool CanQuickSend()
    {
        return IsConnected && SelectedSubject is not null && !string.IsNullOrWhiteSpace(QuickMessageText);
    }

    private async Task QuickSendAsync()
    {
        if (SelectedSubject is null)
        {
            SetLastErrorLocalized("Error.NoSubjectSelected");
            return;
        }

        var request = new MessageSendRequest
        {
            ToId = SelectedSubject.Id,
            Channel = SelectedChannel?.Id ?? (byte)0,
            Flags = 0,
            Text = QuickMessageText,
        };

        await _client.SendMessageAsync(request, CancellationToken.None);
        QuickMessageText = string.Empty;

        var conversation = Conversations.FirstOrDefault(c => c.PeerId == SelectedSubject.Id);
        if (conversation is not null)
        {
            SelectedConversation = conversation;
        }
        else
        {
            Target = $"0x{SelectedSubject.Id:X8}";
        }
    }

    private bool CanRetry()
    {
        return IsConnected && SelectedMessage is not null &&
               (SelectedMessage.Status == MessageDeliveryStatus.Failed || SelectedMessage.Status == MessageDeliveryStatus.Timeout);
    }

    private async Task RetryAsync()
    {
        if (SelectedMessage is null)
            return;

        if (!TryParseNodeId(SelectedMessage.To, out var toId))
        {
            SetLastErrorLocalized("Error.InvalidTargetFormat");
            return;
        }

        var request = new MessageSendRequest
        {
            ToId = toId,
            Channel = byte.TryParse(SelectedMessage.Channel, out var ch) ? ch : (byte)0,
            Flags = 0,
            Text = SelectedMessage.Text,
        };

        await _client.SendMessageAsync(request, CancellationToken.None);
    }

    private bool CanSetTargetToSelectedSubject()
    {
        return SelectedSubject is not null;
    }

    private void SetTargetToSelectedSubject()
    {
        if (SelectedSubject is null)
            return;

        Target = $"0x{SelectedSubject.Id:X8}";
        var conversation = Conversations.FirstOrDefault(c => c.PeerId == SelectedSubject.Id);
        if (conversation is not null)
        {
            SelectedConversation = conversation;
        }
    }

    partial void OnSelectedMessageChanged(MessageItemViewModel? value)
    {
        RetryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConversationChanged(ConversationItemViewModel? value)
    {
        if (value is null)
        {
            RefreshConversationMessages();
            return;
        }

        SelectedChannel = EnsureChannelOption(value.ChannelId);

        if (value.IsBroadcast)
        {
            Target = "0";
        }
        else if (value.PeerId.HasValue)
        {
            Target = $"0x{value.PeerId.Value:X8}";
            SelectedSubject = Subjects.FirstOrDefault(s => s.Id == value.PeerId.Value) ?? SelectedSubject;
        }

        RefreshConversationMessages();
        if (SelectedTabIndex == ChatTabIndex)
        {
            ClearConversationUnread(value);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == ChatTabIndex && SelectedConversation is not null)
        {
            ClearConversationUnread(SelectedConversation);
        }

        _ = LoadDeferredHistoryForTabAsync(value);
    }

    partial void OnUnreadChatCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadChat));
    }

    partial void OnAutoConnectOnDetectChanged(bool value)
    {
        if (value)
        {
            _portScanTimer.Start();
            _ = AutoScanAsync();
        }
        else
        {
            _portScanTimer.Stop();
        }
    }

    partial void OnSelectedTacticalEventChanged(TacticalEventViewModel? value)
    {
        if (value is null)
            return;
        if (value.SubjectId.HasValue)
        {
            SelectedSubject = Subjects.FirstOrDefault(s => s.Id == value.SubjectId.Value) ?? SelectedSubject;
        }
    }

    partial void OnSelectedSubjectChanged(SubjectViewModel? value)
    {
        HasSelectedSubject = value is not null;
        OnPropertyChanged(nameof(HasNoSelectedSubject));
        SetTargetToSelectedSubjectCommand.NotifyCanExecuteChanged();
        QuickSendCommand.NotifyCanExecuteChanged();
        SelectedSubjectDisplayId = value?.DisplayId ?? "--";
        foreach (var team in Teams)
        {
            team.SelectedSubject = value;
        }
    }

    partial void OnQuickMessageTextChanged(string value)
    {
        QuickSendCommand.NotifyCanExecuteChanged();
    }

    partial void OnOutgoingTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnOnlineThresholdSecondsChanged(int value)
    {
        RefreshSubjectStatuses();
    }

    partial void OnWeakThresholdSecondsChanged(int value)
    {
        RefreshSubjectStatuses();
    }

    partial void OnMapFollowLatestChanged(bool value)
    {
        Map.FollowLatest = value;
    }

    partial void OnMapEnableClusterChanged(bool value)
    {
        Map.EnableCluster = value;
        Map.Refresh();
    }

    partial void OnMapShowLogsChanged(bool value)
    {
        Map.SetLogVisibility(value);
    }

    partial void OnOfflineModeChanged(bool value)
    {
        Map.SetMqttVisibility(!value && MapShowMqtt);
        OnPropertyChanged(nameof(CanUseExternalFeeds));

        if (!_settingsLoaded)
            return;

        _aprsGateway.ApplySettings(BuildEffectiveAprsSettings(_settings.Aprs));
        _meshtasticMqtt.ApplySettings(BuildEffectiveMqttSettings(BuildMqttSettings()));
    }

    partial void OnMapShowMqttChanged(bool value)
    {
        Map.SetMqttVisibility(!OfflineMode && value);
    }

    partial void OnMapShowGibsChanged(bool value)
    {
        Map.SetGibsVisibility(value);
    }

    partial void OnSelectedMapBaseLayerChanged(MapBaseLayerOptionViewModel? value)
    {
        if (value is null)
            return;

        Map.SetBaseLayer(value.Kind);
    }

    partial void OnSelectedMqttSourceChanged(MqttSourceViewModel? value)
    {
        RemoveMqttSourceCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOfflineCacheRegionChanged(MapCacheRegionViewModel? value)
    {
        ApplyOfflineCacheRegionCommand.NotifyCanExecuteChanged();
        NotifyOfflineCacheBuildAvailabilityChanged();
        RemoveOfflineCacheRegionCommand.NotifyCanExecuteChanged();
        if (value is null)
            return;

        OfflineCacheRegionName = value.Name;
    }

    partial void OnIsOfflineCacheExportingChanged(bool value)
    {
        NotifyOfflineCacheBuildAvailabilityChanged();
    }

    partial void OnOfflineCacheRegionNameChanged(string value)
    {
        SaveOfflineCacheRegionCommand.NotifyCanExecuteChanged();
    }

    private void OnConnectionStateChanged(object? sender, (ConnectionState OldState, ConnectionState NewState, string? Reason) args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var loc = LocalizationService.Instance;
            ConnectionStateText = args.NewState switch
            {
                ConnectionState.Connecting => loc.GetString("Status.Connection.Connecting"),
                ConnectionState.Handshaking => loc.GetString("Status.Connection.Handshaking"),
                ConnectionState.Ready => loc.GetString("Status.Connection.Ready"),
                ConnectionState.Error => loc.GetString("Status.Connection.Error"),
                ConnectionState.Reconnecting => loc.GetString("Status.Connection.Reconnecting"),
                _ => loc.GetString("Status.Connection.Disconnected"),
            };
            SetLastError(args.Reason ?? string.Empty);
            IsConnected = args.NewState == ConnectionState.Ready;
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
            QuickSendCommand.NotifyCanExecuteChanged();
            SetTargetToSelectedSubjectCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SupportsTeamAppPosting));
        });
    }

    private void OnMessageAdded(object? sender, MessageEntry entry)
    {
        Dispatcher.UIThread.Post(() => ApplyMessageAdded(entry, MapSampleSource.Local));
    }

    private void OnMqttMessageReceived(object? sender, MessageEntry entry)
    {
        if (OfflineMode)
            return;

        Dispatcher.UIThread.Post(() => ApplyMessageAdded(entry, MapSampleSource.Mqtt));
    }

    private void ApplyMessageAdded(MessageEntry entry, MapSampleSource source)
    {
        var displayEntry = EnrichMessageForPreview(entry);
        var vm = new MessageItemViewModel(displayEntry, JumpToMessageLocation);
        if (entry.FromId.HasValue)
        {
            vm.From = ResolvePeerLabel(entry.FromId.Value);
        }
        if (entry.Direction == MessageDirection.Outgoing && entry.ToId.HasValue)
        {
            vm.To = ResolvePeerLabel(entry.ToId.Value);
        }
        Messages.Add(vm);
        if (SelectedMessage is null || SelectedMessage == Messages.LastOrDefault())
        {
            SelectedMessage = vm;
        }
        var conversation = UpsertConversation(entry);
        if (entry.Direction == MessageDirection.Incoming)
        {
            var isActive = SelectedTabIndex == ChatTabIndex &&
                           SelectedConversation is not null &&
                           SelectedConversation.Key == conversation.Key;
            if (!isActive)
            {
                conversation.UnreadCount++;
                RecalculateUnreadCount();
            }
        }
        RefreshConversationMessages();
        if (entry.Latitude.HasValue && entry.Longitude.HasValue)
        {
            Map.AddPoint(entry.FromId ?? entry.ToId, entry.Latitude.Value, entry.Longitude.Value, source);
        }
    }

    private void OnMessageUpdated(object? sender, MessageEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = Messages.FirstOrDefault(m => m.Seq == entry.Seq);
            if (vm != null)
                vm.UpdateFrom(EnrichMessageForPreview(entry));
            UpsertConversation(entry);
            RefreshConversationMessages();
            RetryCommand.NotifyCanExecuteChanged();
        });
    }

    private void JumpToMessageLocation(MessageItemViewModel message)
    {
        if (!message.HasLocation || !message.Latitude.HasValue || !message.Longitude.HasValue)
            return;

        SelectedTabIndex = DashboardTabIndex;
        Map.FocusOn(message.Latitude.Value, message.Longitude.Value);
    }

    private MessageEntry EnrichMessageForPreview(MessageEntry entry)
    {
        if (entry.Latitude.HasValue && entry.Longitude.HasValue)
            return entry;
        if (!LooksLikeLocationShareMessage(entry.Text))
            return entry;

        var candidateId = entry.Direction == MessageDirection.Outgoing
            ? entry.ToId ?? entry.FromId
            : entry.FromId ?? entry.ToId;
        if (!candidateId.HasValue)
            return entry;

        var subject = Subjects.FirstOrDefault(s => s.Id == candidateId.Value);
        if (subject?.Latitude is not double lat || subject.Longitude is not double lon)
            return entry;

        return entry with
        {
            Latitude = lat,
            Longitude = lon,
        };
    }

    private static uint? ResolveLocationPreviewCandidateNode(MessageItemViewModel message)
    {
        return message.Direction == MessageDirection.Outgoing
            ? message.ToId ?? message.FromId
            : message.FromId ?? message.ToId;
    }

    private void BackfillLocationPreviewForNode(uint nodeId, double latitude, double longitude)
    {
        foreach (var message in Messages)
        {
            if (message.HasLocation || !message.ShowLocationPreview)
                continue;

            var candidate = ResolveLocationPreviewCandidateNode(message);
            if (!candidate.HasValue || candidate.Value != nodeId)
                continue;

            message.ApplyLocation(latitude, longitude);
        }
    }

    private static bool LooksLikeLocationShareMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();
        foreach (var keyword in LocationMessageKeywords)
        {
            if (keyword.Any(ch => ch > 127))
            {
                if (trimmed.Contains(keyword, StringComparison.Ordinal))
                    return true;
                continue;
            }

            if (lower.Contains(keyword, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void OnDeviceInfo(object? sender, DeviceInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastDeviceInfo = info;
            var loc = LocalizationService.Instance;
            DeviceInfo = loc.Format("Status.Device.Info", info.Model, info.FirmwareVersion, info.ProtocolVersion);
            CapabilitiesInfo = loc.Format("Status.Device.Capabilities", info.Capabilities.MaxFrameLength, info.Capabilities.CapabilitiesMask);
            Config.ApplyCapabilities(info.Capabilities);
            OnPropertyChanged(nameof(SupportsTeamAppPosting));
        });
    }

    private void OnStatusUpdated(object? sender, StatusInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastStatusInfo = info;
            var loc = LocalizationService.Instance;
            var batteryText = info.BatteryPercent.HasValue ? $"{info.BatteryPercent}%" : loc.GetString("Common.Unknown");
            var chargingText = info.IsCharging ? loc.GetString("Common.Yes") : loc.GetString("Common.No");
            var dutyText = info.DutyCycleEnabled ? loc.GetString("Common.On") : loc.GetString("Common.Off");
            StatusPanel = loc.Format("Status.Device.Panel", batteryText, chargingText, info.LinkState, info.Channel, dutyText, info.LastError);
        });
    }

    private void OnAprsStatusChanged(object? sender, AprsIsStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastAprsStatus = status;
            var loc = LocalizationService.Instance;
            AprsConnectionText = status.State switch
            {
                AprsIsConnectionState.Connected => loc.GetString("Status.Aprs.Connected"),
                AprsIsConnectionState.Connecting => loc.GetString("Status.Aprs.Connecting"),
                AprsIsConnectionState.Disabled => string.IsNullOrWhiteSpace(status.Message)
                    ? loc.GetString("Status.Aprs.Disabled")
                    : loc.Format("Status.Aprs.DisabledWithMessage", status.Message),
                AprsIsConnectionState.Error => loc.Format("Status.Aprs.Error", status.Message),
                _ => loc.GetString("Status.Aprs.Disconnected"),
            };
            AprsConnectionColor = status.State switch
            {
                AprsIsConnectionState.Connected => "#22C55E",
                AprsIsConnectionState.Connecting => "#F59E0B",
                AprsIsConnectionState.Error => "#EF4444",
                AprsIsConnectionState.Disabled => "#9AA3AE",
                _ => "#9AA3AE",
            };
        });
    }

    private void OnAprsStatsChanged(object? sender, AprsGatewayStats stats)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastAprsStats = stats;
            AprsStatsText = LocalizationService.Instance.Format(
                "Status.Aprs.Stats",
                stats.Sent,
                stats.Dropped,
                stats.DedupeHits,
                stats.RateLimited,
                stats.Errors);
        });
    }

    private void OnGpsUpdated(object? sender, GpsEvent gps)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (gps.Latitude.HasValue && gps.Longitude.HasValue)
            {
                Map.AddPoint(_selfNodeId, gps.Latitude.Value, gps.Longitude.Value);
            }
        });
    }

    private async Task SendTeamCommandAsync(TeamCommandType type)
    {
        if (!IsConnected)
        {
            SetLastErrorLocalized("Error.DeviceNotConnected");
            return;
        }

        var targetLat = SelectedTacticalEvent?.Latitude ?? SelectedSubject?.Latitude;
        var targetLon = SelectedTacticalEvent?.Longitude ?? SelectedSubject?.Longitude;
        if ((type == TeamCommandType.RallyTo || type == TeamCommandType.MoveTo) &&
            (!targetLat.HasValue || !targetLon.HasValue))
        {
            SetLastErrorLocalized("Error.MissingTargetLocation");
            return;
        }

        var toId = SelectedSubject?.Id;
        var loc = LocalizationService.Instance;
        var commandLabel = type switch
        {
            TeamCommandType.RallyTo => loc.GetString("Ui.Dashboard.Command.RallyTo"),
            TeamCommandType.MoveTo => loc.GetString("Ui.Dashboard.Command.MoveTo"),
            TeamCommandType.Hold => loc.GetString("Ui.Dashboard.Command.Hold"),
            _ => type.ToString(),
        };
        var title = loc.Format("Command.DispatchTitle", commandLabel);
        var detail = loc.GetString("Command.Sending");
        var ev = new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.CommandIssued),
            TacticalEventKind.CommandIssued,
            title,
            detail,
            toId,
            toId.HasValue ? $"0x{toId:X8}" : null,
            targetLat,
            targetLon);
        var vm = new TacticalEventViewModel(ev);
        ApplySeverityOverride(vm);
        TacticalEvents.Insert(0, vm);

        try
        {
            var request = new TeamCommandRequest
            {
                CommandType = type,
                Latitude = targetLat ?? 0,
                Longitude = targetLon ?? 0,
                RadiusMeters = (ushort)Math.Clamp(CommandRadiusMeters, 0, 5000),
                Priority = (byte)Math.Clamp(CommandPriority, 0, 255),
                Note = CommandNote,
                To = toId,
                Channel = (byte)Math.Clamp(CommandChannel, 0, 255),
            };

            var result = await _client.SendTeamCommandAsync(request, CancellationToken.None);
            if (result == HostLinkErrorCode.Ok)
            {
                vm.Detail = loc.GetString("Command.Received");
            }
            else
            {
                vm.Detail = loc.Format("Command.Failed", result);
                vm.ApplySeverity(TacticalSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            vm.Detail = loc.Format("Command.Exception", ex.Message);
            vm.ApplySeverity(TacticalSeverity.Warning);
        }
    }

    public async Task SendTeamLocationPostAsync(TeamLocationSource source, double latitude, double longitude)
    {
        if (!IsConnected)
        {
            SetLastErrorLocalized("Error.DeviceNotConnected");
            return;
        }

        if (!HasTeams)
        {
            SetLastErrorLocalized("Error.TeamPosition.NoTeam");
            return;
        }

        var loc = LocalizationService.Instance;
        var markerName = ResolveTeamLocationSourceDisplayName(loc, source, (byte)source);
        var title = loc.Format("TeamPosition.DispatchTitle", markerName);
        var vm = new TacticalEventViewModel(new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.ChatLocation),
            TacticalEventKind.ChatLocation,
            title,
            loc.GetString("TeamPosition.Sending"),
            SelectedSubject?.Id,
            SelectedSubject is null ? null : $"0x{SelectedSubject.Id:X8}",
            latitude,
            longitude));
        ApplySeverityOverride(vm);
        TacticalEvents.Insert(0, vm);

        try
        {
            var request = new TeamLocationPostRequest
            {
                Source = source,
                Latitude = latitude,
                Longitude = longitude,
                AltitudeMeters = null,
                AccuracyMeters = 0,
                Label = markerName,
                To = null,
                Channel = (byte)Math.Clamp(CommandChannel, 0, 255),
            };

            var result = await _client.SendTeamLocationAsync(request, CancellationToken.None);
            if (result == HostLinkErrorCode.Ok)
            {
                vm.Detail = loc.GetString("TeamPosition.Received");
            }
            else
            {
                vm.Detail = loc.Format("TeamPosition.Failed", result);
                vm.ApplySeverity(TacticalSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            vm.Detail = loc.Format("TeamPosition.Exception", ex.Message);
            vm.ApplySeverity(TacticalSeverity.Warning);
        }
    }

    private void OnPositionUpdated(object? sender, PositionUpdate update)
    {
        Dispatcher.UIThread.Post(() => ApplyPositionUpdate(update, MapSampleSource.Local));
    }

    private void OnMqttPositionReceived(object? sender, PositionUpdate update)
    {
        if (OfflineMode)
            return;

        Dispatcher.UIThread.Post(() => ApplyPositionUpdate(update, MapSampleSource.Mqtt));
    }

    private void ApplyPositionUpdate(PositionUpdate update, MapSampleSource source)
    {
        var normalizedId = NormalizeNodeId(update.SourceId);
        if (update.Source == PositionSource.TeamWaypointApp ||
            (update.Source == PositionSource.TeamChatLocation && update.TeamLocationMarkerRaw.GetValueOrDefault() > 0))
        {
            var markerLabel = update.Source == PositionSource.TeamWaypointApp
                ? update.Label
                : BuildTeamLocationWaypointLabel(update);
            var pulseDuration = update.Source == PositionSource.TeamChatLocation &&
                                update.TeamLocationMarkerRaw.GetValueOrDefault() > 0
                ? TimeSpan.FromSeconds(10)
                : (TimeSpan?)null;
            Map.AddWaypoint(normalizedId, update.Latitude, update.Longitude, markerLabel, source, pulseDuration);
        }
        else
        {
            Map.AddPoint(normalizedId, update.Latitude, update.Longitude, source);
        }
        if (normalizedId is null)
            return;

        var sourceId = normalizedId.Value;
        var subject = Subjects.FirstOrDefault(s => s.Id == sourceId);
        var created = false;
        if (subject is null)
        {
            subject = new SubjectViewModel(sourceId);
            Subjects.Add(subject);
            EnsureSubjectTracked(subject);
            created = true;
        }
        subject.LastSeen = update.Timestamp;
        subject.Latitude = update.Latitude;
        subject.Longitude = update.Longitude;
        subject.UpdateStatus(ComputeStatus(update.Timestamp));
        BackfillLocationPreviewForNode(sourceId, update.Latitude, update.Longitude);
        if (created)
        {
            RebuildTeamLists();
        }
    }

    private string BuildTeamLocationWaypointLabel(PositionUpdate update)
    {
        var loc = LocalizationService.Instance;
        var markerText = ResolveTeamLocationSourceDisplayName(loc, update.TeamLocationMarker, update.TeamLocationMarkerRaw);
        var tsText = update.Timestamp.ToLocalTime().ToString("HH:mm");
        var sourceText = $"0x{update.SourceId:X8}";
        if (string.IsNullOrWhiteSpace(update.Label))
            return $"{markerText} · {sourceText} · {tsText}";

        var trimmed = update.Label.Trim();
        if (string.Equals(trimmed, markerText, StringComparison.OrdinalIgnoreCase))
            return $"{markerText} · {sourceText} · {tsText}";
        return $"{markerText} · {trimmed} · {tsText}";
    }

    private static string ResolveTeamLocationSourceDisplayName(LocalizationService loc, TeamLocationSource? source, byte? raw)
    {
        if (!source.HasValue)
        {
            return raw.HasValue
                ? loc.Format("Ui.Dashboard.TeamPosition.Unknown", raw.Value)
                : loc.GetString("Ui.Dashboard.TeamPosition.None");
        }

        return source.Value switch
        {
            TeamLocationSource.None => loc.GetString("Ui.Dashboard.TeamPosition.None"),
            TeamLocationSource.AreaCleared => loc.GetString("Ui.Dashboard.TeamPosition.AreaCleared"),
            TeamLocationSource.BaseCamp => loc.GetString("Ui.Dashboard.TeamPosition.BaseCamp"),
            TeamLocationSource.GoodFind => loc.GetString("Ui.Dashboard.TeamPosition.GoodFind"),
            TeamLocationSource.Rally => loc.GetString("Ui.Dashboard.TeamPosition.Rally"),
            TeamLocationSource.Sos => loc.GetString("Ui.Dashboard.TeamPosition.Sos"),
            _ => raw.HasValue
                ? loc.Format("Ui.Dashboard.TeamPosition.Unknown", raw.Value)
                : loc.GetString("Ui.Dashboard.TeamPosition.None"),
        };
    }

    private void OnNodeInfoUpdated(object? sender, NodeInfoUpdate info)
    {
        Dispatcher.UIThread.Post(() => ApplyNodeInfoUpdate(info, MapSampleSource.Local));
    }

    private void OnMqttNodeInfoReceived(object? sender, NodeInfoUpdate info)
    {
        if (OfflineMode)
            return;

        Dispatcher.UIThread.Post(() => ApplyNodeInfoUpdate(info, MapSampleSource.Mqtt));
    }

    private void OnMqttTacticalEventReceived(object? sender, TacticalEvent ev)
    {
        if (OfflineMode)
            return;

        OnTacticalEventReceived(sender, ev);
    }

    private void ApplyNodeInfoUpdate(NodeInfoUpdate info, MapSampleSource source)
    {
        var normalizedId = NormalizeNodeId(info.NodeId);
        if (normalizedId is null)
            return;

        if (normalizedId.Value != info.NodeId)
        {
            info = info with { NodeId = normalizedId.Value };
        }

        var subject = Subjects.FirstOrDefault(s => s.Id == info.NodeId);
        var created = false;
        if (subject is null)
        {
            subject = new SubjectViewModel(info.NodeId);
            Subjects.Add(subject);
            EnsureSubjectTracked(subject);
            created = true;
        }

        subject.ApplyNodeInfo(info);
        subject.LastSeen = info.LastHeard;
        subject.UpdateStatus(ComputeStatus(info.LastHeard));

        if (info.Latitude.HasValue && info.Longitude.HasValue)
        {
            subject.Latitude = info.Latitude;
            subject.Longitude = info.Longitude;
            Map.AddPoint(info.NodeId, info.Latitude.Value, info.Longitude.Value, source);
            BackfillLocationPreviewForNode(info.NodeId, info.Latitude.Value, info.Longitude.Value);
        }

        var label = ResolvePeerLabel(info.NodeId);
        foreach (var conv in Conversations.Where(c => c.PeerId == info.NodeId))
        {
            conv.UpdatePeerLabel(label);
        }

        foreach (var msg in Messages.Where(m => m.FromId == info.NodeId))
        {
            msg.From = label;
        }
        foreach (var msg in Messages.Where(m => m.Direction == MessageDirection.Outgoing && m.ToId == info.NodeId))
        {
            msg.To = label;
        }

        if (created)
        {
            RebuildTeamLists();
        }
    }

    private void OnTacticalEventReceived(object? sender, TacticalEvent ev)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new TacticalEventViewModel(ev);
            ApplySeverityOverride(vm);
            TacticalEvents.Insert(0, vm);
            if (ev.SubjectId.HasValue)
            {
                var normalizedId = NormalizeNodeId(ev.SubjectId.Value);
                if (normalizedId is null)
                    return;

                var subject = Subjects.FirstOrDefault(s => s.Id == normalizedId.Value);
                if (subject is null)
                {
                    if (ev.Kind != TacticalEventKind.Telemetry)
                    {
                        subject = new SubjectViewModel(normalizedId.Value);
                        Subjects.Add(subject);
                        EnsureSubjectTracked(subject);
                    }
                }

                if (subject is not null)
                {
                    subject.LastSeen = ev.Timestamp;
                    subject.UpdateStatus(ComputeStatus(ev.Timestamp));
                    if (ev.Kind == TacticalEventKind.Telemetry)
                    {
                        subject.ApplyTelemetry(ev.Detail, ev.Timestamp);
                    }
                }
            }
        });
    }

    private static bool TryParseNodeId(string value, out uint result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out result);
        }
        return uint.TryParse(value, out result);
    }

    private ChannelOptionViewModel EnsureChannelOption(byte channelId)
    {
        var existing = Channels.FirstOrDefault(ch => ch.Id == channelId);
        if (existing is not null)
            return existing;

        var option = new ChannelOptionViewModel(channelId);
        var insertIndex = 0;
        while (insertIndex < Channels.Count && Channels[insertIndex].Id < channelId)
        {
            insertIndex++;
        }

        Channels.Insert(insertIndex, option);
        return option;
    }

    private ConversationItemViewModel UpsertConversation(MessageEntry entry)
    {
        EnsureChannelOption(entry.ChannelId);

        var isBroadcast = IsBroadcastDestination(entry.ToId);
        var peerId = isBroadcast
            ? (uint?)null
            : entry.Direction == MessageDirection.Outgoing ? entry.ToId : entry.FromId;
        var channelId = entry.ChannelId;
        var key = isBroadcast ? $"BROADCAST:{channelId}" : $"DM:{peerId?.ToString("X8")}";

        var existing = Conversations.FirstOrDefault(c => c.Key == key);
        if (existing is null)
        {
            existing = new ConversationItemViewModel(key, isBroadcast, peerId, channelId);
            Conversations.Insert(0, existing);
        }

        var label = peerId.HasValue ? ResolvePeerLabel(peerId.Value) : null;
        existing.UpdateFrom(entry, label);

        Conversations.Remove(existing);
        Conversations.Insert(0, existing);

        if (SelectedConversation is null)
        {
            SelectedConversation = existing;
        }

        return existing;
    }

    private void ClearConversationUnread(ConversationItemViewModel conversation)
    {
        if (conversation.UnreadCount == 0)
            return;
        conversation.UnreadCount = 0;
        RecalculateUnreadCount();
    }

    private void RecalculateUnreadCount()
    {
        UnreadChatCount = Conversations.Sum(c => c.UnreadCount);
    }

    private async Task LoadDeferredHistoryForTabAsync(int tabIndex)
    {
        try
        {
            if ((tabIndex == EventsTabIndex || tabIndex == ExportTabIndex) &&
                !_eventsHistoryLoadTriggered &&
                !_eventsHistoryLoadInProgress)
            {
                _eventsHistoryLoadInProgress = true;
                try
                {
                    await Events.EnsureOlderHistoryLoadedAsync(_sqliteStore, CancellationToken.None);
                    _eventsHistoryLoadTriggered = true;
                }
                finally
                {
                    _eventsHistoryLoadInProgress = false;
                }
            }

            if ((tabIndex == LogsTabIndex || tabIndex == ExportTabIndex) &&
                !_logsHistoryLoadTriggered &&
                !_logsHistoryLoadInProgress)
            {
                _logsHistoryLoadInProgress = true;
                try
                {
                    await Logs.EnsureOlderHistoryLoadedAsync(_sqliteStore, CancellationToken.None);
                    _logsHistoryLoadTriggered = true;
                }
                finally
                {
                    _logsHistoryLoadInProgress = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load deferred history for tab {TabIndex}", tabIndex);
        }
    }

    private void RefreshConversationMessages()
    {
        FilteredMessages.Clear();

        IEnumerable<MessageItemViewModel> source = Messages;
        if (SelectedConversation is not null)
        {
            source = Messages.Where(m => MatchesConversation(m, SelectedConversation));
        }

        foreach (var msg in source)
            FilteredMessages.Add(msg);

        if (SelectedMessage is null || !FilteredMessages.Contains(SelectedMessage))
        {
            SelectedMessage = FilteredMessages.LastOrDefault();
        }
    }

    private void LoadHistoryFromSession()
    {
        using var _ = Map.BeginBulkUpdate();

        var nodeInfos = _sessionStore.SnapshotNodeInfos()
            .OrderBy(n => n.LastHeard)
            .ToList();
        foreach (var info in nodeInfos)
        {
            var normalizedId = NormalizeNodeId(info.NodeId);
            if (normalizedId is null)
                continue;

            var normalizedInfo = normalizedId.Value == info.NodeId ? info : info with { NodeId = normalizedId.Value };
            var subject = Subjects.FirstOrDefault(s => s.Id == normalizedInfo.NodeId);
            if (subject is null)
            {
                subject = new SubjectViewModel(normalizedInfo.NodeId);
                Subjects.Add(subject);
                EnsureSubjectTracked(subject);
            }
            subject.ApplyNodeInfo(normalizedInfo);
            subject.LastSeen = normalizedInfo.LastHeard;
            subject.UpdateStatus(ComputeStatus(normalizedInfo.LastHeard));
            if (normalizedInfo.Latitude.HasValue && normalizedInfo.Longitude.HasValue)
            {
                subject.Latitude = normalizedInfo.Latitude;
                subject.Longitude = normalizedInfo.Longitude;
                Map.AddPoint(normalizedInfo.NodeId, normalizedInfo.Latitude.Value, normalizedInfo.Longitude.Value);
            }
        }

        var messages = _sessionStore.SnapshotMessages()
            .OrderBy(m => m.Timestamp)
            .ToList();
        foreach (var entry in messages)
        {
            var displayEntry = EnrichMessageForPreview(entry);
            var vm = new MessageItemViewModel(displayEntry, JumpToMessageLocation);
            if (entry.FromId.HasValue)
            {
                vm.From = ResolvePeerLabel(entry.FromId.Value);
            }
            if (entry.Direction == MessageDirection.Outgoing && entry.ToId.HasValue)
            {
                vm.To = ResolvePeerLabel(entry.ToId.Value);
            }
            Messages.Add(vm);
            UpsertConversation(entry);
            if (entry.Latitude.HasValue && entry.Longitude.HasValue)
            {
                Map.AddPoint(entry.FromId ?? entry.ToId, entry.Latitude.Value, entry.Longitude.Value);
            }
        }

        if (Messages.Count > 0)
        {
            SelectedMessage = Messages.LastOrDefault();
        }

        var tacticalEvents = _sessionStore.SnapshotTacticalEvents()
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        foreach (var ev in tacticalEvents)
        {
            var vm = new TacticalEventViewModel(ev);
            ApplySeverityOverride(vm);
            TacticalEvents.Add(vm);
            if (ev.Kind == TacticalEventKind.Telemetry && ev.SubjectId.HasValue)
            {
                var subject = Subjects.FirstOrDefault(s => s.Id == ev.SubjectId.Value);
                if (subject is not null)
                {
                    subject.ApplyTelemetry(ev.Detail, ev.Timestamp);
                }
            }
        }

        var positions = _sessionStore.SnapshotPositions()
            .OrderBy(p => p.Timestamp)
            .ToList();
        foreach (var update in positions)
        {
            var normalizedId = NormalizeNodeId(update.SourceId);
            if (update.Source == PositionSource.TeamWaypointApp ||
                (update.Source == PositionSource.TeamChatLocation && update.TeamLocationMarkerRaw.GetValueOrDefault() > 0))
            {
                var markerLabel = update.Source == PositionSource.TeamWaypointApp
                    ? update.Label
                    : BuildTeamLocationWaypointLabel(update);
                Map.AddWaypoint(normalizedId, update.Latitude, update.Longitude, markerLabel);
            }
            else
            {
                Map.AddPoint(normalizedId, update.Latitude, update.Longitude);
            }
            if (normalizedId is null)
                continue;

            var sourceId = normalizedId.Value;
            var subject = Subjects.FirstOrDefault(s => s.Id == sourceId);
            if (subject is null)
            {
                subject = new SubjectViewModel(sourceId);
                Subjects.Add(subject);
                EnsureSubjectTracked(subject);
            }
            subject.LastSeen = update.Timestamp;
            subject.Latitude = update.Latitude;
            subject.Longitude = update.Longitude;
            subject.UpdateStatus(ComputeStatus(update.Timestamp));
            BackfillLocationPreviewForNode(sourceId, update.Latitude, update.Longitude);
        }

        RefreshConversationMessages();
        RebuildTeamLists();

        var teamState = _sessionStore.SnapshotTeamState();
        if (teamState is not null)
        {
            _lastTeamState = teamState;
            ApplyTeamState(teamState);
        }
    }

    private void RefreshLocalization()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var loc = LocalizationService.Instance;
            ConnectionStateText = _client.ConnectionState switch
            {
                ConnectionState.Connecting => loc.GetString("Status.Connection.Connecting"),
                ConnectionState.Handshaking => loc.GetString("Status.Connection.Handshaking"),
                ConnectionState.Ready => loc.GetString("Status.Connection.Ready"),
                ConnectionState.Error => loc.GetString("Status.Connection.Error"),
                ConnectionState.Reconnecting => loc.GetString("Status.Connection.Reconnecting"),
                _ => loc.GetString("Status.Connection.Disconnected"),
            };

            if (_lastDeviceInfo is not null)
            {
                DeviceInfo = loc.Format("Status.Device.Info", _lastDeviceInfo.Model, _lastDeviceInfo.FirmwareVersion, _lastDeviceInfo.ProtocolVersion);
                CapabilitiesInfo = loc.Format("Status.Device.Capabilities", _lastDeviceInfo.Capabilities.MaxFrameLength, _lastDeviceInfo.Capabilities.CapabilitiesMask);
            }

            if (_lastStatusInfo is not null)
            {
                var batteryText = _lastStatusInfo.BatteryPercent.HasValue ? $"{_lastStatusInfo.BatteryPercent}%" : loc.GetString("Common.Unknown");
                var chargingText = _lastStatusInfo.IsCharging ? loc.GetString("Common.Yes") : loc.GetString("Common.No");
                var dutyText = _lastStatusInfo.DutyCycleEnabled ? loc.GetString("Common.On") : loc.GetString("Common.Off");
                StatusPanel = loc.Format("Status.Device.Panel", batteryText, chargingText, _lastStatusInfo.LinkState, _lastStatusInfo.Channel, dutyText, _lastStatusInfo.LastError);
            }

            if (_lastAprsStatus is not null)
            {
                AprsConnectionText = _lastAprsStatus.State switch
                {
                    AprsIsConnectionState.Connected => loc.GetString("Status.Aprs.Connected"),
                    AprsIsConnectionState.Connecting => loc.GetString("Status.Aprs.Connecting"),
                    AprsIsConnectionState.Disabled => string.IsNullOrWhiteSpace(_lastAprsStatus.Message)
                        ? loc.GetString("Status.Aprs.Disabled")
                        : loc.Format("Status.Aprs.DisabledWithMessage", _lastAprsStatus.Message),
                    AprsIsConnectionState.Error => loc.Format("Status.Aprs.Error", _lastAprsStatus.Message),
                    _ => loc.GetString("Status.Aprs.Disconnected"),
                };
            }
            else
            {
                AprsConnectionText = loc.GetString("Status.Aprs.Disconnected");
            }

            if (_lastAprsStats is not null)
            {
                AprsStatsText = loc.Format(
                    "Status.Aprs.Stats",
                    _lastAprsStats.Sent,
                    _lastAprsStats.Dropped,
                    _lastAprsStats.DedupeHits,
                    _lastAprsStats.RateLimited,
                    _lastAprsStats.Errors);
            }

            foreach (var language in Languages)
            {
                language.RefreshLocalization();
            }
            foreach (var theme in Themes)
            {
                theme.RefreshLocalization();
            }
            foreach (var mapBaseLayer in MapBaseLayers)
            {
                mapBaseLayer.RefreshLocalization();
            }
            foreach (var port in Ports)
            {
                port.RefreshLocalization();
            }
            foreach (var channel in Channels)
            {
                channel.RefreshLocalization();
            }
            foreach (var conversation in Conversations)
            {
                conversation.RefreshLocalization();
            }
            foreach (var message in Messages)
            {
                message.RefreshLocalization();
            }
            foreach (var subject in Subjects)
            {
                subject.RefreshLocalization();
            }
            Events.RefreshLocalization();
            Map.RefreshLocalization();
            Config.RefreshLocalization();
            Logs.RefreshLocalization();
            Export.RefreshLocalization();

            if (_lastTeamState is not null)
            {
                ApplyTeamState(_lastTeamState);
            }

            var currentLanguage = Languages.FirstOrDefault(l => l.Culture.Name == loc.CurrentCulture.Name)
                ?? Languages.FirstOrDefault(l => l.Culture.TwoLetterISOLanguageName == loc.CurrentCulture.TwoLetterISOLanguageName);
            if (currentLanguage is not null && SelectedLanguage != currentLanguage)
            {
                SelectedLanguage = currentLanguage;
            }

            if (!string.IsNullOrWhiteSpace(_lastErrorKey))
            {
                LastError = _lastErrorArgs.Length == 0
                    ? LocalizationService.Instance.GetString(_lastErrorKey)
                    : LocalizationService.Instance.Format(_lastErrorKey, _lastErrorArgs);
            }

            if (!string.IsNullOrWhiteSpace(_earthdataStatusKey))
            {
                EarthdataStatusText = _earthdataStatusArgs.Length == 0
                    ? LocalizationService.Instance.GetString(_earthdataStatusKey)
                    : LocalizationService.Instance.Format(_earthdataStatusKey, _earthdataStatusArgs);
                if (!string.IsNullOrWhiteSpace(_earthdataStatusDetail))
                {
                    EarthdataStatusText = $"{EarthdataStatusText}\n{_earthdataStatusDetail}";
                }
            }
        });
    }

    private void OnTeamStateUpdated(object? sender, TeamStateEvent state)
    {
        _lastTeamState = state;
        Dispatcher.UIThread.Post(() => ApplyTeamState(state));
    }

    private void ApplyTeamState(TeamStateEvent state)
    {
        if (state.SelfId != 0)
        {
            _selfNodeId = state.SelfId;
        }

        var teamName = string.IsNullOrWhiteSpace(state.TeamName)
            ? LocalizationService.Instance.GetString("Status.Team.Untitled")
            : state.TeamName;
        var memberIds = new HashSet<uint>(state.Members
            .Select(m => m.NodeId == 0 && state.SelfId != 0 ? state.SelfId : m.NodeId)
            .Where(id => id != 0));

        foreach (var member in state.Members)
        {
            var nodeId = member.NodeId;
            if (nodeId == 0 && state.SelfId != 0)
            {
                nodeId = state.SelfId;
            }
            if (nodeId == 0)
                continue;

            var subject = Subjects.FirstOrDefault(s => s.Id == nodeId);
            if (subject is null)
            {
                subject = new SubjectViewModel(nodeId);
                Subjects.Add(subject);
                EnsureSubjectTracked(subject);
            }

            subject.TeamName = teamName;
            if (!string.IsNullOrWhiteSpace(member.Name) && string.IsNullOrWhiteSpace(subject.ShortName))
            {
                subject.ShortName = member.Name;
            }

            if (state.LastUpdateSeconds > 0 && member.LastSeenSeconds > 0 && state.LastUpdateSeconds >= member.LastSeenSeconds)
            {
                var delta = state.LastUpdateSeconds - member.LastSeenSeconds;
                var seenAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(delta);
                subject.LastSeen = seenAt;
                subject.UpdateStatus(ComputeStatus(seenAt));
            }
            else if (member.Online)
            {
                subject.LastSeen = DateTimeOffset.UtcNow;
                subject.UpdateStatus(ComputeStatus(subject.LastSeen));
            }
        }

        foreach (var subject in Subjects)
        {
            if (subject.TeamName == teamName && !memberIds.Contains(subject.Id))
            {
                subject.TeamName = string.Empty;
            }
        }

        RebuildTeamLists();
    }

    private uint? NormalizeNodeId(uint nodeId)
    {
        if (nodeId != 0)
            return nodeId;
        if (_selfNodeId.HasValue && _selfNodeId.Value != 0)
            return _selfNodeId.Value;
        return null;
    }

    public void SelectSubjectByNodeId(uint nodeId, bool switchToChatTab = false)
    {
        var normalizedId = NormalizeNodeId(nodeId);
        if (normalizedId is null)
            return;

        var subject = Subjects.FirstOrDefault(s => s.Id == normalizedId.Value);
        var created = false;
        if (subject is null)
        {
            subject = new SubjectViewModel(normalizedId.Value);
            Subjects.Add(subject);
            EnsureSubjectTracked(subject);
            created = true;
        }

        SelectedSubject = subject;
        if (switchToChatTab)
        {
            SelectedTabIndex = ChatTabIndex;
        }

        if (created)
        {
            RebuildTeamLists();
        }
    }

    private static bool MatchesConversation(MessageItemViewModel message, ConversationItemViewModel conversation)
    {
        if (conversation.IsBroadcast)
        {
            return message.IsBroadcast && message.ChannelId == conversation.ChannelId;
        }

        if (!conversation.PeerId.HasValue)
            return false;

        var peerId = conversation.PeerId.Value;
        return message.ChannelId == conversation.ChannelId &&
               (message.FromId == peerId || message.ToId == peerId);
    }

    private static bool IsBroadcastDestination(uint? toId)
    {
        return !toId.HasValue || toId.Value == 0 || toId.Value == uint.MaxValue;
    }

    private string ResolvePeerLabel(uint peerId)
    {
        var subject = Subjects.FirstOrDefault(s => s.Id == peerId);
        return subject?.DisplayName ?? $"0x{peerId:X8}";
    }

    private async Task AutoScanAsync()
    {
        if (!AutoConnectOnDetect || UseReplay)
            return;

        await RefreshPortsAsync();

        if (_autoConnectBusy)
            return;

        if (_client.ConnectionState is ConnectionState.Connecting or ConnectionState.Handshaking or ConnectionState.Ready or ConnectionState.Reconnecting)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastAutoConnectAttempt < TimeSpan.FromSeconds(3))
            return;

        var target = Ports.FirstOrDefault(p => string.Equals(p.PortName, _lastHotplugPortName, StringComparison.OrdinalIgnoreCase))
            ?? Ports.FirstOrDefault(p => string.Equals(p.PortName, _settings.LastPort, StringComparison.OrdinalIgnoreCase))
            ?? SelectedPort
            ?? Ports.FirstOrDefault();
        if (target is null)
            return;

        SelectedPort = target;
        _autoConnectBusy = true;
        _lastAutoConnectAttempt = now;
        try
        {
            await ConnectAsync();
        }
        catch
        {
            // ConnectAsync already sets LastError.
        }
        finally
        {
            _autoConnectBusy = false;
        }
    }

    private static bool IsLikelyExternalSerialPort(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return false;

        var name = portName.ToLowerInvariant();
        if (name.Contains("bluetooth-incoming-port", StringComparison.Ordinal) || name.EndsWith(".blth", StringComparison.Ordinal))
            return false;

        return name.Contains("usb", StringComparison.Ordinal) ||
               name.Contains("serial", StringComparison.Ordinal) ||
               name.Contains("uart", StringComparison.Ordinal) ||
               name.Contains("acm", StringComparison.Ordinal);
    }

    private void LoadSeverityRules(Dictionary<TacticalEventKind, TacticalSeverity> overrides)
    {
        SeverityRules.Clear();
        foreach (var kind in Enum.GetValues<TacticalEventKind>())
        {
            var severity = overrides.TryGetValue(kind, out var overrideSeverity)
                ? overrideSeverity
                : TacticalRules.GetDefaultSeverity(kind);
            var rule = new SeverityRuleViewModel(kind, severity);
            rule.PropertyChanged += (_, _) =>
            {
                ApplySeverityOverrides(kind, rule.Severity);
                ScheduleSettingsSave();
            };
            SeverityRules.Add(rule);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
            return;
        if (AutoSaveProperties.Contains(e.PropertyName))
        {
            ScheduleSettingsSave();
        }
    }

    private void OnMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
            return;

        if (string.Equals(e.PropertyName, nameof(MapViewModel.HasOfflineCacheSelection), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MapViewModel.IsOfflineCacheRunning), StringComparison.Ordinal))
        {
            SaveOfflineCacheRegionCommand.NotifyCanExecuteChanged();
            ApplyOfflineCacheRegionCommand.NotifyCanExecuteChanged();
            NotifyOfflineCacheBuildAvailabilityChanged();
        }
    }

    private void ApplySeverityOverrides(TacticalEventKind kind, TacticalSeverity severity)
    {
        foreach (var ev in TacticalEvents.Where(e => e.Kind == kind))
        {
            ev.ApplySeverity(severity);
        }
    }

    private void ApplySeverityOverride(TacticalEventViewModel ev)
    {
        var rule = SeverityRules.FirstOrDefault(r => r.Kind == ev.Kind);
        if (rule is not null)
        {
            ev.ApplySeverity(rule.Severity);
        }
    }

    private void RefreshSubjectStatuses()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var subject in Subjects)
        {
            subject.UpdateStatus(ComputeStatus(subject.LastSeen, now));
        }
    }

    private LinkStatus ComputeStatus(DateTimeOffset lastSeen)
    {
        return ComputeStatus(lastSeen, DateTimeOffset.UtcNow);
    }

    private LinkStatus ComputeStatus(DateTimeOffset lastSeen, DateTimeOffset now)
    {
        var age = now - lastSeen;
        if (age <= TimeSpan.FromSeconds(OnlineThresholdSeconds))
            return LinkStatus.Online;
        if (age <= TimeSpan.FromSeconds(WeakThresholdSeconds))
            return LinkStatus.Weak;
        return LinkStatus.Offline;
    }

    private void EnsureSubjectTracked(SubjectViewModel subject)
    {
        if (_subjectListeners.Add(subject.Id))
        {
            subject.PropertyChanged += OnSubjectPropertyChanged;
        }
    }

    private void OnSubjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubjectViewModel.TeamName))
        {
            RebuildTeamLists();
        }
    }

    private void RebuildTeamLists()
    {
        Teams.Clear();
        UngroupedSubjects.Clear();

        var grouped = Subjects
            .Where(s => !string.IsNullOrWhiteSpace(s.TeamName))
            .GroupBy(s => s.TeamName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var members = group.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            Teams.Add(new TeamGroupViewModel(group.Key, members, SelectedSubject));
        }

        foreach (var subject in Subjects.Where(s => string.IsNullOrWhiteSpace(s.TeamName))
                     .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            UngroupedSubjects.Add(subject);
        }

        OnPropertyChanged(nameof(HasTeams));
        OnPropertyChanged(nameof(HasNoTeams));
    }
}
