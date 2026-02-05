using TrailMateCenter.Models;

namespace TrailMateCenter.Protocol;

public sealed record HostLinkRawFrameData(
    byte[] Frame,
    RawFrameStatus Status,
    string? Note);
