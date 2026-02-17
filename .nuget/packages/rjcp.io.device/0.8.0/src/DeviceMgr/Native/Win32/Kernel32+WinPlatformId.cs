namespace RJCP.Native.Win32
{
    internal static partial class Kernel32
    {
        public enum WinPlatformId
        {
            Unknown = -1,
            Win32s = 0,
            Win9x = 1,
            WinNT = 2,
            WinCE = 3,
        }
    }
}
