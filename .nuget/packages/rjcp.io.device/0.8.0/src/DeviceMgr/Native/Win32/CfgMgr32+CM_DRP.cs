namespace RJCP.Native.Win32
{
    internal static partial class CfgMgr32
    {
        public enum CM_DRP
        {
            DEVICEDESC = 0x00000001,                  // DeviceDesc REG_SZ property (RW)
            HARDWAREID = 0x00000002,                  // HardwareID REG_MULTI_SZ property (RW)
            COMPATIBLEIDS = 0x00000003,               // CompatibleIDs REG_MULTI_SZ property (RW)
            UNUSED0 = 0x00000004,                     // unused
            SERVICE = 0x00000005,                     // Service REG_SZ property (RW)
            UNUSED1 = 0x00000006,                     // unused
            UNUSED2 = 0x00000007,                     // unused
            CLASS = 0x00000008,                       // Class REG_SZ property (RW)
            CLASSGUID = 0x00000009,                   // ClassGUID REG_SZ property (RW)
            DRIVER = 0x0000000A,                      // Driver REG_SZ property (RW)
            CONFIGFLAGS = 0x0000000B,                 // ConfigFlags REG_DWORD property (RW)
            MFG = 0x0000000C,                         // Mfg REG_SZ property (RW)
            FRIENDLYNAME = 0x0000000D,                // FriendlyName REG_SZ property (RW)
            LOCATION_INFORMATION = 0x0000000E,        // LocationInformation REG_SZ property (RW)
            PHYSICAL_DEVICE_OBJECT_NAME = 0x0000000F, // PhysicalDeviceObjectName REG_SZ property (R)
            CAPABILITIES = 0x00000010,                // Capabilities REG_DWORD property (R)
            UI_NUMBER = 0x00000011,                   // UiNumber REG_DWORD property (R)
            UPPERFILTERS = 0x00000012,                // UpperFilters REG_MULTI_SZ property (RW)
            LOWERFILTERS = 0x00000013,
            BUSTYPEGUID = 0x00000014,                 // Bus Type Guid, GUID, (R)
            LEGACYBUSTYPE = 0x00000015,               // Legacy bus type, INTERFACE_TYPE, (R)
            BUSNUMBER = 0x00000016,                   // Bus Number, DWORD, (R)
            ENUMERATOR_NAME = 0x00000017,             // Enumerator Name REG_SZ property (R)
            SECURITY = 0x00000018,                    // Security - Device override (RW)
            SECURITY_SDS = 0x00000019,                // Security - Device override (RW)
            DEVTYPE = 0x0000001A,                     // Device Type - Device override (RW)
            EXCLUSIVE = 0x0000001B,                   // Exclusivity - Device override (RW)
            CHARACTERISTICS = 0x0000001C,             // Characteristics - Device Override (RW)
            ADDRESS = 0x0000001D,                     // Device Address (R)
            UI_NUMBER_DESC_FORMAT = 0x0000001E,       // UINumberDescFormat REG_SZ property (RW)

            // WinXP and later
            DEVICE_POWER_DATA = 0x0000001F,           // CM_POWER_DATA REG_BINARY property (R)
            REMOVAL_POLICY = 0x00000020,              // CM_DEVICE_REMOVAL_POLICY REG_DWORD (R)
            REMOVAL_POLICY_HW_DEFAULT = 0x00000021,   // CM_DRP_REMOVAL_POLICY_HW_DEFAULT REG_DWORD (R)
            REMOVAL_POLICY_OVERRIDE = 0x00000022,     // CM_DRP_REMOVAL_POLICY_OVERRIDE REG_DWORD (RW)
            INSTALL_STATE = 0x00000023,               // CM_DRP_INSTALL_STATE REG_DWORD (R)

            // Windows 2003 and later
            LOCATION_PATHS = 0x00000024,              // CM_DRP_LOCATION_PATHS REG_MULTI_SZ (R)

            // Windows 7 and later
            BASE_CONTAINERID = 0x00000025             // Base ContainerID REG_SZ property (R)
        }
    }
}
