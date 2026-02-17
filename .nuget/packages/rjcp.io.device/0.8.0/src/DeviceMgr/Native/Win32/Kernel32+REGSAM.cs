namespace RJCP.Native.Win32
{
    using System;

    internal static partial class Kernel32
    {
        [Flags]
        public enum REGSAM
        {
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            SYNCHRONIZE = 0x00100000,
            STANDARD_RIGHTS_REQUIRED = 0x000F0000,
            STANDARD_RIGHTS_READ = READ_CONTROL,
            STANDARD_RIGHTS_WRITE = READ_CONTROL,
            STANDARD_RIGHTS_EXECUTE = READ_CONTROL,
            STANDARD_RIGHTS_ALL = 0x001F0000,

            KEY_QUERY_VALUE = 0x0001,
            KEY_SET_VALUE = 0x0002,
            KEY_CREATE_SUB_KEY = 0x0004,
            KEY_ENUMERATE_SUB_KEYS = 0x0008,
            KEY_NOTIFY = 0x0010,
            KEY_CREATE_LINK = 0x0020,
            KEY_WOW64_32KEY = 0x0200,
            KEY_WOW64_64KEY = 0x0100,
            KEY_WOW64_RES = 0x0300,

            KEY_READ = (STANDARD_RIGHTS_READ | KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS | KEY_NOTIFY) & (~SYNCHRONIZE),
            KEY_WRITE = (STANDARD_RIGHTS_WRITE | KEY_SET_VALUE | KEY_CREATE_SUB_KEY) & (~SYNCHRONIZE),
            KEY_EXECUTE = KEY_READ & (~SYNCHRONIZE),
            KEY_ALL_ACCESS = (STANDARD_RIGHTS_ALL | KEY_QUERY_VALUE | KEY_SET_VALUE | KEY_CREATE_SUB_KEY | KEY_ENUMERATE_SUB_KEYS | KEY_NOTIFY | KEY_CREATE_LINK) & (~SYNCHRONIZE)
        }
    }
}
