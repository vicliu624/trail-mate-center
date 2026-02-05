using Microsoft.Extensions.Logging;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public abstract record HostLinkEvent(DateTimeOffset Timestamp);

public sealed record RxMessageEvent(
    DateTimeOffset Timestamp,
    uint MessageId,
    uint From,
    uint To,
    byte Channel,
    string Text) : HostLinkEvent(Timestamp);

public sealed record TxResultEvent(
    DateTimeOffset Timestamp,
    uint MessageId,
    bool Success,
    HostLinkErrorCode ErrorCode,
    string Reason) : HostLinkEvent(Timestamp);

public sealed record StatusEvent(
    DateTimeOffset Timestamp) : HostLinkEvent(Timestamp)
{
    public int? BatteryPercent { get; init; }
    public bool IsCharging { get; init; }
    public HostLinkLinkState LinkState { get; init; } = HostLinkLinkState.Waiting;
    public byte MeshProtocol { get; init; }
    public byte Region { get; init; }
    public byte Channel { get; init; }
    public bool DutyCycleEnabled { get; init; }
    public byte ChannelUtil { get; init; }
    public uint LastError { get; init; }
}

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message,
    string? RawCode) : HostLinkEvent(Timestamp);

public sealed record GpsEvent(
    DateTimeOffset Timestamp,
    bool HasFix,
    byte Satellites,
    uint AgeMs,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    double? SpeedMps,
    double? CourseDeg) : HostLinkEvent(Timestamp);

public sealed record AppDataEvent(
    DateTimeOffset Timestamp,
    uint Portnum,
    uint From,
    uint To,
    byte Channel,
    HostLinkAppDataFlags Flags,
    byte[] TeamId,
    uint TeamKeyId,
    uint DeviceUptimeSeconds,
    uint TotalLength,
    uint Offset,
    ushort ChunkLength,
    byte[] Chunk) : HostLinkEvent(Timestamp);

public sealed record ConfigEvent(
    DateTimeOffset Timestamp,
    DeviceConfig Config) : HostLinkEvent(Timestamp);
