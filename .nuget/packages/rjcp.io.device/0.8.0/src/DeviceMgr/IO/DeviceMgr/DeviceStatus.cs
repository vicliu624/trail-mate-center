namespace RJCP.IO.DeviceMgr
{
    using System;
    using System.Runtime.CompilerServices;
    using Native.Win32;

    /// <summary>
    /// Contains the Device Status.
    /// </summary>
    /// <remarks>
    /// The device status is retrieved from the Config Manager API (see cfg.h). It is also dependent on the running
    /// Operating System. As the codes are dependent also on the Operating System being compiled (due to reuse of some
    /// of the codes), some of the older codes might not be available on newer Operating Systems. This list uses Windows
    /// XP as a baseline.
    /// </remarks>
    [Flags]
    public enum DeviceStatus : long
    {
        /// <summary>
        /// No device status is available.
        /// </summary>
        None = 0,

        /// <summary>
        /// Was enumerated by ROOT.
        /// </summary>
        RootEnumerated = 0x00000001,

        /// <summary>
        /// Has Register_Device_Driver.
        /// </summary>
        DriverLoaded = 0x00000002,

        /// <summary>
        /// Has Register_Enumerator.
        /// </summary>
        EnumLoaded = 0x00000004,

        /// <summary>
        /// Is currently configured.
        /// </summary>
        Started = 0x00000008,

        /// <summary>
        /// Manually installed.
        /// </summary>
        ManuallyInstalled = 0x00000010,

        /// <summary>
        /// May need reenumeration.
        /// </summary>
        NeedsEnumeration = 0x00000020,

        /// <summary>
        /// One or more drivers are blocked from loading for this Devnode.
        /// </summary>
        DriverBlocked = 0x00000040,

        /// <summary>
        /// Enum generates hardware ID.
        /// </summary>
        HardwareEnum = 0x00000080,

        /// <summary>
        /// System needs to be restarted for this device to work properly.
        /// </summary>
        NeedRestart = 0x00000100,

        /// <summary>
        /// One or more children have invalid ID(s).
        /// </summary>
        ChildWithInvalidId = 0x00000200,

        /// <summary>
        /// Need device installer.
        /// </summary>
        HasProblem = 0x00000400,

        /// <summary>
        /// Is filtered.
        /// </summary>
        Filtered = 0x00000800,

        /// <summary>
        /// This device is using a legacy driver.
        /// </summary>
        LegacyDriver = 0x00001000,

        /// <summary>
        /// Can be disabled.
        /// </summary>
        Disableable = 0x00002000,

        /// <summary>
        /// Can be removed.
        /// </summary>
        Removable = 0x00004000,

        /// <summary>
        /// Has a private problem.
        /// </summary>
        PrivateProblem = 0x00008000,

        /// <summary>
        /// Multi function parent. Up until Windows 8.x.
        /// </summary>
        MultiFunctionParent = 0x00010000,

        /// <summary>
        /// Multi function child. Up until Windows 8.x.
        /// </summary>
        MultiFunctionChild = 0x00020000,

        /// <summary>
        /// Device is being removed.
        /// </summary>
        WillBeRemoved = 0x00040000,

        /// <summary>
        /// Has received a config enumerate.
        /// </summary>
        NotFirstTimeEnum = 0x00080000,

        /// <summary>
        /// When child is stopped, free resources.
        /// </summary>
        StopFreeResources = 0x00100000,

        /// <summary>
        /// Don't skip during rebalance.
        /// </summary>
        RebalanceCandidate = 0x00200000,

        /// <summary>
        /// This device's log_confs do not have same resources.
        /// </summary>
        BadPartial = 0x00400000,

        /// <summary>
        /// This device is an NT enumerator.
        /// </summary>
        NtEnumerator = 0x00800000,

        /// <summary>
        /// This device is an NT driver.
        /// </summary>
        NtDriver = 0x01000000,

        /// <summary>
        /// Device need lock resume processing. This is up to Windows 7.
        /// </summary>
        NeedsLocking = 0x02000000,

        /// <summary>
        /// Device can be the wakeup device.
        /// </summary>
        ArmWakeup = 0x04000000,

        /// <summary>
        /// APM aware enumerator.
        /// </summary>
        ApmEnumerator = 0x08000000,

        /// <summary>
        /// APM aware driver.
        /// </summary>
        ApmDriver = 0x10000000,

        /// <summary>
        /// Silent install.
        /// </summary>
        SilentInstall = 0x20000000,

        /// <summary>
        /// No show in device manager.
        /// </summary>
        NoShowInDevMgr = 0x40000000,

        /// <summary>
        ///  Had a problem during preassignment of boot log conf.
        /// </summary>
        BootLogProblem = 0x80000000,

        /// <summary>
        /// The function driver for a device reported that the device is not connected. Typically this means a wireless
        /// device is out of range. This is in Windows 8 and later.
        /// </summary>
        DeviceDisconnected = 0x102000000,

        /// <summary>
        /// Device is part of a set of related devices collectively pending query-removal. This is in Windows 10 and
        /// later.
        /// </summary>
        QueryRemovePending = 0x100010000,

        /// <summary>
        /// Device is actively engaged in a query-remove IRP. This is in Windows 10 and later.
        /// </summary>
        QueryRemoveActive = 0x100020000
    }

    internal static class DeviceStatusConvert
    {
#if NETSTANDARD
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static bool IsSet(CfgMgr32.DN_STATUS status, CfgMgr32.DN_STATUS bit)
        {
            return unchecked((int)status & (int)bit) != 0;
        }

        public static DeviceStatus Get(CfgMgr32.DN_STATUS status)
        {
            DeviceStatus result = DeviceStatus.None;

            // Microsoft have repurposed some bits for newer operating systems. So we must detect the OS version and set
            // the flags based on the reported OS version. This algorithm is simple as there is not more than 1 rewrite
            // of a bit's meaning.

            Version osVer = Kernel32.GetOsVersion();

            if (IsSet(status, CfgMgr32.DN_STATUS.ROOT_ENUMERATED)) result |= DeviceStatus.RootEnumerated;
            if (IsSet(status, CfgMgr32.DN_STATUS.DRIVER_LOADED)) result |= DeviceStatus.DriverLoaded;
            if (IsSet(status, CfgMgr32.DN_STATUS.ENUM_LOADED)) result |= DeviceStatus.EnumLoaded;
            if (IsSet(status, CfgMgr32.DN_STATUS.STARTED)) result |= DeviceStatus.Started;
            if (IsSet(status, CfgMgr32.DN_STATUS.MANUAL)) result |= DeviceStatus.ManuallyInstalled;
            if (IsSet(status, CfgMgr32.DN_STATUS.NEED_TO_ENUM)) result |= DeviceStatus.NeedsEnumeration;
            if (IsSet(status, CfgMgr32.DN_STATUS.DRIVER_BLOCKED)) result |= DeviceStatus.DriverBlocked;              // NOT_FIRST_TIME is replaced by NT4/2k/XP
            if (IsSet(status, CfgMgr32.DN_STATUS.HARDWARE_ENUM)) result |= DeviceStatus.HardwareEnum;
            if (IsSet(status, CfgMgr32.DN_STATUS.NEED_RESTART)) result |= DeviceStatus.NeedRestart;                  // LIAR is replaced by NT4/2k/XP
            if (IsSet(status, CfgMgr32.DN_STATUS.CHILD_WITH_INVALID_ID)) result |= DeviceStatus.ChildWithInvalidId;  // HAS_MARK is replaced by NT4/2k/XP
            if (IsSet(status, CfgMgr32.DN_STATUS.HAS_PROBLEM)) result |= DeviceStatus.HasProblem;
            if (IsSet(status, CfgMgr32.DN_STATUS.FILTERED)) result |= DeviceStatus.Filtered;
            if (IsSet(status, CfgMgr32.DN_STATUS.LEGACY_DRIVER)) result |= DeviceStatus.LegacyDriver;                // MOVED is replaced by NT4/2k/XP
            if (IsSet(status, CfgMgr32.DN_STATUS.DISABLEABLE)) result |= DeviceStatus.Disableable;
            if (IsSet(status, CfgMgr32.DN_STATUS.REMOVABLE)) result |= DeviceStatus.Removable;
            if (IsSet(status, CfgMgr32.DN_STATUS.PRIVATE_PROBLEM)) result |= DeviceStatus.PrivateProblem;
            if (IsSet(status, CfgMgr32.DN_STATUS.WILL_BE_REMOVED)) result |= DeviceStatus.WillBeRemoved;
            if (IsSet(status, CfgMgr32.DN_STATUS.NOT_FIRST_TIMEE)) result |= DeviceStatus.NotFirstTimeEnum;
            if (IsSet(status, CfgMgr32.DN_STATUS.STOP_FREE_RES)) result |= DeviceStatus.StopFreeResources;
            if (IsSet(status, CfgMgr32.DN_STATUS.REBAL_CANDIDATE)) result |= DeviceStatus.RebalanceCandidate;
            if (IsSet(status, CfgMgr32.DN_STATUS.BAD_PARTIAL)) result |= DeviceStatus.BadPartial;
            if (IsSet(status, CfgMgr32.DN_STATUS.NT_ENUMERATOR)) result |= DeviceStatus.NtEnumerator;
            if (IsSet(status, CfgMgr32.DN_STATUS.NT_DRIVER)) result |= DeviceStatus.NtDriver;
            if (IsSet(status, CfgMgr32.DN_STATUS.ARM_WAKEUP)) result |= DeviceStatus.ArmWakeup;
            if (IsSet(status, CfgMgr32.DN_STATUS.APM_ENUMERATOR)) result |= DeviceStatus.ApmEnumerator;
            if (IsSet(status, CfgMgr32.DN_STATUS.APM_DRIVER)) result |= DeviceStatus.ApmDriver;
            if (IsSet(status, CfgMgr32.DN_STATUS.SILENT_INSTALL)) result |= DeviceStatus.SilentInstall;
            if (IsSet(status, CfgMgr32.DN_STATUS.NO_SHOW_IN_DM)) result |= DeviceStatus.NoShowInDevMgr;
            if (IsSet(status, CfgMgr32.DN_STATUS.BOOT_LOG_PROB)) result |= DeviceStatus.BootLogProblem;

            // Windows 8
            // * DEVICE_DISCONNECTED == NEEDS_LOCKING
            if (!Kernel32.IsWin8OrNewer(osVer)) {
                if (IsSet(status, CfgMgr32.DN_STATUS.NEEDS_LOCKING)) result |= DeviceStatus.NeedsLocking;
            } else {
                if (IsSet(status, CfgMgr32.DN_STATUS.DEVICE_DISCONNECTED)) result |= DeviceStatus.DeviceDisconnected;
            }

            // Windows 10
            // * QUERY_REMOVE_PENDING == MF_PARENT
            // * QUERY_REMOVE_ACTIVE == MF_CHILD
            if (!Kernel32.IsWin10OrNewer(osVer)) {
                if (IsSet(status, CfgMgr32.DN_STATUS.MF_PARENT)) result |= DeviceStatus.MultiFunctionParent;
                if (IsSet(status, CfgMgr32.DN_STATUS.MF_CHILD)) result |= DeviceStatus.MultiFunctionChild;
            } else {
                if (IsSet(status, CfgMgr32.DN_STATUS.QUERY_REMOVE_PENDING)) result |= DeviceStatus.QueryRemovePending;
                if (IsSet(status, CfgMgr32.DN_STATUS.QUERY_REMOVE_ACTIVE)) result |= DeviceStatus.QueryRemoveActive;
            }

            return result;
        }
    }
}
