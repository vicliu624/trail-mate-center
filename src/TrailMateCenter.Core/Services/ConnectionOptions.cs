namespace TrailMateCenter.Services;

public sealed record ConnectionOptions
{
    public bool AutoReconnect { get; init; } = true;
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan AckTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);
    public int MaxRetries { get; init; } = 2;
}
