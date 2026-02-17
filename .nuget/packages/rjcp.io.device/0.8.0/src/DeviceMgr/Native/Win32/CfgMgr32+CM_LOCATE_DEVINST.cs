namespace RJCP.Native.Win32
{
    internal static partial class CfgMgr32
    {
        public enum CM_LOCATE_DEVINST
        {
            NORMAL = 0x00000000,
            PHANTOM = 0x00000001,
            CANCELREMOVE = 0x00000002,
            NOVALIDATION = 0x00000004,
            BITS = 0x00000007
        }
    }
}
