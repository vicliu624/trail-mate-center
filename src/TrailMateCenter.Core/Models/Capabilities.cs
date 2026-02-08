using TrailMateCenter.Protocol;

namespace TrailMateCenter.Models;

public sealed record Capabilities
{
    public ushort MaxFrameLength { get; init; }
    public HostLinkCapabilities CapabilitiesMask { get; init; }

    public bool SupportsTxMsg => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapTxMsg);
    public bool SupportsConfig => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapConfig);
    public bool SupportsSetTime => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapSetTime);
    public bool SupportsStatus => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapStatus);
    public bool SupportsLogs => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapLogs);
    public bool SupportsGps => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapGps);
    public bool SupportsAppData => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapAppData);
    public bool SupportsTeamState => CapabilitiesMask.HasFlag(HostLinkCapabilities.CapTeamState);
}
