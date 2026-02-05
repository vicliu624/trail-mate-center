using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record TxResult
{
    public uint MessageId { get; init; }
    public bool Success { get; init; }
    public HostLinkErrorCode ErrorCode { get; init; } = HostLinkErrorCode.Ok;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
