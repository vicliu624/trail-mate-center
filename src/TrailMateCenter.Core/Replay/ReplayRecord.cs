namespace TrailMateCenter.Replay;

public sealed record ReplayRecord
{
    public DateTimeOffset Timestamp { get; init; }
    public string Direction { get; init; } = "rx";
    public string Hex { get; init; } = string.Empty;
}
