using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Services;
using TrailMateCenter.StateMachine;
using TrailMateCenter.Storage;
using TrailMateCenter.Transport;

namespace TrailMateCenter.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const int ChatTabIndex = 1;
    private readonly HostLinkClient _client;
    private readonly AprsGatewayService _aprsGateway;
    private readonly AprsIsClient _aprsClient;
    private readonly ISerialPortEnumerator _portEnumerator;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _sessionStore;
    private readonly ILogger<MainWindowViewModel> _logger;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _presenceTimer;
    private readonly DispatcherTimer _portScanTimer;
    private bool _autoConnectBusy;
    private DateTimeOffset _lastAutoConnectAttempt = DateTimeOffset.MinValue;
    private readonly HashSet<uint> _subjectListeners = new();
    private uint? _selfNodeId;

    public MainWindowViewModel(
        HostLinkClient client,
        AprsGatewayService aprsGateway,
        AprsIsClient aprsClient,
        ISerialPortEnumerator portEnumerator,
        SettingsStore settingsStore,
        LogStore logStore,
        SessionStore sessionStore,
        ExportService exportService,
        ILogger<MainWindowViewModel> logger)
    {
        _client = client;
        _aprsGateway = aprsGateway;
        _aprsClient = aprsClient;
        _portEnumerator = portEnumerator;
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
        _aprsGateway.StatsChanged += OnAprsStatsChanged;
        _aprsClient.StatusChanged += OnAprsStatusChanged;

        _ = LoadSettingsAsync();
        _ = RefreshPortsAsync();

        Map.EnableCluster = MapEnableCluster;
        Map.FollowLatest = MapFollowLatest;

        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _presenceTimer.Tick += (_, _) => RefreshSubjectStatuses();
        _presenceTimer.Start();

        LoadSeverityRules(new Dictionary<TacticalEventKind, TacticalSeverity>());

        _portScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _portScanTimer.Tick += async (_, _) => await AutoScanAsync();

        SelectedChannel = Channels.FirstOrDefault();

        LoadHistoryFromSession();

        Teams.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTeams));
            OnPropertyChanged(nameof(HasNoTeams));
        };
    }

    public ObservableCollection<SerialPortInfoViewModel> Ports { get; } = new();
    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
    public ObservableCollection<MessageItemViewModel> FilteredMessages { get; } = new();
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
    private string _connectionStateText = "未连接";

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
    private bool _mapFollowLatest = true;

    [ObservableProperty]
    private bool _mapEnableCluster = true;

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
    private string _aprsConnectionText = "APRS-IS: 未连接";

    [ObservableProperty]
    private string _aprsConnectionColor = "#9AA3AE";

    [ObservableProperty]
    private string _aprsStatsText = string.Empty;

    public bool HasNoSelectedSubject => !HasSelectedSubject;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private int _unreadChatCount;

    public bool HasUnreadChat => UnreadChatCount > 0;
    public bool HasTeams => Teams.Count > 0;
    public bool HasNoTeams => Teams.Count == 0;

    public IAsyncRelayCommand RefreshPortsCommand { get; }
    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand DisconnectCommand { get; }
    public IAsyncRelayCommand SendCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand QuickSendCommand { get; }
    public IRelayCommand SetTargetToSelectedSubjectCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand RallyCommand { get; }
    public IAsyncRelayCommand MoveCommand { get; }
    public IAsyncRelayCommand HoldCommand { get; }

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

            _aprsGateway.ApplySettings(_settings.Aprs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings");
        }
    }

    private async Task RefreshPortsAsync()
    {
        Ports.Clear();
        var ports = await _portEnumerator.GetPortsAsync(CancellationToken.None);
        var deduped = ports
            .GroupBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.FriendlyName))
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.Description))
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.VendorId) || !string.IsNullOrWhiteSpace(p.ProductId))
                .First())
            .OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase);

        foreach (var port in deduped)
            Ports.Add(new SerialPortInfoViewModel(port));

        SelectedPort ??= Ports.FirstOrDefault(p => p.PortName == _settings.LastPort) ?? Ports.FirstOrDefault();
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
            LastError = ex.Message;
        }
    }

    private async Task SaveSettingsAsync()
    {
        var overrides = SeverityRules.ToDictionary(r => r.Kind, r => r.Severity);
        var newSettings = _settings with
        {
            AutoReconnect = AutoReconnect,
            AutoConnectOnDetect = AutoConnectOnDetect,
            LastPort = SelectedPort?.PortName,
            LastReplayFile = ReplayFile,
            AckTimeoutMs = AckTimeoutMs,
            MaxRetries = MaxRetries,
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
        };
        _settings = newSettings;
        await _settingsStore.SaveAsync(newSettings, CancellationToken.None);
        _aprsGateway.ApplySettings(newSettings.Aprs);
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
            LastError = "目标格式无效（支持 0x1234 或十进制）";
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
            LastError = "未选择节点";
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
            LastError = "目标格式无效（支持 0x1234 或十进制）";
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

        if (value.IsBroadcast)
        {
            Target = "0";
            SelectedChannel = Channels.FirstOrDefault(ch => ch.Id == value.ChannelId) ?? SelectedChannel;
        }
        else if (value.PeerId.HasValue)
        {
            Target = $"0x{value.PeerId.Value:X8}";
            SelectedChannel = Channels.FirstOrDefault(ch => ch.Id == value.ChannelId) ?? SelectedChannel;
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
        if (value.Latitude.HasValue && value.Longitude.HasValue)
        {
            Map.FocusOn(value.Latitude.Value, value.Longitude.Value);
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

    private void OnConnectionStateChanged(object? sender, (ConnectionState OldState, ConnectionState NewState, string? Reason) args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ConnectionStateText = args.NewState switch
            {
                ConnectionState.Connecting => "连接中",
                ConnectionState.Handshaking => "握手中",
                ConnectionState.Ready => "已连接",
                ConnectionState.Error => "错误",
                ConnectionState.Reconnecting => "重连中",
                _ => "未连接",
            };
            LastError = args.Reason ?? string.Empty;
            IsConnected = args.NewState == ConnectionState.Ready;
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
            QuickSendCommand.NotifyCanExecuteChanged();
            SetTargetToSelectedSubjectCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnMessageAdded(object? sender, MessageEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new MessageItemViewModel(entry);
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
                Map.AddPoint(entry.FromId ?? entry.ToId, entry.Latitude.Value, entry.Longitude.Value);
            }
        });
    }

    private void OnMessageUpdated(object? sender, MessageEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var vm = Messages.FirstOrDefault(m => m.Seq == entry.Seq);
            if (vm != null)
                vm.UpdateFrom(entry);
            UpsertConversation(entry);
            RefreshConversationMessages();
            RetryCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnDeviceInfo(object? sender, DeviceInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DeviceInfo = $"{info.Model} | FW {info.FirmwareVersion} | Proto {info.ProtocolVersion}";
            CapabilitiesInfo = $"MaxFrame {info.Capabilities.MaxFrameLength} | Caps {info.Capabilities.CapabilitiesMask}";
            Config.ApplyCapabilities(info.Capabilities);
        });
    }

    private void OnStatusUpdated(object? sender, StatusInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var batteryText = info.BatteryPercent.HasValue ? $"{info.BatteryPercent}%" : "未知";
            StatusPanel = $"电量 {batteryText} | 充电 {(info.IsCharging ? "是" : "否")} | 状态 {info.LinkState} | Channel {info.Channel} | Duty {(info.DutyCycleEnabled ? "开" : "关")} | 最近错误 {info.LastError}";
        });
    }

    private void OnAprsStatusChanged(object? sender, AprsIsStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AprsConnectionText = status.State switch
            {
                AprsIsConnectionState.Connected => $"APRS-IS: 已连接",
                AprsIsConnectionState.Connecting => "APRS-IS: 连接中",
                AprsIsConnectionState.Disabled => string.IsNullOrWhiteSpace(status.Message) ? "APRS-IS: 未启用" : $"APRS-IS: {status.Message}",
                AprsIsConnectionState.Error => $"APRS-IS: 错误 {status.Message}",
                _ => "APRS-IS: 未连接",
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
            AprsStatsText = $"TX {stats.Sent} · Drop {stats.Dropped} · Dedupe {stats.DedupeHits} · Rate {stats.RateLimited} · Err {stats.Errors}";
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
            LastError = "未连接设备";
            return;
        }

        var targetLat = SelectedTacticalEvent?.Latitude ?? SelectedSubject?.Latitude;
        var targetLon = SelectedTacticalEvent?.Longitude ?? SelectedSubject?.Longitude;
        if ((type == TeamCommandType.RallyTo || type == TeamCommandType.MoveTo) &&
            (!targetLat.HasValue || !targetLon.HasValue))
        {
            LastError = "缺少目标坐标（请选择事件或队员位置）";
            return;
        }

        var toId = SelectedSubject?.Id;
        var title = $"下发指令 {type}";
        var detail = "发送中...";
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
                vm.Detail = "设备已接收指令";
            }
            else
            {
                vm.Detail = $"指令失败: {result}";
                vm.ApplySeverity(TacticalSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            vm.Detail = $"指令异常: {ex.Message}";
            vm.ApplySeverity(TacticalSeverity.Warning);
        }
    }

    private void OnPositionUpdated(object? sender, PositionUpdate update)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var normalizedId = NormalizeNodeId(update.SourceId);
            if (update.Source == PositionSource.TeamWaypointApp)
            {
                Map.AddWaypoint(normalizedId, update.Latitude, update.Longitude, update.Label);
            }
            else
            {
                Map.AddPoint(normalizedId, update.Latitude, update.Longitude);
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
            if (created)
            {
                RebuildTeamLists();
            }
        });
    }

    private void OnNodeInfoUpdated(object? sender, NodeInfoUpdate info)
    {
        Dispatcher.UIThread.Post(() =>
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
                Map.AddPoint(info.NodeId, info.Latitude.Value, info.Longitude.Value);
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
        });
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

    private ConversationItemViewModel UpsertConversation(MessageEntry entry)
    {
        var isBroadcast = entry.ToId is null || entry.ToId == 0;
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
            var vm = new MessageItemViewModel(entry);
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
            if (update.Source == PositionSource.TeamWaypointApp)
            {
                Map.AddWaypoint(normalizedId, update.Latitude, update.Longitude, update.Label);
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
        }

        RefreshConversationMessages();
        RebuildTeamLists();

        var teamState = _sessionStore.SnapshotTeamState();
        if (teamState is not null)
        {
            ApplyTeamState(teamState);
        }
    }

    private void OnTeamStateUpdated(object? sender, TeamStateEvent state)
    {
        Dispatcher.UIThread.Post(() => ApplyTeamState(state));
    }

    private void ApplyTeamState(TeamStateEvent state)
    {
        if (state.SelfId != 0)
        {
            _selfNodeId = state.SelfId;
        }

        var teamName = string.IsNullOrWhiteSpace(state.TeamName) ? "未命名小队" : state.TeamName;
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

        var target = Ports.FirstOrDefault(p => p.PortName == _settings.LastPort) ?? SelectedPort ?? Ports.FirstOrDefault();
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

    private void LoadSeverityRules(Dictionary<TacticalEventKind, TacticalSeverity> overrides)
    {
        SeverityRules.Clear();
        foreach (var kind in Enum.GetValues<TacticalEventKind>())
        {
            var severity = overrides.TryGetValue(kind, out var overrideSeverity)
                ? overrideSeverity
                : TacticalRules.GetDefaultSeverity(kind);
            var rule = new SeverityRuleViewModel(kind, severity);
            rule.PropertyChanged += (_, _) => ApplySeverityOverrides(kind, rule.Severity);
            SeverityRules.Add(rule);
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
