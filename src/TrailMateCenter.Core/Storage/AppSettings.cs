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
    public AprsSettings Aprs { get; init; } = new();
}

public sealed record TacticalSettings
{
    public int OnlineThresholdSeconds { get; init; } = 30;
    public int WeakThresholdSeconds { get; init; } = 120;
    public byte CommandChannel { get; init; } = 0;
    public Dictionary<TacticalEventKind, TacticalSeverity> SeverityOverrides { get; init; } = new();
}

public sealed record AprsSettings
{
    public bool Enabled { get; init; }
    public string ServerHost { get; init; } = "rotate.aprs2.net";
    public int ServerPort { get; init; } = 14580;
    public string IgateCallsign { get; init; } = string.Empty;
    public byte IgateSsid { get; init; }
    public string Passcode { get; init; } = string.Empty;
    public string ToCall { get; init; } = "APRS";
    public string Path { get; init; } = string.Empty;
    public string Filter { get; init; } = string.Empty;
    public int TxMinIntervalSec { get; init; } = 30;
    public int DedupeWindowSec { get; init; } = 30;
    public int PositionIntervalSec { get; init; } = 60;
    public char SymbolTable { get; init; } = '/';
    public char SymbolCode { get; init; } = '>';
    public bool UseCompressed { get; init; } = true;
    public bool EmitStatus { get; init; } = true;
    public bool EmitTelemetry { get; init; } = true;
    public bool EmitWeather { get; init; } = true;
    public bool EmitMessages { get; init; } = true;
    public bool EmitWaypoints { get; init; } = true;
}
