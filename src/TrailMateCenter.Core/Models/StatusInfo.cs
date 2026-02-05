using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record StatusInfo
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
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
