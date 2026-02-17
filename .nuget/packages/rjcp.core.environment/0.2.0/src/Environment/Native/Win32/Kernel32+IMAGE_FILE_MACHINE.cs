namespace RJCP.Core.Environment.Native.Win32
{
    internal static partial class Kernel32
    {
        public static class IMAGE_FILE_MACHINE
        {
            public const ushort UNKNOWN = 0x0000;
            public const ushort TARGET_HOST = 0x0001;
            public const ushort I386 = 0x014c;
            public const ushort MIPS_R3000_BE = 0x0160;
            public const ushort MIPS_R3000 = 0x0162;
            public const ushort MIPS_R4000 = 0x0166;
            public const ushort MIPS_R10000 = 0x0168;
            public const ushort MIPS_WCEMIPSV2 = 0x0169;
            public const ushort ALPHA = 0x0184;
            public const ushort SH3 = 0x01a2;
            public const ushort SH3DSP = 0x01a3;
            public const ushort SH3E = 0x01a4;
            public const ushort SH4 = 0x01a6;
            public const ushort SH5 = 0x01a8;
            public const ushort ARM = 0x01c0;
            public const ushort ARM_THUMB = 0x01c2;
            public const ushort ARMNT = 0x01c4;
            public const ushort AM33 = 0x1d3;
            public const ushort POWERPC = 0x01f0;
            public const ushort POWERPCFP = 0x01f1;
            public const ushort IA64 = 0x200;
            public const ushort MIPS16 = 0x0266;
            public const ushort ALPHA64 = 0x0284;
            public const ushort MIPSFPU = 0x0366;
            public const ushort MIPSFPU16 = 0x0466;
            public const ushort TRICORE = 0x0520;
            public const ushort CEF = 0x0cef;
            public const ushort EBC = 0x0ebc;
            public const ushort AMD64 = 0x8664;
            public const ushort M32R = 0x9041;
            public const ushort ARM64 = 0xAA64;
            public const ushort CEE = 0xc0ee;
        }
    }
}
