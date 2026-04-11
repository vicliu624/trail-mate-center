namespace TrailMateCenter.Services;

public sealed class FakePropagationUnityProcessManager : IPropagationUnityProcessManager
{
    private readonly object _syncRoot = new();
    private int? _processId;
    private PropagationUnityProcessState _processState = PropagationUnityProcessState.Stopped;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _processState == PropagationUnityProcessState.Running;
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_syncRoot)
            {
                return _processId;
            }
        }
    }

    public PropagationUnityProcessState ProcessState
    {
        get
        {
            lock (_syncRoot)
            {
                return _processState;
            }
        }
    }

    public event EventHandler<PropagationUnityProcessStateChangedEventArgs>? ProcessStateChanged;

    public async Task<PropagationUnityProcessSnapshot> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool alreadyRunning;
        lock (_syncRoot)
        {
            alreadyRunning = _processState == PropagationUnityProcessState.Running;
        }

        if (!alreadyRunning)
        {
            SetState(PropagationUnityProcessState.Starting, null, "Mock Unity process is starting.");
            await Task.Delay(120, cancellationToken);

            var fakePid = 20000 + Random.Shared.Next(1000, 9000);
            lock (_syncRoot)
            {
                _processId = fakePid;
            }
            SetState(PropagationUnityProcessState.Running, fakePid, "Mock Unity process is running.");
        }

        return new PropagationUnityProcessSnapshot
        {
            ProcessState = ProcessState,
            ProcessId = ProcessId,
            ExecutablePath = "mock://unity-process",
            IsManagedExternally = false,
            Message = alreadyRunning ? "Mock Unity process already running." : "Mock Unity process started.",
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool shouldStop;
        lock (_syncRoot)
        {
            shouldStop = _processState == PropagationUnityProcessState.Running;
        }

        if (!shouldStop)
        {
            SetState(PropagationUnityProcessState.Stopped, null, "Mock Unity process already stopped.");
            return;
        }

        SetState(PropagationUnityProcessState.Stopping, ProcessId, "Mock Unity process is stopping.");
        await Task.Delay(80, cancellationToken);
        lock (_syncRoot)
        {
            _processId = null;
        }
        SetState(PropagationUnityProcessState.Stopped, null, "Mock Unity process stopped.");
    }

    private void SetState(PropagationUnityProcessState state, int? processId, string message)
    {
        lock (_syncRoot)
        {
            _processState = state;
        }

        ProcessStateChanged?.Invoke(this, new PropagationUnityProcessStateChangedEventArgs
        {
            ProcessState = state,
            ProcessId = processId,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }
}
