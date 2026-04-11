using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace TrailMateCenter.Services;

public sealed class UnityProcessPropagationBridge : IPropagationUnityBridge, IDisposable
{
    private readonly ILogger<UnityProcessPropagationBridge> _logger;
    private readonly UnityBridgeOptions _options;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PropagationUnityBridgeAck>> _pendingAcks = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly CancellationTokenSource _lifecycleCts = new();
    private readonly Task _heartbeatTask;

    private NamedPipeClientStream? _pipe;
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private Task? _reconnectTask;
    private string _viewportId = "unbound";
    private volatile bool _isTransportConnected;
    private volatile bool _isDisposed;
    private volatile bool _shouldMaintainConnection;

    public UnityProcessPropagationBridge(ILogger<UnityProcessPropagationBridge> logger)
    {
        _logger = logger;
        _options = UnityBridgeOptions.FromEnvironment();
        _logger.LogInformation(
            "Unity bridge configured. Transport={Transport}, Pipe={PipeName}, Tcp={Host}:{Port}",
            _options.TransportMode,
            _options.PipeName,
            _options.TcpHost,
            _options.TcpPort);

        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_lifecycleCts.Token), CancellationToken.None);
    }

    public bool IsAttached { get; private set; }

    public event EventHandler<PropagationUnityBridgeStateChangedEventArgs>? BridgeStateChanged;
    public event EventHandler<PropagationUnityBridgeTelemetryEventArgs>? TelemetryUpdated;
    public event EventHandler<PropagationUnityLayerStateChangedEventArgs>? LayerStateChanged;
    public event EventHandler<PropagationUnityDiagnosticSnapshotEventArgs>? DiagnosticSnapshotReceived;
    public event EventHandler<PropagationUnityCameraStateChangedEventArgs>? CameraStateChanged;
    public event EventHandler<PropagationUnityMapPointSelectedEventArgs>? MapPointSelected;
    public event EventHandler<PropagationUnityProfileLineChangedEventArgs>? ProfileLineChanged;

    public async Task AttachViewportAsync(string viewportId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _shouldMaintainConnection = true;

        _viewportId = string.IsNullOrWhiteSpace(viewportId) ? "propagation-main-slot" : viewportId.Trim();
        if (!await EnsureConnectedAsync(cancellationToken))
            return;

        var ack = await SendCommandAsync(
            commandType: "attach_viewport",
            runId: string.Empty,
            payload: new { viewport_id = _viewportId },
            cancellationToken);

        if (!ack.Acknowledged)
        {
            RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
            {
                EventType = "attach_timeout",
                IsConnected = _isTransportConnected,
                IsAttached = IsAttached,
                Message = $"Unity bridge attach_viewport ack timed out after {_options.AckTimeoutMs} ms.",
                TimestampUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        if (!IsAttached)
        {
            IsAttached = true;
            RaiseBridgeStateChanged(true, _viewportId, ack.Detail);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        _shouldMaintainConnection = false;
        HandleTransportDisconnected("Disconnected by desktop lifecycle.", scheduleReconnect: false);
        return Task.CompletedTask;
    }

    public async Task<PropagationUnityBridgeAck> SetActiveLayerAsync(
        string layerId,
        string runId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("Layer id is required.", nameof(layerId));

        return await SetLayerPresentationAsync(
            new PropagationUnityLayerPresentation
            {
                LayerIds = [layerId.Trim()],
            },
            runId,
            cancellationToken);
    }

    public async Task<PropagationUnityBridgeAck> SetLayerPresentationAsync(
        PropagationUnityLayerPresentation presentation,
        string runId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _shouldMaintainConnection = true;

        if (presentation == null)
            throw new ArgumentNullException(nameof(presentation));

        var layerIds = presentation.LayerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        if (layerIds.Length == 0)
            throw new ArgumentException("At least one layer id is required.", nameof(presentation));

        if (string.IsNullOrWhiteSpace(_viewportId) || string.Equals(_viewportId, "unbound", StringComparison.Ordinal))
        {
            _viewportId = "propagation-main-slot";
        }

        if (!await EnsureConnectedAsync(cancellationToken))
            throw new InvalidOperationException("Unity bridge transport is not connected.");

        if (!IsAttached)
        {
            await AttachViewportAsync(_viewportId, cancellationToken);
            if (!IsAttached)
                throw new InvalidOperationException("Unity bridge viewport is not attached.");
        }

        var visibility = (presentation.LayerVisibility ?? new Dictionary<string, bool>(StringComparer.Ordinal))
            .Where(static kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(static kv => kv.Key.Trim(), static kv => kv.Value, StringComparer.Ordinal);

        var opacity = (presentation.LayerOpacity ?? new Dictionary<string, double>(StringComparer.Ordinal))
            .Where(static kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(static kv => kv.Key.Trim(), static kv => Math.Clamp(kv.Value, 0d, 1d), StringComparer.Ordinal);

        var order = (presentation.LayerOrder ?? Array.Empty<string>())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return await SendCommandAsync(
            commandType: "set_active_layer",
            runId: runId,
            payload: new
            {
                layer_id = layerIds[0],
                layer_ids = layerIds,
                layer_visibility = visibility,
                layer_opacity = opacity,
                layer_order = order,
                run_id = runId
            },
            cancellationToken);
    }

    public async Task<PropagationUnityBridgeAck> SetCameraStateAsync(
        PropagationUnityCameraState cameraState,
        string runId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _shouldMaintainConnection = true;

        if (string.IsNullOrWhiteSpace(_viewportId) || string.Equals(_viewportId, "unbound", StringComparison.Ordinal))
        {
            _viewportId = "propagation-main-slot";
        }

        if (!await EnsureConnectedAsync(cancellationToken))
            throw new InvalidOperationException("Unity bridge transport is not connected.");

        if (!IsAttached)
        {
            await AttachViewportAsync(_viewportId, cancellationToken);
            if (!IsAttached)
                throw new InvalidOperationException("Unity bridge viewport is not attached.");
        }

        return await SendCommandAsync(
            commandType: "set_camera_state",
            runId: runId,
            payload: new
            {
                x = cameraState.X,
                y = cameraState.Y,
                z = cameraState.Z,
                pitch = cameraState.Pitch,
                yaw = cameraState.Yaw,
                roll = cameraState.Roll,
                fov = cameraState.Fov,
            },
            cancellationToken);
    }

    public async Task<PropagationUnityBridgeAck> PushSimulationRequestAsync(
        string runId,
        PropagationSimulationRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _shouldMaintainConnection = true;

        if (!await EnsureConnectedAsync(cancellationToken))
            throw new InvalidOperationException("Unity bridge transport is not connected.");

        if (!IsAttached)
        {
            await AttachViewportAsync(_viewportId, cancellationToken);
            if (!IsAttached)
                throw new InvalidOperationException("Unity bridge viewport is not attached.");
        }

        return await SendCommandAsync(
            commandType: "push_request",
            runId: runId,
            payload: request,
            cancellationToken);
    }

    public async Task<PropagationUnityBridgeAck> PushSimulationResultAsync(
        PropagationSimulationResult result,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _shouldMaintainConnection = true;

        if (!await EnsureConnectedAsync(cancellationToken))
            throw new InvalidOperationException("Unity bridge transport is not connected.");

        if (!IsAttached)
        {
            await AttachViewportAsync(_viewportId, cancellationToken);
            if (!IsAttached)
                throw new InvalidOperationException("Unity bridge viewport is not attached.");
        }

        return await SendCommandAsync(
            commandType: "push_result",
            runId: result.RunMeta.RunId,
            payload: result,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        try
        {
            _lifecycleCts.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _heartbeatTask.GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }

        try
        {
            _reconnectTask?.GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }

        SafeCloseTransport();

        _connectLock.Dispose();
        _sendLock.Dispose();
        _reconnectLock.Dispose();
        _lifecycleCts.Dispose();
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_writer is not null && _reader is not null)
            return true;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_writer is not null && _reader is not null)
                return true;

            SafeCloseTransport();
            _listenCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);

            var connectResult = _options.TransportMode == UnityBridgeTransportMode.NamedPipe
                ? await ConnectNamedPipeAsync(cancellationToken)
                : await ConnectTcpAsync(cancellationToken);

            if (!connectResult.Succeeded)
            {
                SafeCloseTransport();
                _isTransportConnected = false;
                RaiseBridgeStateChanged(false, _viewportId, "Transport connect failed");
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = "connect_failed",
                    IsConnected = false,
                    IsAttached = IsAttached,
                    Message = connectResult.Message,
                    TimestampUtc = DateTimeOffset.UtcNow,
                });
                return false;
            }

            _isTransportConnected = true;
            _listenTask = Task.Run(() => ListenLoopAsync(_listenCts.Token), CancellationToken.None);
            RaiseBridgeStateChanged(IsAttached, _viewportId, "Transport connected");
            RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
            {
                EventType = "connected",
                IsConnected = true,
                IsAttached = IsAttached,
                Message = connectResult.Message,
                TimestampUtc = DateTimeOffset.UtcNow,
            });
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SafeCloseTransport();
            _isTransportConnected = false;
            _logger.LogInformation(ex, "Unity bridge transport connect failed.");
            RaiseBridgeStateChanged(false, _viewportId, "Transport connect failed");
            RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
            {
                EventType = "connect_failed",
                IsConnected = false,
                IsAttached = IsAttached,
                Message = ex.Message,
                TimestampUtc = DateTimeOffset.UtcNow,
            });
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<TransportConnectResult> ConnectNamedPipeAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _options.PipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        cancellationToken.ThrowIfCancellationRequested();
        var fullPipeName = $@"\.\pipe\{_options.PipeName}";
        if (OperatingSystem.IsWindows() && !WaitNamedPipe(fullPipeName, (uint)Math.Max(0, _options.ConnectTimeoutMs)))
        {
            try { pipe.Dispose(); } catch { /* ignore */ }
            return new TransportConnectResult(
                Succeeded: false,
                Message: $"Unity named pipe '{_options.PipeName}' was not available within {_options.ConnectTimeoutMs} ms.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ConnectTimeoutMs);

        try
        {
            await pipe.ConnectAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            try { pipe.Dispose(); } catch { /* ignore */ }
            return new TransportConnectResult(
                Succeeded: false,
                Message: $"Unity named pipe '{_options.PipeName}' was not available within {_options.ConnectTimeoutMs} ms.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            try { pipe.Dispose(); } catch { /* ignore */ }
            throw new OperationCanceledException(cancellationToken);
        }

        BindTransport(pipe);
        _pipe = pipe;
        _logger.LogInformation("Unity bridge connected via named pipe {PipeName}", _options.PipeName);
        return new TransportConnectResult(
            Succeeded: true,
            Message: $"Unity bridge connected via named pipe {_options.PipeName}.");
    }

    private async Task<TransportConnectResult> ConnectTcpAsync(CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        var connectTask = tcpClient.ConnectAsync(_options.TcpHost, _options.TcpPort, cancellationToken).AsTask();
        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(_options.ConnectTimeoutMs), cancellationToken);
        var completed = await Task.WhenAny(connectTask, timeoutTask);

        if (completed != connectTask)
        {
            try { tcpClient.Dispose(); } catch { /* ignore */ }
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            return new TransportConnectResult(
                Succeeded: false,
                Message: $"Unity bridge connect timed out after {_options.ConnectTimeoutMs} ms.");
        }

        try
        {
            await connectTask;
        }
        catch
        {
            try { tcpClient.Dispose(); } catch { /* ignore */ }
            throw;
        }

        var stream = tcpClient.GetStream();

        BindTransport(stream);
        _tcpClient = tcpClient;
        _logger.LogInformation("Unity bridge connected via tcp {Host}:{Port}", _options.TcpHost, _options.TcpPort);
        return new TransportConnectResult(
            Succeeded: true,
            Message: $"Unity bridge connected via tcp {_options.TcpHost}:{_options.TcpPort}.");
    }

    private void BindTransport(Stream stream)
    {
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
    }

    private async Task<PropagationUnityBridgeAck> SendCommandAsync(
        string commandType,
        string runId,
        object payload,
        CancellationToken cancellationToken)
    {
        if (_writer is null)
            throw new InvalidOperationException("Unity bridge transport is not connected.");

        var correlationId = Guid.NewGuid().ToString("N");
        var envelope = new OutboundEnvelope
        {
            Type = commandType,
            CorrelationId = correlationId,
            RunId = runId ?? string.Empty,
            TimestampUtc = DateTimeOffset.UtcNow,
            Payload = payload,
        };
        var json = JsonSerializer.Serialize(envelope, _jsonOptions);

        var tcs = new TaskCompletionSource<PropagationUnityBridgeAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks[correlationId] = tcs;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(json);
        }
        catch
        {
            _pendingAcks.TryRemove(correlationId, out _);
            HandleTransportDisconnected("Write failed", scheduleReconnect: true);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }

        var completed = await Task.WhenAny(
            tcs.Task,
            Task.Delay(TimeSpan.FromMilliseconds(_options.AckTimeoutMs), cancellationToken));

        if (completed == tcs.Task)
            return await tcs.Task;

        _pendingAcks.TryRemove(correlationId, out _);
        return new PropagationUnityBridgeAck
        {
            Action = commandType,
            RunId = runId ?? string.Empty,
            CorrelationId = correlationId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Detail = "sent (ack timeout)",
            Acknowledged = false,
        };
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_reader is null)
                    break;

                string? line;
                try
                {
                    line = await _reader.ReadLineAsync().WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (line is null)
                        break;
                    continue;
                }

                HandleInboundMessage(line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unity bridge listener stopped due to exception.");
        }
        finally
        {
            HandleTransportDisconnected("Transport disconnected", scheduleReconnect: true);
        }
    }

    private void HandleInboundMessage(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = ReadString(root, "type");

            if (string.Equals(type, "ack", StringComparison.OrdinalIgnoreCase))
            {
                var ack = ParseAck(root);
                if (!string.IsNullOrWhiteSpace(ack.CorrelationId) &&
                    _pendingAcks.TryRemove(ack.CorrelationId, out var pending))
                {
                    pending.TrySetResult(ack);
                }
                return;
            }

            if (string.Equals(type, "map_point_selected", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                RaiseMapPointSelected(new PropagationUnityMapPointSelectedEventArgs
                {
                    X = ReadDouble(payload, "x"),
                    Y = ReadDouble(payload, "y"),
                    NodeId = ReadString(payload, "node_id"),
                });
                return;
            }

            if (string.Equals(type, "profile_line_changed", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                RaiseProfileLineChanged(new PropagationUnityProfileLineChangedEventArgs
                {
                    StartX = ReadDouble(payload, "start_x"),
                    StartY = ReadDouble(payload, "start_y"),
                    EndX = ReadDouble(payload, "end_x"),
                    EndY = ReadDouble(payload, "end_y"),
                });
                return;
            }

            if (string.Equals(type, "layer_state_changed", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                var progress = TryReadDouble(payload, "progress", out var progressValue)
                    ? progressValue
                    : TryReadDouble(payload, "progress_pct", out var progressPct)
                        ? progressPct
                        : (double?)null;
                var transitionMs = TryReadDouble(payload, "transition_ms", out var transitionValue)
                    ? transitionValue
                    : (double?)null;

                RaiseLayerStateChanged(new PropagationUnityLayerStateChangedEventArgs
                {
                    LayerId = ReadString(payload, "layer_id"),
                    RunId = ReadString(payload, "run_id"),
                    State = ReadString(payload, "state"),
                    ProgressPercent = progress,
                    TransitionMs = transitionMs,
                    Message = ReadString(payload, "message"),
                    TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
                });
                return;
            }

            if (string.Equals(type, "diagnostic_snapshot", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                RaiseDiagnosticSnapshot(new PropagationUnityDiagnosticSnapshotEventArgs
                {
                    Fps = ReadDouble(payload, "fps"),
                    FrameTimeP95Ms = ReadDouble(payload, "frame_time_p95_ms"),
                    GpuMemoryMb = ReadDouble(payload, "gpu_memory_mb"),
                    LayerLoadMs = ReadDouble(payload, "layer_load_ms"),
                    TileCacheHitRate = ReadDouble(payload, "tile_cache_hit_rate"),
                    Message = ReadString(payload, "message"),
                    TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
                });
                return;
            }

            if (string.Equals(type, "camera_state_changed", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                RaiseCameraStateChanged(new PropagationUnityCameraStateChangedEventArgs
                {
                    CameraState = new PropagationUnityCameraState
                    {
                        X = ReadDouble(payload, "x"),
                        Y = ReadDouble(payload, "y"),
                        Z = ReadDouble(payload, "z"),
                        Pitch = ReadDouble(payload, "pitch"),
                        Yaw = ReadDouble(payload, "yaw"),
                        Roll = ReadDouble(payload, "roll"),
                        Fov = ReadDouble(payload, "fov"),
                    },
                    Message = ReadString(payload, "message"),
                    TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
                });
                return;
            }

            if (string.Equals(type, "interaction_event", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                var eventType = ReadString(payload, "event_type");
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = string.IsNullOrWhiteSpace(eventType) ? "interaction_event" : $"interaction_{eventType}",
                    IsConnected = _isTransportConnected,
                    IsAttached = IsAttached,
                    Message = payload.ToString(),
                    TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
                });
                return;
            }

            if (string.Equals(type, "error_report", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                var category = ReadString(payload, "category");
                var source = ReadString(payload, "source");
                var message = ReadString(payload, "message");
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = $"error_{category}",
                    IsConnected = _isTransportConnected,
                    IsAttached = IsAttached,
                    Message = $"{source}: {message}",
                    TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
                });
                return;
            }

            if (string.Equals(type, "bridge_state", StringComparison.OrdinalIgnoreCase))
            {
                var payload = GetPayload(root);
                var attached = ReadBoolean(payload, "attached");
                var message = ReadString(payload, "message");
                IsAttached = attached;
                RaiseBridgeStateChanged(attached, _viewportId, message);
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = "bridge_state",
                    IsConnected = _isTransportConnected,
                    IsAttached = attached,
                    Message = message,
                    TimestampUtc = DateTimeOffset.UtcNow,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse inbound unity bridge message: {Message}", line);
        }
    }

    private PropagationUnityBridgeAck ParseAck(JsonElement root)
    {
        var payload = GetPayload(root);
        var correlationId = ReadString(root, "correlation_id");
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = ReadString(payload, "correlation_id");

        return new PropagationUnityBridgeAck
        {
            Action = ReadString(payload, "action"),
            RunId = ReadString(payload, "run_id"),
            CorrelationId = correlationId,
            Detail = ReadString(payload, "detail"),
            TimestampUtc = ReadDateTimeOffset(payload, "timestamp_utc") ?? DateTimeOffset.UtcNow,
        };
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableHeartbeat)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.HeartbeatIntervalMs), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_shouldMaintainConnection)
                continue;

            if (!_isTransportConnected || _writer is null || _reader is null)
            {
                StartReconnectLoop("Heartbeat observed disconnected transport.");
                continue;
            }

            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                var ack = await SendCommandAsync(
                    commandType: "heartbeat",
                    runId: string.Empty,
                    payload: new { viewport_id = _viewportId },
                    cancellationToken: cancellationToken);

                if (!ack.Acknowledged)
                {
                    RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                    {
                        EventType = "heartbeat_timeout",
                        IsConnected = false,
                        IsAttached = IsAttached,
                        Message = $"Unity bridge heartbeat ack timed out after {_options.AckTimeoutMs} ms.",
                        TimestampUtc = DateTimeOffset.UtcNow,
                    });
                    HandleTransportDisconnected("Heartbeat ack timed out", scheduleReconnect: true);
                    continue;
                }

                var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = "heartbeat_ack",
                    IsConnected = true,
                    IsAttached = IsAttached,
                    RttMs = elapsedMs,
                    Message = $"Heartbeat ack {elapsedMs:F0} ms.",
                    TimestampUtc = DateTimeOffset.UtcNow,
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = "heartbeat_timeout",
                    IsConnected = false,
                    IsAttached = IsAttached,
                    Message = ex.Message,
                    TimestampUtc = DateTimeOffset.UtcNow,
                });
                HandleTransportDisconnected("Heartbeat failed", scheduleReconnect: true);
            }
        }
    }

    private void StartReconnectLoop(string reason)
    {
        if (_lifecycleCts.IsCancellationRequested || !_shouldMaintainConnection || _isDisposed)
            return;

        lock (_pendingAcks)
        {
            if (_reconnectTask is { IsCompleted: false })
                return;

            _reconnectTask = Task.Run(() => ReconnectLoopAsync(reason, _lifecycleCts.Token), CancellationToken.None);
        }
    }

    private async Task ReconnectLoopAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            await _reconnectLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var attempt = 0;
            while (!cancellationToken.IsCancellationRequested && _shouldMaintainConnection)
            {
                if (_isTransportConnected && _writer is not null && _reader is not null)
                    return;

                attempt++;
                RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                {
                    EventType = "reconnect_attempt",
                    Attempt = attempt,
                    IsConnected = false,
                    IsAttached = IsAttached,
                    Message = $"{reason} attempt={attempt}",
                    TimestampUtc = DateTimeOffset.UtcNow,
                });

                try
                {
                    if (!await EnsureConnectedAsync(cancellationToken))
                    {
                        RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                        {
                            EventType = "reconnect_failed",
                            Attempt = attempt,
                            IsConnected = false,
                            IsAttached = IsAttached,
                            Message = "Unity bridge transport is unavailable.",
                            TimestampUtc = DateTimeOffset.UtcNow,
                        });
                    }
                    else
                    {
                        if (!IsAttached && !string.IsNullOrWhiteSpace(_viewportId) && !string.Equals(_viewportId, "unbound", StringComparison.Ordinal))
                        {
                            await AttachViewportAsync(_viewportId, cancellationToken);
                        }

                        if (!IsAttached && !string.IsNullOrWhiteSpace(_viewportId) && !string.Equals(_viewportId, "unbound", StringComparison.Ordinal))
                        {
                            RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                            {
                                EventType = "reconnect_failed",
                                Attempt = attempt,
                                IsConnected = _isTransportConnected,
                                IsAttached = false,
                                Message = "Unity bridge viewport attach did not complete.",
                                TimestampUtc = DateTimeOffset.UtcNow,
                            });
                        }
                        else
                        {
                            RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                            {
                                EventType = "reconnect_succeeded",
                                Attempt = attempt,
                                IsConnected = true,
                                IsAttached = IsAttached,
                                Message = "Reconnect succeeded.",
                                TimestampUtc = DateTimeOffset.UtcNow,
                            });
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
                    {
                        EventType = "reconnect_failed",
                        Attempt = attempt,
                        IsConnected = false,
                        IsAttached = IsAttached,
                        Message = ex.Message,
                        TimestampUtc = DateTimeOffset.UtcNow,
                    });
                }

                var backoff = _options.ReconnectBackoffMs[Math.Min(attempt - 1, _options.ReconnectBackoffMs.Length - 1)];
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(backoff), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private void HandleTransportDisconnected(string reason, bool scheduleReconnect)
    {
        var wasConnected = _isTransportConnected;
        var wasAttached = IsAttached;

        SafeCloseTransport();
        _isTransportConnected = false;
        IsAttached = false;

        foreach (var pending in _pendingAcks)
        {
            if (_pendingAcks.TryRemove(pending.Key, out var tcs))
            {
                tcs.TrySetException(new IOException("Unity bridge disconnected."));
            }
        }

        if (wasConnected || wasAttached)
        {
            RaiseBridgeStateChanged(false, _viewportId, reason);
        }

        RaiseTelemetry(new PropagationUnityBridgeTelemetryEventArgs
        {
            EventType = "disconnected",
            IsConnected = false,
            IsAttached = false,
            Message = reason,
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        if (scheduleReconnect)
        {
            StartReconnectLoop(reason);
        }
    }

    private void SafeCloseTransport()
    {
        try { _listenCts?.Cancel(); } catch { /* ignore */ }
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _pipe?.Dispose(); } catch { /* ignore */ }
        try { _tcpClient?.Dispose(); } catch { /* ignore */ }

        _listenCts = null;
        _listenTask = null;
        _reader = null;
        _writer = null;
        _pipe = null;
        _tcpClient = null;
    }

    private static JsonElement GetPayload(JsonElement root)
    {
        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            return payload;
        return root;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
                return value;
            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), out var parsed))
                return parsed;
        }
        return 0d;
    }

    private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0d;
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            {
                value = number;
                return true;
            }
            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }
        return false;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.True)
                return true;
            if (property.ValueKind == JsonValueKind.False)
                return false;
            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out var parsed))
                return parsed;
        }
        return false;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out var value))
        {
            return value;
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string name, uint timeout);

    private readonly record struct TransportConnectResult(bool Succeeded, string Message);

    private void RaiseBridgeStateChanged(bool isAttached, string viewportId, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BridgeStateChanged?.Invoke(this, new PropagationUnityBridgeStateChangedEventArgs
            {
                IsAttached = isAttached,
                ViewportId = viewportId,
                Message = message,
            });
        });
    }

    private void RaiseTelemetry(PropagationUnityBridgeTelemetryEventArgs args)
    {
        Dispatcher.UIThread.Post(() => TelemetryUpdated?.Invoke(this, args));
    }

    private void RaiseMapPointSelected(PropagationUnityMapPointSelectedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => MapPointSelected?.Invoke(this, args));
    }

    private void RaiseProfileLineChanged(PropagationUnityProfileLineChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => ProfileLineChanged?.Invoke(this, args));
    }

    private void RaiseLayerStateChanged(PropagationUnityLayerStateChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => LayerStateChanged?.Invoke(this, args));
    }

    private void RaiseDiagnosticSnapshot(PropagationUnityDiagnosticSnapshotEventArgs args)
    {
        Dispatcher.UIThread.Post(() => DiagnosticSnapshotReceived?.Invoke(this, args));
    }

    private void RaiseCameraStateChanged(PropagationUnityCameraStateChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() => CameraStateChanged?.Invoke(this, args));
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(UnityProcessPropagationBridge));
    }

    private sealed class OutboundEnvelope
    {
        public string Type { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public DateTimeOffset TimestampUtc { get; init; }
        public object? Payload { get; init; }
    }

    private sealed class UnityBridgeOptions
    {
        public UnityBridgeTransportMode TransportMode { get; init; } = UnityBridgeTransportMode.NamedPipe;
        public string PipeName { get; init; } = "TrailMateCenter.Propagation.Bridge";
        public string TcpHost { get; init; } = "127.0.0.1";
        public int TcpPort { get; init; } = 51110;
        public int ConnectTimeoutMs { get; init; } = 5000;
        public int AckTimeoutMs { get; init; } = 2000;
        public bool EnableHeartbeat { get; init; } = true;
        public int HeartbeatIntervalMs { get; init; } = 3000;
        public int[] ReconnectBackoffMs { get; init; } = [1000, 2000, 5000, 10000];

        public static UnityBridgeOptions FromEnvironment()
        {
            var modeRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE");
            var mode = string.Equals(modeRaw, "tcp", StringComparison.OrdinalIgnoreCase)
                ? UnityBridgeTransportMode.Tcp
                : UnityBridgeTransportMode.NamedPipe;

            var pipeName = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_PIPE_NAME");
            var host = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_TCP_HOST");
            var portRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_TCP_PORT");
            var connectTimeoutRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_CONNECT_TIMEOUT_MS");
            var ackTimeoutRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_ACK_TIMEOUT_MS");
            var heartbeatEnabledRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_HEARTBEAT_ENABLED");
            var heartbeatIntervalRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_HEARTBEAT_INTERVAL_MS");
            var reconnectBackoffRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_RECONNECT_BACKOFF_MS");

            return new UnityBridgeOptions
            {
                TransportMode = mode,
                PipeName = string.IsNullOrWhiteSpace(pipeName) ? "TrailMateCenter.Propagation.Bridge" : pipeName.Trim(),
                TcpHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(),
                TcpPort = int.TryParse(portRaw, out var port) ? port : 51110,
                ConnectTimeoutMs = int.TryParse(connectTimeoutRaw, out var connectTimeout) ? connectTimeout : 5000,
                AckTimeoutMs = int.TryParse(ackTimeoutRaw, out var ackTimeout) ? ackTimeout : 2000,
                EnableHeartbeat = ParseBoolean(heartbeatEnabledRaw, defaultValue: true),
                HeartbeatIntervalMs = Math.Max(500, int.TryParse(heartbeatIntervalRaw, out var heartbeatInterval) ? heartbeatInterval : 3000),
                ReconnectBackoffMs = ParseReconnectBackoff(reconnectBackoffRaw),
            };
        }

        private static bool ParseBoolean(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            if (bool.TryParse(value, out var parsed))
                return parsed;

            return value.Trim() switch
            {
                "1" => true,
                "yes" => true,
                "on" => true,
                "0" => false,
                "no" => false,
                "off" => false,
                _ => defaultValue,
            };
        }

        private static int[] ParseReconnectBackoff(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return [1000, 2000, 5000, 10000];

            var values = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static token => int.TryParse(token, out var parsed) ? Math.Max(250, parsed) : 0)
                .Where(static value => value > 0)
                .ToArray();

            return values.Length == 0 ? [1000, 2000, 5000, 10000] : values;
        }
    }

    private enum UnityBridgeTransportMode
    {
        NamedPipe = 0,
        Tcp = 1,
    }
}
