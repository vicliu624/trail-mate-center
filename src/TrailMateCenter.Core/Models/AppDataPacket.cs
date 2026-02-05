using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record AppDataPacket(
    uint Portnum,
    uint From,
    uint To,
    byte Channel,
    HostLinkAppDataFlags Flags,
    byte[] TeamId,
    uint TeamKeyId,
    uint DeviceUptimeSeconds,
    byte[] Payload);
