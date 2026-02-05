namespace TrailMateCenter.StateMachine;

public sealed class ConnectionStateMachine
{
    public event EventHandler<(ConnectionState OldState, ConnectionState NewState, string? Reason)>? StateChanged;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError { get; private set; }

    public void Transition(ConnectionState next, string? reason = null)
    {
        if (State == next && reason is null)
            return;

        var old = State;
        State = next;
        if (next == ConnectionState.Error)
            LastError = reason;
        StateChanged?.Invoke(this, (old, next, reason));
    }
}
