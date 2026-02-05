using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record DeviceConfig
{
    public Dictionary<HostLinkConfigKey, byte[]> Items { get; init; } = new();
}
