namespace RJCP.Core.Environment.Version.NetFx
{
    using System;
    using System.Security;
    using System.Text;
    using Microsoft.Win32;
    using Resources;

    /// <summary>
    /// Older version of .NET 2.0 to 3.5
    /// </summary>
    public sealed class NetFxLegacy : INetVersion
    {
        internal NetFxLegacy(string key)
        {
            try {
                GetNetFxDetails(key);
            } catch (SecurityException) {
                IsValid = false;
            } catch (Exception) {
                IsValid = false;
            }
        }

        private void GetNetFxDetails(string key)
        {
            if (!key.StartsWith("v")) {
                IsValid = false;
                return;
            }

            string fullKeyPath = string.Format(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\{0}", key);
            using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(fullKeyPath)) {
                IsValid = false;
                if (registryKey == null) return;

                string installed = registryKey.GetValue("Install", "").ToString();
                if (installed == null || installed != "1") return;

                string[] path = key.Split('\\');

#if NETFRAMEWORK
                string netVersion = (string)registryKey.GetValue("Version")
                    ?? path[0].Substring(1);
#else
                string netVersion = (string)registryKey.GetValue("Version")
                    ?? path[0][1..];
#endif
                NetVersion = new Version(netVersion);

                string servicePack = registryKey.GetValue("SP", "").ToString();
                if (servicePack.Equals("0")) servicePack = string.Empty;

                StringBuilder version = new StringBuilder();
#if NETFRAMEWORK
                version.Append(path[0].Substring(1));
#else
                version.Append(path[0][1..]);
#endif
                if (path.Length >= 2) version.Append(' ').Append(path[1]);
                if (!string.IsNullOrEmpty(servicePack)) version.Append(" SP").Append(servicePack);
                Version = version.ToString();

                StringBuilder description = new StringBuilder();
                description.Append(Messages.NetFxLegacy).Append(' ').Append(netVersion);
                if (path.Length >= 2)
                    description.Append(' ').Append(Messages.NetFxLegacyProfile).Append(' ').Append(path[1]);
                if (!string.IsNullOrEmpty(servicePack))
                    description.Append(' ').Append(Messages.NetFxLegacySp).Append(servicePack);
                Description = description.ToString();

                IsValid = true;
            }
        }

        /// <summary>
        /// Gets the version of .NET as read from the registry.
        /// </summary>
        /// <value>The version of .NET as read from the registry.</value>
        public Version NetVersion { get; private set; }

        /// <summary>
        /// Returns <see langword="true"/> if the version information contains valid (even if partially) information.
        /// </summary>
        /// <value><see langword="true"/> if this instance is valid; otherwise, <see langword="false"/>.</value>
        public bool IsValid { get; private set; }

        /// <summary>
        /// Gets the description of the .NET version installed.
        /// </summary>
        /// <value>The .NET version description.</value>
        public string Description { get; private set; }

        /// <summary>
        /// Gets the version string for the .NET version installed.
        /// </summary>
        /// <value>The .NET version string.</value>
        public string Version { get; private set; }
    }
}
