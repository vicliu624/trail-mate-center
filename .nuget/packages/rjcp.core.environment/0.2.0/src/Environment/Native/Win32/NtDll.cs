namespace RJCP.Core.Environment.Native.Win32
{
    using System.Runtime.InteropServices;
    using System.Security;

    [SuppressUnmanagedCodeSecurity]
    internal static partial class NtDll
    {
        [DllImport("ntdll.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        public static extern int RtlGetVersion([In, Out] Kernel32.OSVERSIONINFO osVersionInfo);

        [DllImport("ntdll.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        public static extern int RtlGetVersion([In, Out] Kernel32.OSVERSIONINFOEX osVersionInfoEx);
    }
}
