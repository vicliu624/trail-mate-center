namespace RJCP.Native.Win32
{
    using System.Runtime.InteropServices;

    internal static partial class Kernel32
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class OSVERSIONINFOEX
        {
            public int OSVersionInfoSize;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x80)]
            public string CSDVersion;
            public short ServicePackMajor;
            public short ServicePackMinor;
            public ushort SuiteMask;
            public byte ProductType;
            public byte Reserved;

            public OSVERSIONINFOEX()
            {
                OSVersionInfoSize = Marshal.SizeOf(this);
            }
        }
    }
}
