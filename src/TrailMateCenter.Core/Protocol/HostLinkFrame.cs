namespace TrailMateCenter.Protocol;

public readonly record struct HostLinkFrame(
    HostLinkFrameType Type,
    ushort Seq,
    ReadOnlyMemory<byte> Payload,
    byte Version = HostLinkConstants.Version);
