using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.StateMachine;
using TrailMateCenter.Storage;
using TrailMateCenter.Transport;

namespace TrailMateCenter.Services;

public sealed class HostLinkClient : IAsyncDisposable
{
    private const byte TeamStateFlagInTeam = 1 << 0;
    private const byte TeamStateFlagPendingJoin = 1 << 1;
    private const byte TeamStateFlagHasTeamId = 1 << 4;

    private readonly ILogger<HostLinkClient> _logger;
    private readonly LogStore _logStore;
    private readonly SessionStore _sessionStore;
    private readonly Func<TransportEndpoint, IHostLinkTransport> _transportFactory;
    private readonly HostLinkCodec _codec = new();
    private readonly RequestTracker _requests = new();
    private readonly ConnectionStateMachine _stateMachine = new();
    private readonly AppDataReassembler _appDataReassembler = new();
    private readonly AppDataDecoder _appDataDecoder = new();
    private readonly Dictionary<ushort, MessageEntry> _pendingMessages = new();
    private readonly Queue<MessageEntry> _awaitingTxResults = new();
    private readonly object _gate = new();
    private readonly byte[] _teamId = new byte[8];
    private uint _teamKeyId;
    private byte _teamChannel;
    private bool _hasTeamContext;
    private uint _teamSelfId;
    private long _teamAppDataTraceCounter;

    private IHostLinkTransport? _transport;
    private TransportEndpoint? _endpoint;
    private ConnectionOptions _options = new();
    private CancellationTokenSource? _cts;
    private Task? _watchdogTask;
    private Task? _reconnectTask;
    private TaskCompletionSource<DeviceInfo>? _helloTcs;
    private TaskCompletionSource<DeviceConfig>? _configTcs;

    public HostLinkClient(
        ILogger<HostLinkClient> logger,
        LogStore logStore,
        SessionStore sessionStore,
        Func<TransportEndpoint, IHostLinkTransport>? transportFactory = null)
    {
        _logger = logger;
        _logStore = logStore;
        _sessionStore = sessionStore;
        _transportFactory = transportFactory ?? CreateTransport;
        _codec.DecodeError += OnDecodeError;
        _codec.RawFrameDecoded += OnRawFrameDecoded;
        _stateMachine.StateChanged += (_, args) => ConnectionStateChanged?.Invoke(this, args);
    }

    public event EventHandler<(ConnectionState OldState, ConnectionState NewState, string? Reason)>? ConnectionStateChanged;
    public event EventHandler<DeviceInfo>? DeviceInfoReceived;
    public event EventHandler<StatusInfo>? StatusUpdated;
    public event EventHandler<GpsEvent>? GpsUpdated;
    public event EventHandler<PositionUpdate>? PositionUpdated;
    public event EventHandler<NodeInfoUpdate>? NodeInfoUpdated;
    public event EventHandler<TeamStateEvent>? TeamStateUpdated;
    public event EventHandler<MessageEntry>? MessageAdded;
    public event EventHandler<MessageEntry>? MessageUpdated;
    public event EventHandler<TacticalEvent>? TacticalEventReceived;
    public event EventHandler<HostLinkEvent>? EventReceived;
    public event EventHandler<HostLinkDecodeError>? ProtocolError;
    public event EventHandler<TransportError>? TransportError;
    public event EventHandler? TeamContextChanged;

    public ConnectionState ConnectionState => _stateMachine.State;
    public string? LastError => _stateMachine.LastError;
    public DeviceInfo? DeviceInfo { get; private set; }
    public bool HasTeamContext
    {
        get
        {
            lock (_gate)
            {
                return _hasTeamContext;
            }
        }
    }
    public bool SupportsTxAppDataCommand =>
        DeviceInfo?.Capabilities.CapabilitiesMask.HasFlag(HostLinkCapabilities.CapTxAppData) == true;
    public StatusInfo? Status { get; private set; }

    public async Task ConnectAsync(TransportEndpoint endpoint, ConnectionOptions options, CancellationToken cancellationToken)
    {
        ResetTeamContext(notify: true);
        _endpoint = endpoint;
        _options = options;
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await OpenTransportAsync(_cts.Token);
        await HandshakeAsync(_cts.Token);

        _watchdogTask = Task.Run(() => WatchdogLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        ResetTeamContext(notify: true);
        if (_transport is not null)
        {
            await _transport.CloseAsync(cancellationToken);
            await _transport.DisposeAsync();
        }
        _transport = null;
        _stateMachine.Transition(ConnectionState.Disconnected);
    }

    public async Task<MessageEntry> SendMessageAsync(MessageSendRequest request, CancellationToken cancellationToken)
    {
        EnsureReady();
        var isBroadcast = IsBroadcastDestination(request.ToId);

        var payload = HostLinkSerializer.BuildCmdTxMsgPayload(request);
        var pending = _requests.Register(HostLinkFrameType.CmdTxMsg, _options.AckTimeout, _options.MaxRetries);
        var frame = new HostLinkFrame(HostLinkFrameType.CmdTxMsg, pending.Seq, payload);
        pending.FrameBytes = HostLinkCodec.Encode(frame);
        pending.LastSendAt = DateTimeOffset.UtcNow;

        var message = new MessageEntry
        {
            Direction = MessageDirection.Outgoing,
            From = "PC",
            To = $"0x{request.ToId:X8}",
            FromId = null,
            ToId = request.ToId,
            ChannelId = request.Channel,
            Channel = request.Channel.ToString(),
            Text = request.Text,
            Status = MessageDeliveryStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            DeviceTimestamp = null,
            Seq = pending.Seq,
        };

        lock (_gate)
        {
            _pendingMessages[pending.Seq] = message;
        }

        _sessionStore.AddMessage(message);
        MessageAdded?.Invoke(this, message);

        await WriteAsync(pending.FrameBytes, cancellationToken);

        if (isBroadcast)
        {
            lock (_gate)
            {
                if (_pendingMessages.TryGetValue(pending.Seq, out var tracked))
                {
                    tracked.Status = MessageDeliveryStatus.Succeeded;
                    tracked.ErrorMessage = null;
                    _sessionStore.UpdateMessage(tracked);
                    MessageUpdated?.Invoke(this, tracked);
                    _pendingMessages.Remove(pending.Seq);
                }
                _requests.Complete(pending.Seq);
            }
        }

        return message;
    }

    public async Task<HostLinkErrorCode> SendTeamTextAsync(
        string text,
        uint? to,
        byte channel,
        string? teamConversationKey,
        CancellationToken cancellationToken)
    {
        EnsureReady();
        if (!SupportsTxAppDataCommand)
            return HostLinkErrorCode.Unsupported;
        if (string.IsNullOrWhiteSpace(text))
            return HostLinkErrorCode.InvalidParam;

        var outboundContext = ResolveOutboundTeamContext(channel, teamConversationKey);
        var payload = TeamChatEncoder.BuildTextPayload(text);

        var appRequest = new AppDataSendRequest
        {
            Portnum = AppDataDecoder.TeamChatPort,
            From = outboundContext.FromId,
            To = to ?? 0,
            Channel = outboundContext.Channel,
            Flags = outboundContext.HasTeamContext ? HostLinkAppDataFlags.HasTeamMetadata : 0,
            TeamId = outboundContext.HasTeamContext ? outboundContext.TeamId : new byte[8],
            TeamKeyId = outboundContext.HasTeamContext ? outboundContext.TeamKeyId : 0,
            Payload = payload,
        };

        var isBroadcast = IsBroadcastDestination(to);
        var outgoingMessage = new MessageEntry
        {
            Direction = MessageDirection.Outgoing,
            From = "PC",
            To = isBroadcast ? "broadcast" : $"0x{to!.Value:X8}",
            FromId = null,
            ToId = isBroadcast ? null : to,
            ChannelId = outboundContext.Channel,
            Channel = outboundContext.Channel.ToString(),
            Text = text,
            Status = MessageDeliveryStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            DeviceTimestamp = null,
            IsTeamChat = true,
            TeamConversationKey = BuildTeamConversationKeyForOutbound(
                outboundContext.HasTeamContext,
                outboundContext.TeamId,
                outboundContext.TeamKeyId,
                teamConversationKey),
        };

        _sessionStore.AddMessage(outgoingMessage);
        MessageAdded?.Invoke(this, outgoingMessage);

        var result = await SendTeamAppDataWithCompatibilityFallbackAsync(appRequest, cancellationToken);
        outgoingMessage.Status = result == HostLinkErrorCode.Ok
            ? MessageDeliveryStatus.Succeeded
            : MessageDeliveryStatus.Failed;
        outgoingMessage.ErrorMessage = result == HostLinkErrorCode.Ok ? null : $"APPDATA {result}";
        _sessionStore.UpdateMessage(outgoingMessage);
        MessageUpdated?.Invoke(this, outgoingMessage);

        return result;
    }

    public async Task<DeviceConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        EnsureReady();

        _configTcs = new TaskCompletionSource<DeviceConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        var payload = HostLinkSerializer.BuildCmdGetConfigPayload();
        var ack = await SendCommandWithAckAsync(HostLinkFrameType.CmdGetConfig, payload, cancellationToken);
        if (ack != HostLinkErrorCode.Ok)
            throw new InvalidOperationException($"Config read failed: {ack}");
        return await _configTcs.Task.WaitAsync(cancellationToken);
    }

    public async Task SetConfigAsync(DeviceConfig config, CancellationToken cancellationToken)
    {
        EnsureReady();
        var payload = HostLinkSerializer.BuildCmdSetConfigPayload(config);
        var ack = await SendCommandWithAckAsync(HostLinkFrameType.CmdSetConfig, payload, cancellationToken);
        if (ack != HostLinkErrorCode.Ok)
            throw new InvalidOperationException($"Config write failed: {ack}");
    }

    public async Task<HostLinkErrorCode> SendTeamCommandAsync(TeamCommandRequest request, CancellationToken cancellationToken)
    {
        EnsureReady();
        if (!SupportsTxAppDataCommand)
            return HostLinkErrorCode.Unsupported;

        var teamRequest = new TeamCommandRequest
        {
            CommandType = request.CommandType,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            Priority = request.Priority,
            Note = request.Note,
            To = request.To,
            Channel = request.Channel,
        };

        var outboundContext = ResolveOutboundTeamContext(request.Channel, teamConversationKey: null);
        var payload = TeamChatEncoder.BuildCommandPayload(teamRequest);
        var appRequest = new AppDataSendRequest
        {
            Portnum = AppDataDecoder.TeamChatPort,
            From = outboundContext.FromId,
            To = request.To ?? 0,
            Channel = outboundContext.Channel,
            Flags = outboundContext.HasTeamContext ? HostLinkAppDataFlags.HasTeamMetadata : 0,
            TeamId = outboundContext.HasTeamContext ? outboundContext.TeamId : new byte[8],
            TeamKeyId = outboundContext.HasTeamContext ? outboundContext.TeamKeyId : 0,
            Payload = payload,
        };

        return await SendTeamAppDataWithCompatibilityFallbackAsync(appRequest, cancellationToken);
    }

    public async Task<HostLinkErrorCode> SendTeamLocationAsync(TeamLocationPostRequest request, CancellationToken cancellationToken)
    {
        EnsureReady();
        if (!SupportsTxAppDataCommand)
            return HostLinkErrorCode.Unsupported;

        var outboundContext = ResolveOutboundTeamContext(request.Channel, teamConversationKey: null);
        var payload = TeamChatEncoder.BuildLocationPayload(request);

        var appRequest = new AppDataSendRequest
        {
            Portnum = AppDataDecoder.TeamChatPort,
            From = outboundContext.FromId,
            To = request.To ?? 0,
            Channel = outboundContext.Channel,
            Flags = outboundContext.HasTeamContext ? HostLinkAppDataFlags.HasTeamMetadata : 0,
            TeamId = outboundContext.HasTeamContext ? outboundContext.TeamId : new byte[8],
            TeamKeyId = outboundContext.HasTeamContext ? outboundContext.TeamKeyId : 0,
            Payload = payload,
        };

        return await SendTeamAppDataWithCompatibilityFallbackAsync(appRequest, cancellationToken);
    }

    private async Task<HostLinkErrorCode> SendTeamAppDataWithCompatibilityFallbackAsync(
        AppDataSendRequest appRequest,
        CancellationToken cancellationToken)
    {
        var attempts = BuildTeamAppDataCompatibilityAttempts(appRequest);
        var wireFormats = new (string Name, bool IncludeTimestampField)[]
        {
            ("no_ts", false),
            ("with_ts", true),
        };

        var traceId = System.Threading.Interlocked.Increment(ref _teamAppDataTraceCounter);
        WriteTeamAppDataTrace(
            LogLevel.Information,
            "APPDATA_TRACE_START",
            $"trace={traceId} attemptCount={attempts.Count * wireFormats.Length} original={DescribeAppDataRequest(appRequest)} payload={DescribeTeamPayload(appRequest.Payload)}");

        var lastResult = HostLinkErrorCode.Internal;
        for (var wireIndex = 0; wireIndex < wireFormats.Length; wireIndex++)
        {
            var wire = wireFormats[wireIndex];
            for (var i = 0; i < attempts.Count; i++)
            {
                var attempt = attempts[i];
                var attemptIndex = i + 1;
                var globalAttemptIndex = wireIndex * attempts.Count + attemptIndex;
                var totalAttemptCount = attempts.Count * wireFormats.Length;
                WriteTeamAppDataTrace(
                    LogLevel.Information,
                    "APPDATA_TX",
                    $"trace={traceId} attempt={globalAttemptIndex}/{totalAttemptCount} candidate={attemptIndex}/{attempts.Count} wire={wire.Name} {DescribeAppDataRequest(attempt)} payload={DescribeTeamPayload(attempt.Payload)}");
                if (globalAttemptIndex > 1)
                {
                    _logger.LogWarning(
                        "CMD_TX_APPDATA compatibility retry #{Retry}: wire={Wire}, port={Port}, from=0x{From:X8}, to=0x{To:X8}, channel={Channel}, flags=0x{Flags:X2}, teamKeyId=0x{TeamKeyId:X8}",
                        globalAttemptIndex - 1,
                        wire.Name,
                        attempt.Portnum,
                        attempt.From,
                        attempt.To,
                        attempt.Channel,
                        (byte)attempt.Flags,
                        attempt.TeamKeyId);
                }

                lastResult = await SendAppDataAsync(attempt, wire.IncludeTimestampField, cancellationToken);
                WriteTeamAppDataTrace(
                    lastResult == HostLinkErrorCode.Ok ? LogLevel.Information : LogLevel.Warning,
                    "APPDATA_ACK",
                    $"trace={traceId} attempt={globalAttemptIndex}/{totalAttemptCount} candidate={attemptIndex}/{attempts.Count} wire={wire.Name} ack={lastResult} {DescribeAppDataRequest(attempt)}");
                if (lastResult == HostLinkErrorCode.Ok)
                {
                    WriteTeamAppDataTrace(
                        LogLevel.Information,
                        "APPDATA_TRACE_DONE",
                        $"trace={traceId} result=Ok attemptsUsed={globalAttemptIndex} wire={wire.Name}");
                    return lastResult;
                }
                if (lastResult != HostLinkErrorCode.InvalidParam)
                {
                    WriteTeamAppDataTrace(
                        LogLevel.Warning,
                        "APPDATA_TRACE_DONE",
                        $"trace={traceId} result={lastResult} attemptsUsed={globalAttemptIndex} wire={wire.Name}");
                    return lastResult;
                }
            }

            if (wireIndex + 1 < wireFormats.Length)
            {
                WriteTeamAppDataTrace(
                    LogLevel.Warning,
                    "APPDATA_TX",
                    $"trace={traceId} switching_wire_to={wireFormats[wireIndex + 1].Name} after {attempts.Count} InvalidParam ACKs on wire={wire.Name}");
            }
        }

        WriteTeamAppDataTrace(
            LogLevel.Warning,
            "APPDATA_TRACE_DONE",
            $"trace={traceId} result={lastResult} attemptsUsed={attempts.Count * wireFormats.Length}");
        return lastResult;
    }

    private static List<AppDataSendRequest> BuildTeamAppDataCompatibilityAttempts(AppDataSendRequest original)
    {
        static string BuildSignature(AppDataSendRequest request)
        {
            return $"{request.Portnum}:{request.From}:{request.To}:{request.Channel}:{(byte)request.Flags}:{Convert.ToHexString(request.TeamId)}:{request.TeamKeyId}";
        }

        static AppDataSendRequest WithoutTeamMetadata(AppDataSendRequest request) => request with
        {
            Flags = 0,
            TeamId = new byte[8],
            TeamKeyId = 0,
        };

        var attempts = new List<AppDataSendRequest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(AppDataSendRequest request)
        {
            if (seen.Add(BuildSignature(request)))
            {
                attempts.Add(request);
            }
        }

        Add(original);
        if (original.From != 0)
        {
            Add(original with { From = 0 });
        }

        if (original.To == 0)
        {
            Add(original with { To = uint.MaxValue });
            if (original.From != 0)
            {
                Add(original with { To = uint.MaxValue, From = 0 });
            }
        }

        if (original.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata))
        {
            var noMetadata = WithoutTeamMetadata(original);
            Add(noMetadata);
            if (original.From != 0)
            {
                Add(noMetadata with { From = 0 });
            }

            if (original.To == 0)
            {
                Add(noMetadata with { To = uint.MaxValue });
                if (original.From != 0)
                {
                    Add(noMetadata with { To = uint.MaxValue, From = 0 });
                }
            }
        }

        return attempts;
    }

    private void WriteTeamAppDataTrace(LogLevel level, string code, string message)
    {
        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Message = message,
            RawCode = code,
        });
    }

    private static string DescribeAppDataRequest(AppDataSendRequest request)
    {
        var teamId = request.TeamId is { Length: 8 } ? Convert.ToHexString(request.TeamId) : "INVALID";
        return $"port={request.Portnum},from=0x{request.From:X8},to=0x{request.To:X8},ch={request.Channel},flags=0x{(byte)request.Flags:X2},teamId={teamId},teamKeyId=0x{request.TeamKeyId:X8},len={request.Payload?.Length ?? 0}";
    }

    private static string DescribeTeamPayload(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty";

        var reader = new HostLinkSpanReader(payload);
        if (!reader.TryReadByte(out var version) ||
            !reader.TryReadByte(out var type) ||
            !reader.TryReadUInt16(out var flags) ||
            !reader.TryReadUInt32(out var msgId) ||
            !reader.TryReadUInt32(out var ts) ||
            !reader.TryReadUInt32(out var from))
        {
            var previewLen = Math.Min(payload.Length, 24);
            return $"len={payload.Length},hex={Convert.ToHexString(payload.AsSpan(0, previewLen))}";
        }

        var typeLabel = type switch
        {
            1 => "Text",
            2 => "Location",
            3 => "Command",
            _ => $"Type{type}",
        };
        return $"v={version},type={typeLabel}({type}),flags=0x{flags:X4},msgId=0x{msgId:X8},ts={ts},from=0x{from:X8},len={payload.Length}";
    }

    private async Task SendCommandAsync(HostLinkFrameType commandType, byte[] payload, CancellationToken cancellationToken)
    {
        var pending = _requests.Register(commandType, _options.AckTimeout, _options.MaxRetries);
        var frame = new HostLinkFrame(commandType, pending.Seq, payload);
        pending.FrameBytes = HostLinkCodec.Encode(frame);
        pending.LastSendAt = DateTimeOffset.UtcNow;
        await WriteAsync(pending.FrameBytes, cancellationToken);
    }

    private async Task<HostLinkErrorCode> SendCommandWithAckAsync(HostLinkFrameType commandType, byte[] payload, CancellationToken cancellationToken)
    {
        var pending = _requests.Register(commandType, _options.AckTimeout, _options.MaxRetries);
        var frame = new HostLinkFrame(commandType, pending.Seq, payload);
        pending.FrameBytes = HostLinkCodec.Encode(frame);
        pending.LastSendAt = DateTimeOffset.UtcNow;
        await WriteAsync(pending.FrameBytes, cancellationToken);

        var totalTimeout = TimeSpan.FromMilliseconds(_options.AckTimeout.TotalMilliseconds * (pending.MaxRetries + 1));
        try
        {
            var code = await pending.Acked.Task.WaitAsync(totalTimeout, cancellationToken);
            if (commandType != HostLinkFrameType.CmdTxMsg)
                _requests.Complete(pending.Seq);
            return code;
        }
        catch (TimeoutException)
        {
            return HostLinkErrorCode.Internal;
        }
    }

    private async Task<HostLinkErrorCode> SendAppDataAsync(
        AppDataSendRequest request,
        bool includeTimestampField,
        CancellationToken cancellationToken)
    {
        var maxFrame = DeviceInfo?.Capabilities.MaxFrameLength ?? HostLinkConstants.DefaultMaxPayloadLength;
        const int baseHeaderSize = 4 + 4 + 4 + 1 + 1 + 8 + 4 + 4 + 4 + 2;
        var headerSize = includeTimestampField ? baseHeaderSize + 4 : baseHeaderSize;
        var maxChunk = Math.Max(1, maxFrame - headerSize);
        var payload = request.Payload ?? Array.Empty<byte>();

        if (payload.Length == 0)
        {
            var emptyPayload = HostLinkSerializer.BuildCmdTxAppDataPayload(
                request,
                0,
                Array.Empty<byte>(),
                includeTimestampField);
            return await SendCommandWithAckAsync(HostLinkFrameType.CmdTxAppData, emptyPayload, cancellationToken);
        }

        HostLinkErrorCode lastResult = HostLinkErrorCode.Ok;
        var offset = 0;
        while (offset < payload.Length)
        {
            var chunkLen = Math.Min(maxChunk, payload.Length - offset);
            var chunk = new byte[chunkLen];
            Array.Copy(payload, offset, chunk, 0, chunkLen);
            var cmdPayload = HostLinkSerializer.BuildCmdTxAppDataPayload(
                request,
                (uint)offset,
                chunk,
                includeTimestampField);
            lastResult = await SendCommandWithAckAsync(HostLinkFrameType.CmdTxAppData, cmdPayload, cancellationToken);
            if (lastResult != HostLinkErrorCode.Ok)
                return lastResult;
            offset += chunkLen;
        }

        return lastResult;
    }

    private async Task OpenTransportAsync(CancellationToken cancellationToken)
    {
        _stateMachine.Transition(ConnectionState.Connecting);

        if (_transport is not null)
        {
            _transport.TransportError -= OnTransportError;
            _transport.DataReceived -= OnDataReceived;
            await _transport.DisposeAsync();
        }

        _transport = _transportFactory(_endpoint!);
        _transport.TransportError += OnTransportError;
        _transport.DataReceived += OnDataReceived;

        await _transport.OpenAsync(_endpoint!, cancellationToken);
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        _stateMachine.Transition(ConnectionState.Handshaking);

        _helloTcs = new TaskCompletionSource<DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frame = new HostLinkFrame(HostLinkFrameType.Hello, _requests.NextSeq(), HostLinkSerializer.BuildHelloPayload());
        await WriteAsync(HostLinkCodec.Encode(frame), cancellationToken);

        DeviceInfo = await _helloTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        DeviceInfoReceived?.Invoke(this, DeviceInfo);
        await SyncDeviceClockIfSupportedAsync(cancellationToken);
        _stateMachine.Transition(ConnectionState.Ready);
    }

    private async Task SyncDeviceClockIfSupportedAsync(CancellationToken cancellationToken)
    {
        var info = DeviceInfo;
        if (info?.Capabilities.SupportsSetTime != true)
            return;

        var now = DateTimeOffset.UtcNow;
        var payload = HostLinkSerializer.BuildCmdSetTimePayload(now);

        try
        {
            var ack = await SendCommandWithAckAsync(HostLinkFrameType.CmdSetTime, payload, cancellationToken);
            if (ack == HostLinkErrorCode.Ok)
            {
                _logger.LogInformation(
                    "Device clock synchronized to {UtcTime:O} via CMD_SET_TIME",
                    now);
            }
            else
            {
                _logger.LogWarning(
                    "Device clock sync rejected by device: ACK={AckCode}",
                    ack);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Device clock sync failed");
        }
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (_transport is null)
            throw new InvalidOperationException("Transport not ready");
        _sessionStore.AddRawFrame(new RawFrameRecord(
            DateTimeOffset.UtcNow,
            RawFrameDirection.Tx,
            RawFrameStatus.Ok,
            bytes.ToArray(),
            null));
        await _transport.WriteAsync(bytes, cancellationToken);
    }

    private void OnDataReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        _codec.Append(data.Span);
        foreach (var frame in _codec.DrainFrames())
        {
            HandleFrame(frame);
        }
    }

    private void HandleFrame(HostLinkFrame frame)
    {
        Debug.WriteLine($"[HostLink] RX frame type={frame.Type} seq={frame.Seq} len={frame.Payload.Length}");
        switch (frame.Type)
        {
            case HostLinkFrameType.HelloAck:
                var info = HostLinkSerializer.ParseHelloAck(frame.Payload.Span);
                DeviceInfo = info;
                _helloTcs?.TrySetResult(info);
                break;
            case HostLinkFrameType.Ack:
                var code = HostLinkSerializer.ParseAck(frame.Payload.Span);
                _requests.HandleAck(frame.Seq, code);
                if (_requests.TryGet(frame.Seq, out var pending) && pending.CommandType == HostLinkFrameType.CmdTxMsg)
                {
                    UpdateMessageAcked(frame.Seq, code);
                }
                break;
            case HostLinkFrameType.EvRxMsg:
                HandleRxMessage(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvTxResult:
                HandleTxResult(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvStatus:
                HandleStatus(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvLog:
                HandleLog(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvGps:
                HandleGps(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvAppData:
                HandleAppData(frame.Payload.Span);
                break;
            case HostLinkFrameType.EvTeamState:
                HandleTeamState(frame.Payload.Span);
                break;
            default:
                break;
        }
    }

    private void UpdateMessageAcked(ushort seq, HostLinkErrorCode code)
    {
        lock (_gate)
        {
            if (_pendingMessages.TryGetValue(seq, out var message))
            {
                if (code == HostLinkErrorCode.Ok)
                {
                    if (IsBroadcastDestination(message.ToId))
                    {
                        // Broadcast has no deterministic TX result on many stacks, treat ACK as completion.
                        message.Status = MessageDeliveryStatus.Succeeded;
                        message.ErrorMessage = null;
                        _pendingMessages.Remove(seq);
                        _requests.Complete(seq);
                    }
                    else
                    {
                        message.Status = MessageDeliveryStatus.Acked;
                        _awaitingTxResults.Enqueue(message);
                    }
                }
                else
                {
                    message.Status = MessageDeliveryStatus.Failed;
                    message.ErrorMessage = $"ACK {code}";
                    _pendingMessages.Remove(seq);
                    _requests.Complete(seq);
                    _logStore.Add(new HostLinkLogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Message = $"ACK failed seq={seq} code={code}",
                        RawCode = code.ToString(),
                    });
                }
                _sessionStore.UpdateMessage(message);
                MessageUpdated?.Invoke(this, message);
            }
        }
    }

    private void UpdateMessageResult(TxResult result)
    {
        lock (_gate)
        {
            if (_awaitingTxResults.Count == 0)
                return;
            var message = _awaitingTxResults.Dequeue();
            message.MessageId = result.MessageId;
            message.Status = result.Success ? MessageDeliveryStatus.Succeeded : MessageDeliveryStatus.Failed;
            message.ErrorMessage = result.Success ? null : $"{result.ErrorCode}: {result.Reason}";
            _sessionStore.UpdateMessage(message);
            MessageUpdated?.Invoke(this, message);
            if (message.Seq != 0)
            {
                _pendingMessages.Remove(message.Seq);
                _requests.Complete(message.Seq);
            }
        }
    }

    private void HandleRxMessage(ReadOnlySpan<byte> payload)
    {
        var rx = HostLinkSerializer.ParseRxMessage(payload);
        var hasGps = TrailMateCenter.Helpers.GpsParser.TryExtract(rx.Text, out var lat, out var lon);
        var isBroadcast = rx.To == 0 || rx.To == uint.MaxValue;

        var receivedAt = DateTimeOffset.UtcNow;
        var deviceTimestamp = rx.RxMeta?.TimestampUtc ?? (rx.Timestamp == DateTimeOffset.UnixEpoch ? (DateTimeOffset?)null : rx.Timestamp);

        var message = new MessageEntry
        {
            Direction = MessageDirection.Incoming,
            MessageId = rx.MessageId,
            FromId = rx.From,
            ToId = isBroadcast ? null : rx.To,
            From = $"0x{rx.From:X8}",
            To = isBroadcast ? "broadcast" : $"0x{rx.To:X8}",
            ChannelId = rx.Channel,
            Channel = rx.Channel.ToString(),
            Text = rx.Text,
            Status = MessageDeliveryStatus.Succeeded,
            Timestamp = receivedAt,
            DeviceTimestamp = deviceTimestamp,
            Rssi = rx.RxMeta?.RssiDbm,
            Snr = rx.RxMeta?.SnrDb,
            Hop = rx.RxMeta?.HopCount,
            Direct = rx.RxMeta?.Direct,
            Origin = rx.RxMeta?.Origin,
            FromIs = rx.RxMeta?.FromIs,
            Latitude = hasGps ? lat : null,
            Longitude = hasGps ? lon : null,
        };
        _sessionStore.AddMessage(message);
        MessageAdded?.Invoke(this, message);
        _sessionStore.AddEvent(rx);
        EventReceived?.Invoke(this, rx);
    }

    private void HandleTxResult(ReadOnlySpan<byte> payload)
    {
        var tx = HostLinkSerializer.ParseTxResult(payload);
        var result = new TxResult
        {
            MessageId = tx.MessageId,
            Success = tx.Success,
            ErrorCode = tx.ErrorCode,
            Reason = tx.Reason,
            Timestamp = tx.Timestamp,
        };
        if (_awaitingTxResults.Count == 0)
        {
            _logStore.Add(new HostLinkLogEntry
            {
                Timestamp = result.Timestamp,
                Message = $"Unmatched TX result msg={result.MessageId}",
                RawCode = result.ErrorCode.ToString(),
            });
            return;
        }
        UpdateMessageResult(result);
        if (!result.Success)
        {
            _logStore.Add(new HostLinkLogEntry
            {
                Timestamp = result.Timestamp,
                Message = $"TX failed msg={result.MessageId} code={result.ErrorCode} reason={result.Reason}",
                RawCode = result.ErrorCode.ToString(),
            });
        }
        _sessionStore.AddEvent(tx);
        EventReceived?.Invoke(this, tx);
    }

    private void HandleStatus(ReadOnlySpan<byte> payload)
    {
        var statusEvent = HostLinkSerializer.ParseStatus(payload);
        Status = new StatusInfo
        {
            BatteryPercent = statusEvent.BatteryPercent,
            IsCharging = statusEvent.IsCharging,
            LinkState = statusEvent.LinkState,
            MeshProtocol = statusEvent.MeshProtocol,
            Region = statusEvent.Region,
            Channel = statusEvent.Channel,
            DutyCycleEnabled = statusEvent.DutyCycleEnabled,
            ChannelUtil = statusEvent.ChannelUtil,
            LastError = statusEvent.LastError,
            Timestamp = statusEvent.Timestamp,
        };
        StatusUpdated?.Invoke(this, Status);
        _sessionStore.AddEvent(statusEvent);
        EventReceived?.Invoke(this, statusEvent);

        var config = HostLinkSerializer.ParseConfigFromStatus(payload);
        _configTcs?.TrySetResult(config);
        _sessionStore.AddEvent(new ConfigEvent(statusEvent.Timestamp, config));
    }

    private void HandleLog(ReadOnlySpan<byte> payload)
    {
        var log = HostLinkSerializer.ParseLog(payload);
        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = log.Timestamp,
            Level = log.Level,
            Message = log.Message,
            RawCode = log.RawCode,
        });
        _sessionStore.AddEvent(log);
        EventReceived?.Invoke(this, log);
    }

    private void HandleGps(ReadOnlySpan<byte> payload)
    {
        var gps = HostLinkSerializer.ParseGps(payload);
        _sessionStore.AddEvent(gps);
        EventReceived?.Invoke(this, gps);
        if (gps.HasFix && gps.Latitude.HasValue && gps.Longitude.HasValue)
        {
            GpsUpdated?.Invoke(this, gps);
        }
    }

    private void HandleAppData(ReadOnlySpan<byte> payload)
    {
        var app = HostLinkSerializer.ParseAppData(payload);
        _sessionStore.AddEvent(app);
        EventReceived?.Invoke(this, app);
        Debug.WriteLine($"[HostLink] EV_APP_DATA port={app.Portnum} from=0x{app.From:X8} to=0x{app.To:X8} ch={app.Channel} flags={app.Flags} totalLen={app.TotalLength} offset={app.Offset} chunkLen={app.ChunkLength}");
        _logger.LogInformation(
            "EV_APP_DATA: port={Port} from=0x{From:X8} to=0x{To:X8} ch={Channel} flags={Flags} totalLen={TotalLen} offset={Offset} chunkLen={ChunkLen}",
            app.Portnum, app.From, app.To, app.Channel, app.Flags, app.TotalLength, app.Offset, app.ChunkLength);
        if (app.Portnum == AppDataDecoder.NodeInfoPort)
        {
            Debug.WriteLine($"[HostLink] EV_APP_DATA NodeInfo from=0x{app.From:X8} totalLen={app.TotalLength} offset={app.Offset} chunkLen={app.ChunkLength}");
            _logger.LogInformation(
                "EV_APP_DATA NodeInfo: from=0x{From:X8} to=0x{To:X8} ch={Channel} flags={Flags} totalLen={TotalLen} offset={Offset} chunkLen={ChunkLen}",
                app.From, app.To, app.Channel, app.Flags, app.TotalLength, app.Offset, app.ChunkLength);
        }
        CacheTeamContext(app);
        var packets = _appDataReassembler.Accept(app);
        foreach (var packet in packets)
        {
            if (packet.Portnum == AppDataDecoder.NodeInfoPort && packet.Payload.Length == 0)
            {
                Debug.WriteLine($"[HostLink] NodeInfo payload empty after reassembly: from=0x{app.From:X8} totalLen={app.TotalLength} chunkLen={app.ChunkLength}");
                _logger.LogWarning(
                    "NodeInfo payload is empty after reassembly: from=0x{From:X8} totalLen={TotalLen} chunkLen={ChunkLen}",
                    app.From, app.TotalLength, app.ChunkLength);
            }
            var decoded = _appDataDecoder.Decode(packet);
            if (packet.Portnum == AppDataDecoder.NodeInfoPort)
            {
                if (decoded.NodeInfos.Count == 0)
                {
                    Debug.WriteLine($"[HostLink] NodeInfo decode produced no records: from=0x{packet.From:X8} payloadLen={packet.Payload.Length}");
                    _logger.LogWarning(
                        "NodeInfo decode produced no records: from=0x{From:X8} payloadLen={PayloadLen}",
                        packet.From, packet.Payload.Length);
                }
                else
                {
                    foreach (var info in decoded.NodeInfos)
                    {
                        Debug.WriteLine($"[HostLink] NodeInfo decoded id=0x{info.NodeId:X8} short={info.ShortName} long={info.LongName} userId={info.UserId}");
                        _logger.LogInformation(
                            "NodeInfo decoded: id=0x{Id:X8} short={Short} long={Long} userId={UserId} lat={Lat} lon={Lon}",
                            info.NodeId, info.ShortName, info.LongName, info.UserId, info.Latitude, info.Longitude);
                    }
                }
            }
            foreach (var msg in decoded.Messages)
            {
                _sessionStore.AddMessage(msg);
                MessageAdded?.Invoke(this, msg);
            }
            foreach (var pos in decoded.Positions)
            {
                _sessionStore.AddPositionUpdate(pos);
                PositionUpdated?.Invoke(this, pos);
            }
            foreach (var info in decoded.NodeInfos)
            {
                _sessionStore.AddOrUpdateNodeInfo(info);
                NodeInfoUpdated?.Invoke(this, info);
            }
            foreach (var te in decoded.TacticalEvents)
            {
                _sessionStore.AddTacticalEvent(te);
                TacticalEventReceived?.Invoke(this, te);
            }
        }
    }

    private void HandleTeamState(ReadOnlySpan<byte> payload)
    {
        var teamState = HostLinkSerializer.ParseTeamState(payload);
        _logger.LogInformation(
            "EV_TEAM_STATE decoded: flags=0x{Flags:X2}, keyId={KeyId}, lastEventSeq={LastEventSeq}, members={Members}, teamName={TeamName}",
            teamState.Flags,
            teamState.KeyId,
            teamState.LastEventSeq,
            teamState.Members.Count,
            teamState.TeamName);
        CacheTeamContext(teamState);
        _sessionStore.AddEvent(teamState);
        EventReceived?.Invoke(this, teamState);
        _sessionStore.SetTeamState(teamState);
        TeamStateUpdated?.Invoke(this, teamState);
    }

    private void CacheTeamContext(AppDataEvent app)
    {
        if (!app.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata))
            return;
        if (app.TeamId.Length != 8 || app.TeamId.All(b => b == 0))
            return;

        var changed = false;
        lock (_gate)
        {
            if (!_hasTeamContext ||
                !_teamId.AsSpan().SequenceEqual(app.TeamId) ||
                _teamKeyId != app.TeamKeyId ||
                _teamChannel != app.Channel)
            {
                app.TeamId.CopyTo(_teamId, 0);
                _teamKeyId = app.TeamKeyId;
                _teamChannel = app.Channel;
                _hasTeamContext = true;
                changed = true;
            }
        }

        if (changed)
        {
            TeamContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CacheTeamContext(TeamStateEvent teamState)
    {
        var hasTeamId = teamState.TeamId.Length == 8 && teamState.TeamId.Any(b => b != 0);
        var inTeam =
            (teamState.Flags & TeamStateFlagInTeam) != 0 ||
            (teamState.Flags & TeamStateFlagPendingJoin) != 0 ||
            (teamState.Flags & TeamStateFlagHasTeamId) != 0 ||
            hasTeamId ||
            teamState.KeyId != 0;

        var changed = false;
        lock (_gate)
        {
            if (!inTeam)
            {
                if (_hasTeamContext || _teamKeyId != 0 || _teamChannel != 0 || _teamSelfId != 0 || _teamId.Any(b => b != 0))
                {
                    Array.Clear(_teamId, 0, _teamId.Length);
                    _teamKeyId = 0;
                    _teamChannel = 0;
                    _hasTeamContext = false;
                    _teamSelfId = 0;
                    changed = true;
                }
            }
            else
            {
                if (teamState.SelfId != 0 && _teamSelfId != teamState.SelfId)
                {
                    _teamSelfId = teamState.SelfId;
                    changed = true;
                }

                if (hasTeamId && !_teamId.AsSpan().SequenceEqual(teamState.TeamId))
                {
                    teamState.TeamId.CopyTo(_teamId, 0);
                    changed = true;
                }

                if (_teamKeyId != teamState.KeyId)
                {
                    _teamKeyId = teamState.KeyId;
                    changed = true;
                }

                if (!_hasTeamContext && (hasTeamId || teamState.KeyId != 0))
                {
                    _hasTeamContext = true;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            TeamContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetTeamContext(bool notify)
    {
        var changed = false;
        lock (_gate)
        {
            if (_hasTeamContext || _teamKeyId != 0 || _teamChannel != 0 || _teamSelfId != 0 || _teamId.Any(b => b != 0))
            {
                Array.Clear(_teamId, 0, _teamId.Length);
                _teamKeyId = 0;
                _teamChannel = 0;
                _hasTeamContext = false;
                _teamSelfId = 0;
                changed = true;
            }
        }

        if (notify && changed)
        {
            TeamContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDecodeError(object? sender, HostLinkDecodeError error)
    {
        ProtocolError?.Invoke(this, error);
        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = $"Protocol error: {error.Code} {error.Message}",
        });
    }

    private void OnRawFrameDecoded(object? sender, HostLinkRawFrameData data)
    {
        _sessionStore.AddRawFrame(new RawFrameRecord(
            DateTimeOffset.UtcNow,
            RawFrameDirection.Rx,
            data.Status,
            data.Frame.ToArray(),
            data.Note));
    }

    private void OnTransportError(object? sender, TransportError error)
    {
        TransportError?.Invoke(this, error);
        _logger.LogWarning("Transport error: {Message}", error.Message);
        _stateMachine.Transition(ConnectionState.Error, error.Message);
        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = $"Transport error: {error.Type} {error.Message}",
        });

        if (_options.AutoReconnect && _endpoint is not null)
        {
            _reconnectTask ??= Task.Run(() => ReconnectLoopAsync(_cts?.Token ?? CancellationToken.None));
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        _stateMachine.Transition(ConnectionState.Reconnecting);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await OpenTransportAsync(cancellationToken);
                await HandshakeAsync(cancellationToken);
                _stateMachine.Transition(ConnectionState.Ready);
                _reconnectTask = null;
                return;
            }
            catch (Exception ex)
            {
                _stateMachine.Transition(ConnectionState.Error, ex.Message);
                await Task.Delay(_options.ReconnectDelay, cancellationToken);
            }
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pending in _requests.GetTimedOut(now).ToList())
            {
                if (pending.Retries < pending.MaxRetries)
                {
                    pending.Retries++;
                    pending.LastSendAt = now;
                    try
                    {
                        await WriteAsync(pending.FrameBytes, cancellationToken);
                        _logger.LogDebug("Resent seq {Seq} retry {Retry}", pending.Seq, pending.Retries);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Resend failed seq {Seq}", pending.Seq);
                        HandleTimeout(pending);
                    }
                }
                else
                {
                    HandleTimeout(pending);
                }
            }
        }
    }

    private void HandleTimeout(PendingRequest pending)
    {
        _requests.Complete(pending.Seq);
        pending.Acked.TrySetResult(HostLinkErrorCode.Internal);
        lock (_gate)
        {
            if (_pendingMessages.TryGetValue(pending.Seq, out var message))
            {
                message.Status = MessageDeliveryStatus.Timeout;
                message.ErrorMessage = "ACK timeout";
                _sessionStore.UpdateMessage(message);
                MessageUpdated?.Invoke(this, message);
                _pendingMessages.Remove(pending.Seq);
            }
        }

        _logStore.Add(new HostLinkLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = $"ACK timeout seq={pending.Seq}",
        });
    }

    private void EnsureReady()
    {
        if (_stateMachine.State != ConnectionState.Ready)
            throw new InvalidOperationException("Not connected");
    }

    private static bool IsBroadcastDestination(uint? toId)
    {
        return !toId.HasValue || toId.Value == 0 || toId.Value == uint.MaxValue;
    }

    private (bool HasTeamContext, byte[] TeamId, uint TeamKeyId, byte Channel, uint FromId) ResolveOutboundTeamContext(
        byte requestedChannel,
        string? teamConversationKey)
    {
        var channel = requestedChannel;
        var hasContext = false;
        var teamId = new byte[8];
        var teamKeyId = 0u;
        var fromId = 0u;

        lock (_gate)
        {
            hasContext = _hasTeamContext;
            fromId = _teamSelfId;
            if (hasContext)
            {
                _teamId.CopyTo(teamId, 0);
                teamKeyId = _teamKeyId;
                if (channel == 0 && _teamChannel != 0)
                {
                    channel = _teamChannel;
                }
            }
        }

        if (!hasContext &&
            TryParseTeamConversationKey(teamConversationKey, out var parsedTeamId, out var parsedTeamKeyId))
        {
            hasContext = true;
            teamId = parsedTeamId;
            teamKeyId = parsedTeamKeyId;
        }

        return (hasContext, teamId, teamKeyId, channel, fromId);
    }

    private static bool TryParseTeamConversationKey(string? key, out byte[] teamId, out uint teamKeyId)
    {
        teamId = new byte[8];
        teamKeyId = 0;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        var trimmed = key.Trim();
        if (string.Equals(trimmed, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return false;

        if (trimmed.StartsWith("KEY:", StringComparison.OrdinalIgnoreCase))
        {
            var keyHex = trimmed[4..];
            if (uint.TryParse(keyHex, System.Globalization.NumberStyles.HexNumber, null, out var parsedKey))
            {
                teamKeyId = parsedKey;
                return true;
            }

            return false;
        }

        var splitIndex = trimmed.IndexOf(':');
        if (splitIndex <= 0 || splitIndex >= trimmed.Length - 1)
            return false;

        var teamHex = trimmed[..splitIndex];
        var keyPart = trimmed[(splitIndex + 1)..];
        if (teamHex.Length != 16)
            return false;
        if (!uint.TryParse(keyPart, System.Globalization.NumberStyles.HexNumber, null, out var parsedTeamKeyId))
            return false;

        try
        {
            var parsedTeamId = Convert.FromHexString(teamHex);
            if (parsedTeamId.Length != 8)
                return false;
            parsedTeamId.CopyTo(teamId, 0);
            teamKeyId = parsedTeamKeyId;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildTeamConversationKeyForOutbound(
        bool hasTeamContext,
        byte[] teamId,
        uint teamKeyId,
        string? fallbackKey)
    {
        if (hasTeamContext)
        {
            if (teamId.Length == 8 && teamId.Any(b => b != 0))
            {
                return $"{Convert.ToHexString(teamId)}:{teamKeyId:X8}";
            }

            if (teamKeyId != 0)
            {
                return $"KEY:{teamKeyId:X8}";
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackKey))
            return fallbackKey.Trim();

        return "DEFAULT";
    }

    private IHostLinkTransport CreateTransport(TransportEndpoint endpoint)
    {
        return endpoint switch
        {
            SerialEndpoint => new SerialTransport(_logger),
            ReplayEndpoint => new ReplayTransport(_logger),
            _ => throw new NotSupportedException("Unknown endpoint"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync();
    }
}
