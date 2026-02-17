namespace RJCP.Core.Environment
{
    using System;
#if NETSTANDARD
    using System.Runtime.InteropServices;
#endif

    /// <summary>
    /// Utility class providing OS specific functionality.
    /// </summary>
    public static class Platform
    {
        /// <summary>
        /// Determines whether the operating system is Windows NT or later.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the operating system is Windows NT or later; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool IsWinNT()
        {
#if NETSTANDARD
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif
        }

        /// <summary>
        /// Determines whether the operating system is a supported version of a Unix based system.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the operating system is a supported version of a Unix based system; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The method is meant to be compatible with both the .NET CLR and the MONO framework.
        /// Details of how to detect the platform under MONO can be found at
        /// http://www.mono-project.com/docs/faq/technical/#how-to-detect-the-execution-platform
        /// </remarks>
        public static bool IsUnix()
        {
#if NETSTANDARD
            return
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
            int platform = (int)Environment.OSVersion.Platform;
            return ((platform == 4) || (platform == 6) || (platform == 128));
#endif
        }

        /// <summary>
        /// Gets the path to the system special folder that is identified by the specified enumeration.
        /// </summary>
        /// <param name="folder">An enumerated constant that identifies a system special folder.</param>
        /// <returns>
        /// The path to the specified system special folder, if that folder physically exists on your computer;
        /// otherwise, an empty string ( <see cref="string.Empty"/>). A folder will not physically exist if the operating
        /// system did not create it, the existing folder was deleted, or the folder is a virtual directory, such as
        /// <see cref="Environment.SpecialFolder.MyComputer"/>, which does not correspond to a physical path.
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">The platform is not supported.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="folder"/> is not a member of <see cref="Environment.SpecialFolder"/>.
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// The caller doesn't have permissions to read environment variables.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Unable to determine the location of the home directory.
        /// </exception>
        /// <remarks>
        /// This method can be used in place of the .NET <see cref="Environment.SpecialFolder"/>. On Windows, it will
        /// return the same results. On Linux, it will initially apply the XDG specifications for folder paths, and if
        /// not available, will fall back to <see cref="Environment.SpecialFolder"/>.
        /// </remarks>
        public static string GetFolderPath(Environment.SpecialFolder folder)
        {
            if (IsWinNT()) return Environment.GetFolderPath(folder);
            if (!IsUnix()) throw new PlatformNotSupportedException();

            switch (folder) {
            case Environment.SpecialFolder.LocalApplicationData:
                return Xdg.GetFolderPath(Xdg.SpecialFolder.LocalApplicationData);
            case Environment.SpecialFolder.CommonApplicationData:
                return Xdg.GetFolderPath(Xdg.SpecialFolder.CommonApplicationData);
            default:
                return Environment.GetFolderPath(folder);
            }
        }

        /// <summary>
        /// Determines if the software is running on top of the Mono Common Language Runtime.
        /// </summary>
        /// <returns><see langword="true"/> if running on the Mono CLR; otherwise, <see langword="false"/>.</returns>
        public static bool IsMonoClr()
        {
            return Type.GetType("Mono.Runtime") != null;
        }

        /// <summary>
        /// Determines if the process is started from an MSys Shell
        /// </summary>
        /// <returns>
        /// Returns <see langword="true"/> the system is running from an MSys shell, otherwise, <see langword="false"/>.
        /// </returns>
        public static bool IsMSys()
        {
            if (!IsWinNT()) return false;

            string msystem = Environment.GetEnvironmentVariable("MSYSTEM");
            if (string.IsNullOrWhiteSpace(msystem)) return false;

            return msystem.StartsWith("MIN", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
