namespace RJCP.Core.Environment.Version
{
    using System;

    /// <summary>
    /// Various OS Suite variants.
    /// </summary>
    /// <remarks>This field is derived from <c>OSVERSIONINFOEX.wSuiteMask</c>.</remarks>
    [Flags]
    public enum WinSuite
    {
        /// <summary>
        /// No OS Suite defined.
        /// </summary>
        None = 0,

        /// <summary>
        /// Microsoft Small Business Server was once installed on the system, but may have been upgraded to another
        /// version of Windows. You should not rely upon only the <c>SmallBusiness</c> flag to determine whether Small
        /// Business Server has been installed on the system, as both this flag and the
        /// <see cref="SmallBusinessRestricted"/> flag are set when this product suite is installed. If you upgrade this
        /// installation to Windows Server, Standard Edition, the <see cref="SmallBusinessRestricted"/> flag will be
        /// cleared — however, the <see cref="SmallBusiness"/> flag will remain set. In this case, this indicates that
        /// Small Business Server was once installed on this system. If this installation is further upgraded to Windows
        /// Server, Enterprise Edition, the <see cref="SmallBusiness"/> flag will remain set.
        /// </summary>
        SmallBusiness = 0x00000001,

        /// <summary>
        /// Windows Server 2008 Enterprise, Windows Server 2003, Enterprise Edition, or Windows 2000 Advanced Server is
        /// installed.
        /// </summary>
        Enterprise = 0x00000002,

        /// <summary>
        /// Microsoft BackOffice components are installed.
        /// </summary>
        BackOffice = 0x00000004,

        /// <summary>
        /// TBD: Communications Server.
        /// </summary>
        Communications = 0x00000008,

        /// <summary>
        /// Terminal Services is installed. This value is always set. If <c>Terminal</c> is set but <c>SingleUserTS</c>
        /// is not set, the system is running in application server mode.
        /// </summary>
        Terminal = 0x00000010,

        /// <summary>
        /// Microsoft Small Business Server is installed with the restrictive client license in force.
        /// </summary>
        SmallBusinessRestricted = 0x00000020,

        /// <summary>
        /// Windows XP Embedded is installed.
        /// </summary>
        EmbeddedNT = 0x00000040,

        /// <summary>
        /// Windows Server 2008 Datacenter, Windows Server 2003, Datacenter Edition, or Windows 2000 Datacenter Server
        /// is installed.
        /// </summary>
        Datacenter = 0x00000080,

        /// <summary>
        /// Remote Desktop is supported, but only one interactive session is supported. This value is set unless the
        /// system is running in application server mode.
        /// </summary>
        SingleUserTS = 0x00000100,

        /// <summary>
        /// Windows Vista Home Premium, Windows Vista Home Basic, or Windows XP Home Edition is installed.
        /// </summary>
        Personal = 0x00000200,

        /// <summary>
        /// Windows Server 2003, Web Edition is installed.
        /// </summary>
        Blade = 0x00000400,

        /// <summary>
        /// TBD: Embedded Restricted.
        /// </summary>
        EmbeddedRestricted = 0x00000800,

        /// <summary>
        /// TBD: Security Appliance.
        /// </summary>
        SecurityAppliance = 0x00001000,

        /// <summary>
        /// Windows Storage Server 2003 R2 or Windows Storage Server 2003is installed.
        /// </summary>
        StorageServer = 0x00002000,

        /// <summary>
        /// Windows Server 2003, Compute Cluster Edition is installed.
        /// </summary>
        ComputeServer = 0x00004000,

        /// <summary>
        /// Windows Home Server is installed.
        /// </summary>
        HomeServer = 0x00008000,

        /// <summary>
        /// AppServer mode is enabled.
        /// </summary>
        MultiUserTS = 0x00020000
    }
}
