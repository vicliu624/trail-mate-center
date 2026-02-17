namespace RJCP.Core.Environment.Version.NetFx
{
    using System;
    using System.Security;
    using Microsoft.Win32;
    using Net45;
    using Resources;

    /// <summary>
    /// Installation details for .NET 4.5 and later (up to .NET 4.8).
    /// </summary>
    public sealed class NetFx45 : INetVersion
    {
        internal NetFx45()
        {
            GetNetFx45Details();
        }

        internal NetFx45(int netfx45release)
        {
            GetNetVersion(netfx45release);
        }

        private void GetNetFx45Details()
        {
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) {
                    if (key == null) {
                        IsValid = false;
                        return;
                    }

                    object objRelease = key.GetValue("Release");
                    if (objRelease == null) return;
                    GetNetVersion((int)objRelease);

                    object objTargetVersion = key.GetValue("TargetVersion");
                    if (objTargetVersion != null) {
                        TargetVersion = new Version(objTargetVersion as string);
                    }

                    object objNetVersion = key.GetValue("Version");
                    if (objNetVersion != null) {
                        NetVersion = new Version(objNetVersion as string);
                    }

                    if (Version == null) {
                        Version = NetVersion.ToString();
                        Description = string.Format(Messages.NetFx45Details, NetVersion, Net45Release);
                    }

                    IsValid = true;
                }
            } catch (SecurityException) {
                IsValid = false;
            } catch (Exception) {
                IsValid = false;
            }
        }

        private void GetNetVersion(int release)
        {
            Net45Release = release;

            NetFxVersion details = NetFxConfig.GetNetFxVersion(release);
            if (details == null) return;
            Version = details.Version;
            Description = details.Description;
        }

        /// <summary>
        /// Gets the release version of .NET 4.5 and later.
        /// </summary>
        /// <value>The release version of .NET 4.5 and later.</value>
        public int Net45Release { get; private set; }

        /// <summary>
        /// Gets the target version for the runtime.
        /// </summary>
        /// <value>The target version for the runtime.</value>
        public Version TargetVersion { get; private set; }

        /// <summary>
        /// Gets the version of .NET 1.0 as read from the registry.
        /// </summary>
        /// <value>The version of .NET as read from the registry.</value>
        public Version NetVersion { get; private set; }

        /// <summary>
        /// Returns <see langword="true"/> if the version information contains valid (even if partially) information.
        /// </summary>
        /// <value><see langword="true"/> if this instance is valid; otherwise, <see langword="false"/>.</value>
        public bool IsValid { get; private set; }

        /// <summary>
        /// Gets the version string for the .NET version installed.
        /// </summary>
        /// <value>The .NET version string.</value>
        public string Version { get; private set; }

        /// <summary>
        /// Gets the description of the .NET version installed.
        /// </summary>
        /// <value>The .NET version description.</value>
        public string Description { get; private set; }
    }
}
