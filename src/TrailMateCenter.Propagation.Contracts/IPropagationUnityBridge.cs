namespace TrailMateCenter.Services;

public interface IPropagationUnityBridge
{
    bool IsAttached { get; }

    event EventHandler<PropagationUnityBridgeStateChangedEventArgs>? BridgeStateChanged;
    event EventHandler<PropagationUnityBridgeTelemetryEventArgs>? TelemetryUpdated;
    event EventHandler<PropagationUnityLayerStateChangedEventArgs>? LayerStateChanged;
    event EventHandler<PropagationUnityDiagnosticSnapshotEventArgs>? DiagnosticSnapshotReceived;
    event EventHandler<PropagationUnityCameraStateChangedEventArgs>? CameraStateChanged;
    event EventHandler<PropagationUnityMapPointSelectedEventArgs>? MapPointSelected;
    event EventHandler<PropagationUnityProfileLineChangedEventArgs>? ProfileLineChanged;

    Task AttachViewportAsync(string viewportId, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<PropagationUnityBridgeAck> SetActiveLayerAsync(
        string layerId,
        string runId,
        CancellationToken cancellationToken);

    Task<PropagationUnityBridgeAck> SetLayerPresentationAsync(
        PropagationUnityLayerPresentation presentation,
        string runId,
        CancellationToken cancellationToken);

    Task<PropagationUnityBridgeAck> SetCameraStateAsync(
        PropagationUnityCameraState cameraState,
        string runId,
        CancellationToken cancellationToken);

    Task<PropagationUnityBridgeAck> PushSimulationRequestAsync(
        string runId,
        PropagationSimulationRequest request,
        CancellationToken cancellationToken);

    Task<PropagationUnityBridgeAck> PushSimulationResultAsync(
        PropagationSimulationResult result,
        CancellationToken cancellationToken);
}

public sealed class PropagationUnityBridgeAck
{
    public string Action { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Detail { get; init; } = string.Empty;
    public bool Acknowledged { get; init; } = true;
}

public sealed class PropagationUnityBridgeStateChangedEventArgs : EventArgs
{
    public bool IsAttached { get; init; }
    public string ViewportId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class PropagationUnityBridgeTelemetryEventArgs : EventArgs
{
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsConnected { get; init; }
    public bool IsAttached { get; init; }
    public int Attempt { get; init; }
    public double? RttMs { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class PropagationUnityLayerStateChangedEventArgs : EventArgs
{
    public string LayerId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public double? ProgressPercent { get; init; }
    public double? TransitionMs { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PropagationUnityDiagnosticSnapshotEventArgs : EventArgs
{
    public double Fps { get; init; }
    public double FrameTimeP95Ms { get; init; }
    public double GpuMemoryMb { get; init; }
    public double LayerLoadMs { get; init; }
    public double TileCacheHitRate { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PropagationUnityCameraStateChangedEventArgs : EventArgs
{
    public PropagationUnityCameraState CameraState { get; init; } = new();
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PropagationUnityCameraState
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Pitch { get; init; }
    public double Yaw { get; init; }
    public double Roll { get; init; }
    public double Fov { get; init; }
}

public sealed class PropagationUnityLayerPresentation
{
    public IReadOnlyList<string> LayerIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, bool> LayerVisibility { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, double> LayerOpacity { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);
    public IReadOnlyList<string> LayerOrder { get; init; } = Array.Empty<string>();
}

public sealed class PropagationUnityMapPointSelectedEventArgs : EventArgs
{
    public double X { get; init; }
    public double Y { get; init; }
    public string NodeId { get; init; } = string.Empty;
}

public sealed class PropagationUnityProfileLineChangedEventArgs : EventArgs
{
    public double StartX { get; init; }
    public double StartY { get; init; }
    public double EndX { get; init; }
    public double EndY { get; init; }
}
