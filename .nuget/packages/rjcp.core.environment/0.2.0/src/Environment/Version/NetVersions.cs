namespace RJCP.Core.Environment.Version
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Security;
    using Microsoft.Win32;
    using NetFx.Runtime;

    /// <summary>
    /// A class to get the versions of .NET installed on the local computer. Only relevant for Windows.
    /// </summary>
    public sealed class NetVersions : IEnumerable<INetVersion>
    {
        private readonly List<INetVersion> m_Installed = new List<INetVersion>();

        /// <summary>
        /// Initializes a new instance of the <see cref="NetVersions"/> class. Other operating systems will just list no
        /// versions of .NET (until there might be support at a later date).
        /// </summary>
        public NetVersions()
        {
            if (Platform.IsWinNT()) {
                FindNetFxVersions();
            }
        }

        private void FindNetFxVersions()
        {
            FindNetFx10();
            FindNetFxLegacy();

            try {
                NetFx.NetFx45 version45 = new NetFx.NetFx45();
                if (version45.IsValid) m_Installed.Add(version45);
            } catch (SecurityException) {
                /* Ignore */
            }
        }

        private void FindNetFx10()
        {
            try {
                using (RegistryKey netKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\Full\")) {
                    if (netKey == null) return;
                    foreach (string versionKeyName in netKey.GetSubKeyNames()) {
                        if (versionKeyName.StartsWith("v")) {
                            RegistryKey versionKey = netKey.OpenSubKey(versionKeyName);
                            foreach (string langCode in versionKey.GetSubKeyNames()) {
                                RegistryKey langKey = versionKey.OpenSubKey(langCode);
                                foreach (string product in langKey.GetSubKeyNames()) {
                                    RegistryKey productKey = langKey.OpenSubKey(product);
                                    if (IsInstalled(productKey)) {
                                        NetFx.NetFx10 net10 = new NetFx.NetFx10(versionKeyName, product);
                                        if (net10.IsValid) m_Installed.Add(net10);
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (SecurityException) {
                /* Ignore */
            }
        }

        // See https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed
        private void FindNetFxLegacy()
        {
            string netKey;
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess) {
                netKey = @"SOFTWARE\Wow6432Node\Microsoft\NET Framework Setup\NDP\";
            } else {
                netKey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\";
            }

            try {
                using (RegistryKey ndpKey = Registry.LocalMachine.OpenSubKey(netKey)) {
                    if (ndpKey == null) return;
                    foreach (string versionKeyName in ndpKey.GetSubKeyNames()) {
                        if (versionKeyName.StartsWith("v")) {
                            RegistryKey versionKey = ndpKey.OpenSubKey(versionKeyName);

                            string defaultValue = versionKey.GetValue(null, "").ToString();
                            if (defaultValue != null && defaultValue.Equals("deprecated")) return;

                            if (IsInstalled(versionKey)) {
                                NetFx.NetFxLegacy netfx = new NetFx.NetFxLegacy(versionKeyName);
                                if (netfx.IsValid) m_Installed.Add(netfx);
                                continue;
                            }
                            foreach (string subKeyName in versionKey.GetSubKeyNames()) {
                                RegistryKey subKey = versionKey.OpenSubKey(subKeyName);
                                if (IsInstalled(subKey)) {
                                    string path = string.Format(@"{0}\{1}", versionKeyName, subKeyName);
                                    NetFx.NetFxLegacy netfx = new NetFx.NetFxLegacy(path);
                                    if (netfx.IsValid) {
                                        m_Installed.Add(netfx);
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (SecurityException) {
                /* Ignore */
            }
        }

        private static bool IsInstalled(RegistryKey key)
        {
            string install = key.GetValue("Install", "").ToString();
            return install.Equals("1");
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the collection of .NET versions found.</returns>
        public IEnumerator<INetVersion> GetEnumerator()
        {
            return m_Installed.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static readonly object RunTimeLock = new object();
        private static List<INetVersion> RunTimes;

        /// <summary>
        /// Gets the detected .NET runtimes.
        /// </summary>
        /// <value>The current .NET runtimes.</value>
        public static IEnumerable<INetVersion> Runtime
        {
            get
            {
                if (RunTimes == null) {
                    lock (RunTimeLock) {
                        if (RunTimes == null) {
                            RunTimes = new List<INetVersion> {
                                new NetRuntime()
                            };

                            INetVersion mono = new Mono();
                            if (mono.IsValid) RunTimes.Add(mono);
                        }
                    }
                }
                return RunTimes;
            }
        }
    }
}
