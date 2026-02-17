namespace RJCP.Native.Win32
{
    internal static partial class Kernel32
    {
        public enum REG_DATATYPE
        {
            REG_NONE = 0, // No value type
            REG_SZ = 1, // Unicode nul terminated string
            REG_EXPAND_SZ = 2, // Unicode nul terminated string
            REG_BINARY = 3, // Free form binary
            REG_DWORD = 4, // 32-bit number
            REG_DWORD_LITTLE_ENDIAN = 4, // 32-bit number (same as REG_DWORD)
            REG_DWORD_BIG_ENDIAN = 5, // 32-bit number
            REG_LINK = 6, // Symbolic Link (unicode)
            REG_MULTI_SZ = 7, // Multiple Unicode strings
            REG_RESOURCE_LIST = 8, // Resource list in the resource map
            REG_FULL_RESOURCE_DESCRIPTOR = 9, // Resource list in the hardware description
            REG_RESOURCE_REQUIREMENTS_LIST = 10,
            REG_QWORD = 11, // 64-bit number
            REG_QWORD_LITTLE_ENDIAN = 11, // 64-bit number (same as REG_QWORD)
        }
    }
}
