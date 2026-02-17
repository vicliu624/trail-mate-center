namespace RJCP.Core.Environment.Version
{
    /// <summary>
    /// The Windows Product Type.
    /// </summary>
    public enum WinProductType
    {
        /// <summary>
        /// Unknown Product Type.
        /// </summary>
        Unknown = -1,

        /// <summary>
        /// Windows 9x, 32-bit, referring to Windows 95, 95OSR2, 98, 98SE, or ME.
        /// </summary>
        Win9x = 0,

        /// <summary>
        /// The operating system is Windows 8, Windows 7, Windows Vista, Windows XP Professional, Windows XP Home
        /// Edition, or Windows 2000 Professional.
        /// </summary>
        Workstation = 1,

        /// <summary>
        /// The system is a domain controller and the operating system is Windows Server 2012 , Windows Server 2008 R2,
        /// Windows Server 2008, Windows Server 2003, or Windows 2000 Server.
        /// </summary>
        DomainController = 2,

        /// <summary>
        /// The operating system is Windows Server 2012, Windows Server 2008 R2, Windows Server 2008, Windows Server
        /// 2003, or Windows 2000 Server. Note that a server that is also a domain controller is reported as
        /// VER_NT_DOMAIN_CONTROLLER, not VER_NT_SERVER.
        /// </summary>
        Server = 3,
    }
}
