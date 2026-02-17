namespace RJCP.Core.Environment.Native.Win32
{
    using System.Runtime.InteropServices;
    using System.Security;

    [SuppressUnmanagedCodeSecurity]
    internal static class WinBrand
    {
        [DllImport("winbrand.dll", CharSet = CharSet.Unicode)]
#if NET45_OR_GREATER
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
#endif
        public static extern string BrandingFormatString(string format);
    }
}
