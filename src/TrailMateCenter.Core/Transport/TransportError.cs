namespace TrailMateCenter.Transport;

public enum TransportErrorType
{
    OpenFailed,
    ReadError,
    WriteError,
    Disconnected,
    ReplayCompleted,
    Unknown,
}

public sealed record TransportError(TransportErrorType Type, string Message, Exception? Exception = null);
