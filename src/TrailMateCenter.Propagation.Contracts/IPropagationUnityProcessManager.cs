namespace TrailMateCenter.Services;

public interface IPropagationUnityProcessManager
{
    bool IsRunning { get; }
    int? ProcessId { get; }
    PropagationUnityProcessState ProcessState { get; }

    event EventHandler<PropagationUnityProcessStateChangedEventArgs>? ProcessStateChanged;

    Task<PropagationUnityProcessSnapshot> EnsureStartedAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public enum PropagationUnityProcessState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Faulted = 4,
    ExternalManaged = 5,
}

public sealed class PropagationUnityProcessSnapshot
{
    public PropagationUnityProcessState ProcessState { get; init; } = PropagationUnityProcessState.Stopped;
    public int? ProcessId { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public bool IsManagedExternally { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PropagationUnityProcessStateChangedEventArgs : EventArgs
{
    public PropagationUnityProcessState ProcessState { get; init; } = PropagationUnityProcessState.Stopped;
    public int? ProcessId { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
