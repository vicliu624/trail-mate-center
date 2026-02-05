namespace TrailMateCenter.Models;

public enum RawFrameDirection
{
    Rx,
    Tx,
}

public enum RawFrameStatus
{
    Ok,
    CrcMismatch,
    Invalid,
}

public sealed record RawFrameRecord(
    DateTimeOffset Timestamp,
    RawFrameDirection Direction,
    RawFrameStatus Status,
    byte[] Frame,
    string? Note);
