namespace RJCP.Core.Environment.Version
{
    /// <summary>
    /// List of various platform IDs provided by Microsoft.
    /// </summary>
    public enum WinPlatform
    {
        /// <summary>
        /// Unknown PlatformId.
        /// </summary>
        Unknown = -1,

        /// <summary>
        /// Win32 Subsystem.
        /// </summary>
        Win32s = 0,

        /// <summary>
        /// Windows 9x.
        /// </summary>
        Win9x = 1,

        /// <summary>
        /// Windows NT.
        /// </summary>
        WinNT = 2,

        /// <summary>
        /// Windows Embedded.
        /// </summary>
        WinCE = 3,
    }
}
