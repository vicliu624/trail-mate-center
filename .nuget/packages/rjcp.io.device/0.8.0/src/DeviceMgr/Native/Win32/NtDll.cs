namespace RJCP.Native.Win32
{
    using System.Runtime.InteropServices;
    using System.Security;

    [SuppressUnmanagedCodeSecurity]
    internal static partial class NtDll
    {
        [DllImport("ntdll.dll", SetLastError = false, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int RtlGetVersion([In, Out] Kernel32.OSVERSIONINFOEX osVersionInfoEx);
    }
}
