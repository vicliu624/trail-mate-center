namespace TrailMateCenter.Models;

public enum RxTimeSource : byte
{
    Unknown = 0,
    Uptime = 1,
    DeviceUtc = 2,
    GpsUtc = 3,
}

public enum RxOrigin : byte
{
    Unknown = 0,
    Mesh = 1,
    External = 2,
}

public sealed record RxMetadata
{
    public DateTimeOffset? TimestampUtc { get; init; }
    public uint? TimestampMs { get; init; }
    public RxTimeSource TimeSource { get; init; } = RxTimeSource.Unknown;
    public bool? Direct { get; init; }
    public byte? HopCount { get; init; }
    public byte? HopLimit { get; init; }
    public RxOrigin Origin { get; init; } = RxOrigin.Unknown;
    public bool? FromIs { get; init; }
    public int? RssiDbm { get; init; }
    public int? SnrDb { get; init; }
    public uint? FreqHz { get; init; }
    public uint? BwHz { get; init; }
    public byte? Sf { get; init; }
    public byte? Cr { get; init; }
    public uint? PacketId { get; init; }
}
