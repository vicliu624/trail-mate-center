namespace RJCP.Core.Environment.Native.Win32
{
    internal static partial class Kernel32
    {
        public static class PROCESSOR_ARCHITECTURE
        {
            public const ushort INTEL = 0;
            public const ushort MIPS = 1;
            public const ushort ALPHA = 2;
            public const ushort PPC = 3;
            public const ushort SHX = 4;
            public const ushort ARM = 5;
            public const ushort IA64 = 6;
            public const ushort ALPHA64 = 7;
            public const ushort MSIL = 8;
            public const ushort AMD64 = 9;
            public const ushort ARM64 = 12;
            public const ushort UNKNOWN = 0xFFFF;
        }
    }
}
