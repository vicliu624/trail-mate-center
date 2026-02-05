namespace TrailMateCenter.StateMachine;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Handshaking,
    Ready,
    Error,
    Reconnecting,
}
