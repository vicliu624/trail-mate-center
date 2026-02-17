namespace RJCP.Core.Environment.Version.NetFx.Runtime
{
    using System;
    using System.Reflection;

    /// <summary>
    /// An instance checking if the .NET Run Time is MONO on Windows or Unix.
    /// </summary>
    public sealed class Mono : INetVersion
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Mono"/> class.
        /// </summary>
        internal Mono()
        {
            GetMonoVersion();
        }

        private void GetMonoVersion()
        {
            Type monoType = Type.GetType("Mono.Runtime");
            if (monoType == null) {
                IsValid = false;
                return;
            }

            Version = Environment.Version.ToString();

            MethodInfo displayName = monoType.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
            if (displayName != null) {
                Description = string.Format("Mono {0}", displayName.Invoke(null, null));
            }

            IsValid = true;
        }

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
