namespace TrailMateCenter.Models;

public enum MessageDirection
{
    Incoming,
    Outgoing,
    System,
}

public enum MessageDeliveryStatus
{
    Pending,
    Acked,
    Succeeded,
    Failed,
    Timeout,
}

public sealed record MessageEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeviceTimestamp { get; init; }
    public MessageDirection Direction { get; init; }
    public uint? MessageId { get; set; }
    public uint? FromId { get; init; }
    public uint? ToId { get; init; }
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public byte ChannelId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int? Rssi { get; init; }
    public int? Snr { get; init; }
    public int? Hop { get; init; }
    public bool? Direct { get; init; }
    public RxOrigin? Origin { get; init; }
    public bool? FromIs { get; init; }
    public int? Retry { get; init; }
    public int? AirtimeMs { get; init; }
    public ushort Seq { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Altitude { get; init; }
    public bool IsTeamChat { get; init; }
    public string? TeamConversationKey { get; init; }
}
