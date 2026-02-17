namespace RJCP.Core.Environment.Version.NetFx.Net45
{
    internal sealed class NetFxVersion
    {
        internal NetFxVersion(string version, string description)
        {
            Version = version;
            Description = description;
        }

        public string Version { get; private set; }

        public string Description { get; private set; }
    }
}
