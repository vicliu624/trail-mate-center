namespace RJCP.Core.Environment.Version.NetFx.Net45
{
    using System.Collections.Generic;
    using System.Linq;
    using Resources;

    internal static class NetFxConfig
    {
        // https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed
        private readonly static Dictionary<int, NetFxVersion> Versions = new Dictionary<int, NetFxVersion>() {
            { 378389, new NetFxVersion("4.5", Messages.Net45) },
            { 378675, new NetFxVersion("4.5.1", Messages.Net451_Win81) },
            { 378758, new NetFxVersion("4.5.1", Messages.Net451_Vista) },
            { 379893, new NetFxVersion("4.5.2", Messages.Net452) },
            { 393295, new NetFxVersion("4.6", Messages.Net46_Win10) },
            { 393297, new NetFxVersion("4.6", Messages.Net46) },
            { 394254, new NetFxVersion("4.6.1", Messages.Net461_Win10) },
            { 394271, new NetFxVersion("4.6.1", Messages.Net461) },
            { 394802, new NetFxVersion("4.6.2", Messages.Net462_Win10) },
            { 394806, new NetFxVersion("4.6.2", Messages.Net462) },
            { 460798, new NetFxVersion("4.7", Messages.Net47_Win10) },
            { 460805, new NetFxVersion("4.7", Messages.Net47) },
            { 461308, new NetFxVersion("4.7.1", Messages.Net471_Win10) },
            { 461310, new NetFxVersion("4.7.1", Messages.Net471) },
            { 461808, new NetFxVersion("4.7.2", Messages.Net472_Win10) },
            { 461814, new NetFxVersion("4.7.2", Messages.Net472) },
            { 528040, new NetFxVersion("4.8", Messages.Net48_Win10_1903) },
            { 528049, new NetFxVersion("4.8", Messages.Net48) },
            { 528209, new NetFxVersion("4.8", Messages.Net48_Win10_1909) },
            { 528372, new NetFxVersion("4.8", Messages.Net48_Win10_20H2) },
            { 528449, new NetFxVersion("4.8", Messages.Net48_Win11) },
            { 533320, new NetFxVersion("4.8.1", Messages.Net481_Win11_22H2) },
            { 533325, new NetFxVersion("4.8.1", Messages.Net481) }
        };

        public static NetFxVersion GetNetFxVersion(int release)
        {
            string description;
            if (Versions.TryGetValue(release, out NetFxVersion version)) return version;

            var versionList = from netVersion in Versions
                              orderby netVersion.Key select netVersion;
            NetFxVersion derivedVersion = null;
            foreach (var netVersion in versionList) {
                if (netVersion.Key <= release)
                    derivedVersion = netVersion.Value;

                if (netVersion.Key >= release) {
                    if (derivedVersion == null) {
                        description = string.Format(Messages.Net45_Unknown, release);
                        return new NetFxVersion("< 4.5", description);
                    }
                    break;
                }
            }

            // This can't happen, because the enumeration will always have at least one element, but quiesces a possible
            // null pointer warning.
            if (derivedVersion == null) return null;

            description = string.Format(Messages.NetFx, derivedVersion.Version, release);
            return new NetFxVersion(derivedVersion.Version, description);
        }
    }
}
