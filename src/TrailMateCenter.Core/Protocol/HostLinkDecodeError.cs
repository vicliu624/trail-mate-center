namespace TrailMateCenter.Protocol;

public enum HostLinkDecodeErrorCode
{
    InvalidSof,
    LengthTooLarge,
    CrcMismatch,
}

public sealed record HostLinkDecodeError(
    HostLinkDecodeErrorCode Code,
    string Message,
    byte[]? RawBytes = null);
