namespace RJCP.Native.Win32
{
    using System;

    internal static partial class CfgMgr32
    {
        [Flags]
        public enum DN_STATUS : uint
        {
            ROOT_ENUMERATED = 0x00000001,          // Was enumerated by ROOT
            DRIVER_LOADED = 0x00000002,            // Has Register_Device_Driver
            ENUM_LOADED = 0x00000004,              // Has Register_Enumerator
            STARTED = 0x00000008,                  // Is currently configured
            MANUAL = 0x00000010,                   // Manually installed
            NEED_TO_ENUM = 0x00000020,             // May need reenumeration
            NOT_FIRST_TIME = 0x00000040,           // Has received a config
            HARDWARE_ENUM = 0x00000080,            // Enum generates hardware ID
            LIAR = 0x00000100,                     // Lied about can reconfig once
            HAS_MARK = 0x00000200,                 // Not CM_Create_DevInst lately
            HAS_PROBLEM = 0x00000400,              // Need device installer
            FILTERED = 0x00000800,                 // Is filtered
            MOVED = 0x00001000,                    // Has been moved
            DISABLEABLE = 0x00002000,              // Can be disabled
            REMOVABLE = 0x00004000,                // Can be removed
            PRIVATE_PROBLEM = 0x00008000,          // Has a private problem
            MF_PARENT = 0x00010000,                // Multi function parent
            MF_CHILD = 0x00020000,                 // Multi function child
            WILL_BE_REMOVED = 0x00040000,          // DevInst is being removed

            NOT_FIRST_TIMEE = 0x00080000,          // S: Has received a config enumerate
            STOP_FREE_RES = 0x00100000,            // S: When child is stopped, free resources
            REBAL_CANDIDATE = 0x00200000,          // S: Don't skip during rebalance
            BAD_PARTIAL = 0x00400000,              // S: This devnode's log_confs do not have same resources
            NT_ENUMERATOR = 0x00800000,            // S: This devnode's is an NT enumerator
            NT_DRIVER = 0x01000000,                // S: This devnode's is an NT driver

            NEEDS_LOCKING = 0x02000000,            // S: Devnode need lock resume processing
            ARM_WAKEUP = 0x04000000,               // S: Devnode can be the wakeup device
            APM_ENUMERATOR = 0x08000000,           // S: APM aware enumerator
            APM_DRIVER = 0x10000000,               // S: APM aware driver
            SILENT_INSTALL = 0x20000000,           // S: Silent install
            NO_SHOW_IN_DM = 0x40000000,            // S: No show in device manager
            BOOT_LOG_PROB = 0x80000000,            // S: Had a problem during preassignment of boot log conf

            // Windows 2000
            NEED_RESTART = LIAR,                   // System needs to be restarted for this Devnode to work properly

            // Windows XP
            DRIVER_BLOCKED = NOT_FIRST_TIME,       // One or more drivers are blocked from loading for this Devnode
            LEGACY_DRIVER = MOVED,                 // This device is using a legacy driver
            CHILD_WITH_INVALID_ID = HAS_MARK,      // One or more children have invalid ID(s)

            // Windows 8
            DEVICE_DISCONNECTED = NEEDS_LOCKING,   // The function driver for a device reported that the device is not connected.  Typically this means a wireless device is out of range.

            // Windows 10
            QUERY_REMOVE_PENDING = MF_PARENT,      // Device is part of a set of related devices collectively pending query-removal
            QUERY_REMOVE_ACTIVE = MF_CHILD         // Device is actively engaged in a query-remove IRP
        }
    }
}
