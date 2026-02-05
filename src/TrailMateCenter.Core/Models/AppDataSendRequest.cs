using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record AppDataSendRequest
{
    public uint Portnum { get; init; }
    public uint To { get; init; }
    public byte Channel { get; init; }
    public HostLinkAppDataFlags Flags { get; init; }
    public byte[] TeamId { get; init; } = new byte[8];
    public uint TeamKeyId { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}
