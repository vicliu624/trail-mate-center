namespace TrailMateCenter.Services;

public sealed class FakePropagationUnityBridge : IPropagationUnityBridge
{
    private readonly object _syncRoot = new();
    private string _viewportId = "unbound";

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
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(80, cancellationToken);

        lock (_syncRoot)
        {
            IsAttached = true;
            _viewportId = string.IsNullOrWhiteSpace(viewportId) ? "embedded-slot" : viewportId.Trim();
        }

        BridgeStateChanged?.Invoke(this, new PropagationUnityBridgeStateChangedEventArgs
        {
            IsAttached = true,
            ViewportId = _viewportId,
            Message = "Unity viewport attached",
        });
        TelemetryUpdated?.Invoke(this, new PropagationUnityBridgeTelemetryEventArgs
        {
            EventType = "connected",
            IsConnected = true,
            IsAttached = true,
            Message = "Mock bridge attached.",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        EmitMockDiagnostics();
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            IsAttached = false;
        }

        BridgeStateChanged?.Invoke(this, new PropagationUnityBridgeStateChangedEventArgs
        {
            IsAttached = false,
            ViewportId = _viewportId,
            Message = "Mock bridge disconnected",
        });
        TelemetryUpdated?.Invoke(this, new PropagationUnityBridgeTelemetryEventArgs
        {
            EventType = "disconnected",
            IsConnected = false,
            IsAttached = false,
            Message = "Mock bridge disconnected.",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        return Task.CompletedTask;
    }

    public async Task<PropagationUnityBridgeAck> PushSimulationRequestAsync(
        string runId,
        PropagationSimulationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAttached();
        await Task.Delay(40, cancellationToken);

        var seed = Math.Abs(HashCode.Combine(runId, request.FrequencyMHz, request.TxPowerDbm));
        var x = 1200 + seed % 800;
        var y = 2200 + (seed / 10) % 800;
        MapPointSelected?.Invoke(this, new PropagationUnityMapPointSelectedEventArgs
        {
            X = x,
            Y = y,
            NodeId = $"node_{seed % 97:00}",
        });
        TelemetryUpdated?.Invoke(this, new PropagationUnityBridgeTelemetryEventArgs
        {
            EventType = "mock_push_request",
            IsConnected = true,
            IsAttached = true,
            RttMs = 40,
            Message = $"Mock request accepted for run {runId}.",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        return new PropagationUnityBridgeAck
        {
            Action = "push_request",
            RunId = runId,
            Detail = $"mode={request.Mode}, freq={request.FrequencyMHz:F1}MHz",
            TimestampUtc = DateTimeOffset.UtcNow,
        };
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
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAttached();
        await Task.Delay(20, cancellationToken);

        var layerIds = presentation.LayerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        if (layerIds.Length == 0)
            throw new ArgumentException("At least one layer id is required.", nameof(presentation));

        var primaryLayerId = layerIds[0];
        LayerStateChanged?.Invoke(this, new PropagationUnityLayerStateChangedEventArgs
        {
            LayerId = primaryLayerId,
            RunId = runId,
            State = "ready",
            ProgressPercent = 100,
            TransitionMs = 180,
            Message = "Mock layer active",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        foreach (var id in layerIds.Skip(1))
        {
            LayerStateChanged?.Invoke(this, new PropagationUnityLayerStateChangedEventArgs
            {
                LayerId = id,
                RunId = runId,
                State = "ready",
                ProgressPercent = 100,
                TransitionMs = 180,
                Message = "Mock stacked layer active",
                TimestampUtc = DateTimeOffset.UtcNow,
            });
        }

        return new PropagationUnityBridgeAck
        {
            Action = "set_active_layer",
            RunId = runId,
            Detail = $"layers={string.Join(",", layerIds)}",
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<PropagationUnityBridgeAck> SetCameraStateAsync(
        PropagationUnityCameraState cameraState,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAttached();
        await Task.Delay(20, cancellationToken);

        CameraStateChanged?.Invoke(this, new PropagationUnityCameraStateChangedEventArgs
        {
            CameraState = cameraState,
            Message = "Mock camera state applied",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        return new PropagationUnityBridgeAck
        {
            Action = "set_camera_state",
            RunId = runId,
            Detail = "camera updated",
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<PropagationUnityBridgeAck> PushSimulationResultAsync(
        PropagationSimulationResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAttached();
        await Task.Delay(40, cancellationToken);

        var seed = Math.Abs(HashCode.Combine(result.RunMeta.RunId, result.AnalysisOutputs.Profile.DistanceKm));
        var sx = 900 + seed % 600;
        var sy = 1400 + (seed / 7) % 600;
        var ex = sx + 1000 + seed % 400;
        var ey = sy + 400 + (seed / 11) % 300;
        ProfileLineChanged?.Invoke(this, new PropagationUnityProfileLineChangedEventArgs
        {
            StartX = sx,
            StartY = sy,
            EndX = ex,
            EndY = ey,
        });
        TelemetryUpdated?.Invoke(this, new PropagationUnityBridgeTelemetryEventArgs
        {
            EventType = "mock_push_result",
            IsConnected = true,
            IsAttached = true,
            RttMs = 40,
            Message = $"Mock result accepted for run {result.RunMeta.RunId}.",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        return new PropagationUnityBridgeAck
        {
            Action = "push_result",
            RunId = result.RunMeta.RunId,
            Detail = $"status={result.RunMeta.Status}, p95={result.AnalysisOutputs.Reliability.P95:F1}%",
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    private void EnsureAttached()
    {
        if (!IsAttached)
            throw new InvalidOperationException("Unity bridge is not attached.");
    }

    private void EmitMockDiagnostics()
    {
        var fps = 58 + Random.Shared.NextDouble() * 4;
        DiagnosticSnapshotReceived?.Invoke(this, new PropagationUnityDiagnosticSnapshotEventArgs
        {
            Fps = fps,
            FrameTimeP95Ms = 1000 / Math.Max(1, fps) * 1.6,
            GpuMemoryMb = 620 + Random.Shared.NextDouble() * 40,
            LayerLoadMs = 140 + Random.Shared.NextDouble() * 30,
            TileCacheHitRate = 0.86 + Random.Shared.NextDouble() * 0.1,
            Message = "Mock diagnostics",
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }
}
