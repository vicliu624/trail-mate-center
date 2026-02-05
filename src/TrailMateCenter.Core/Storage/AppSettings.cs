using TrailMateCenter.Models;

namespace TrailMateCenter.Storage;

public sealed record AppSettings
{
    public bool AutoReconnect { get; init; } = true;
    public bool AutoConnectOnDetect { get; init; }
    public int AckTimeoutMs { get; init; } = 1500;
    public int MaxRetries { get; init; } = 2;
    public string? LastPort { get; init; }
    public string? LastReplayFile { get; init; }
    public double ReplaySpeed { get; init; } = 1.0;
    public TacticalSettings Tactical { get; init; } = new();
}

public sealed record TacticalSettings
{
    public int OnlineThresholdSeconds { get; init; } = 30;
    public int WeakThresholdSeconds { get; init; } = 120;
    public byte CommandChannel { get; init; } = 0;
    public Dictionary<TacticalEventKind, TacticalSeverity> SeverityOverrides { get; init; } = new();
}
