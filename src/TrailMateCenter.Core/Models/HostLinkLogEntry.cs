using Microsoft.Extensions.Logging;

namespace TrailMateCenter.Models;

public sealed record HostLinkLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public LogLevel Level { get; init; } = LogLevel.Information;
    public string Message { get; init; } = string.Empty;
    public string? RawCode { get; init; }
}
