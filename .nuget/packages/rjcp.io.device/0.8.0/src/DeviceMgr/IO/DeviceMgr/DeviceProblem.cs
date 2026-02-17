namespace RJCP.IO.DeviceMgr
{
    /// <summary>
    /// Known Device Problems.
    /// </summary>
    /// <remarks>
    /// Obtained from 'cfg.h' from the Windows SDK.
    /// </remarks>
    public enum DeviceProblem
    {

        /// <summary>
        /// No problem identified.
        /// </summary>
        None = 0,

        /// <summary>
        /// No config for device.
        /// </summary>
        NotConfigured = 0x00000001,

        /// <summary>
        /// Service load failed.
        /// </summary>
        DevLoaderFailed = 0x00000002,

        /// <summary>
        /// Out of memory.
        /// </summary>
        OutOfMemory = 0x00000003,

        /// <summary>
        /// 
        /// </summary>
        EntryIsWrongType = 0x00000004,

        /// <summary>
        /// 
        /// </summary>
        LackedArbitrator = 0x00000005,

        /// <summary>
        /// Boot config conflict.
        /// </summary>
        BootConfigConflict = 0x00000006,

        /// <summary>
        /// 
        /// </summary>
        FailedFilter = 0x00000007,

        /// <summary>
        /// Devloader not found.
        /// </summary>
        DevLoaderNotFound = 0x00000008,

        /// <summary>
        /// Invalid ID.
        /// </summary>
        InvalidData = 0x00000009,

        /// <summary>
        /// 
        /// </summary>
        FailedStart = 0x0000000A,

        /// <summary>
        /// 
        /// </summary>
        Liar = 0x0000000B,

        /// <summary>
        /// Config conflict.
        /// </summary>
        NormalConflict = 0x0000000C,

        /// <summary>
        /// 
        /// </summary>
        NotVerified = 0x0000000D,

        /// <summary>
        /// Requires restart.
        /// </summary>
        NeedRestart = 0x0000000E,

        /// <summary>
        /// 
        /// </summary>
        Reenumeration = 0x0000000F,

        /// <summary>
        /// 
        /// </summary>
        PartialLogConf = 0x00000010,

        /// <summary>
        /// Unknown resource type.
        /// </summary>
        UnknownResource = 0x00000011,

        /// <summary>
        /// 
        /// </summary>
        Reinstall = 0x00000012,

        /// <summary>
        /// 
        /// </summary>
        Registry = 0x00000013,

        /// <summary>
        /// WINDOWS 95 ONLY.
        /// </summary>
        VXDLDR = 0x00000014,

        /// <summary>
        /// Device will be removed.
        /// </summary>
        WillBeRemoved = 0x00000015,

        /// <summary>
        /// Device is disabled.
        /// </summary>
        Disabled = 0x00000016,

        /// <summary>
        /// Dev loader not ready.
        /// </summary>
        DevLoaderNotReady = 0x00000017,

        /// <summary>
        /// Device doesn't exist.
        /// </summary>
        DeviceNotThere = 0x00000018,

        /// <summary>
        /// 
        /// </summary>
        Moved = 0x00000019,

        /// <summary>
        /// 
        /// </summary>
        TooEarly = 0x0000001A,

        /// <summary>
        /// No valid log config.
        /// </summary>
        NoValidLogConf = 0x0000001B,

        /// <summary>
        /// Install failed.
        /// </summary>
        FailedInstall = 0x0000001C,

        /// <summary>
        /// Device disabled.
        /// </summary>
        HardwareDisabled = 0x0000001D,

        /// <summary>
        /// Can't share IRQ.
        /// </summary>
        CantShareIrq = 0x0000001E,

        /// <summary>
        /// Driver failed add.
        /// </summary>
        FailedAdd = 0x0000001F,

        /// <summary>
        /// Service's start is 4.
        /// </summary>
        DisabledService = 0x00000020,

        /// <summary>
        /// Resource translation failed.
        /// </summary>
        TranslationFailed = 0x00000021,

        /// <summary>
        /// No soft configuration.
        /// </summary>
        NoSoftConfig = 0x00000022,

        /// <summary>
        /// Device missing in BIOS table.
        /// </summary>
        BiosTable = 0x00000023,

        /// <summary>
        /// IRQ translator failed.
        /// </summary>
        IrqTranslationFailed = 0x00000024,

        /// <summary>
        /// DriverEntry() failed.
        /// </summary>
        FailedDriverEntry = 0x00000025,

        /// <summary>
        /// Driver should have unloaded.
        /// </summary>
        DriverFailedPrioUnload = 0x00000026,

        /// <summary>
        /// Driver load unsuccessful.
        /// </summary>
        DriverFailedLoad = 0x00000027,

        /// <summary>
        /// Error accessing driver's service key.
        /// </summary>
        DriverServiceKeyInvalid = 0x00000028,

        /// <summary>
        /// Loaded legacy service created no devices.
        /// </summary>
        LegacyServiceNoDevices = 0x00000029,

        /// <summary>
        /// Two devices were discovered with the same name.
        /// </summary>
        DuplicateDevice = 0x0000002A,

        /// <summary>
        /// The drivers set the device state to failed.
        /// </summary>
        FailedPostStart = 0x0000002B,

        /// <summary>
        /// This device was failed post start via usermode.
        /// </summary>
        Halted = 0x0000002C,

        /// <summary>
        /// The devinst currently exists only in the registry.
        /// </summary>
        Phantom = 0x0000002D,

        /// <summary>
        /// The system is shutting down.
        /// </summary>
        SystemShutdown = 0x0000002E,

        /// <summary>
        /// The device is offline awaiting removal.
        /// </summary>
        HeldForEject = 0x0000002F,

        /// <summary>
        /// One or more drivers is blocked from loading.
        /// </summary>
        DriverBlocked = 0x00000030,

        /// <summary>
        /// System hive has grown too large.
        /// </summary>
        RegistryTooLarge = 0x00000031,

        /// <summary>
        /// Failed to apply one or more registry properties.
        /// </summary>
        SetPropertiesFailed = 0x00000032,

        /// <summary>
        /// Device is stalled waiting on a dependency to start.
        /// </summary>
        WaitingOnDependency = 0x00000033,

        /// <summary>
        /// Failed load driver due to unsigned image.
        /// </summary>
        UnsignedDriver = 0x00000034,

        /// <summary>
        /// Device is being used by kernel debugger.
        /// </summary>
        UsedByDebugger = 0x00000035,

        /// <summary>
        /// Device is being reset.
        /// </summary>
        DeviceReset = 0x00000036,

        /// <summary>
        /// Device is blocked while console is locked.
        /// </summary>
        ConsoleLocked = 0x00000037,

        /// <summary>
        /// Device needs extended class configuration to start.
        /// </summary>
        NeedClassConfig = 0x00000038,

        /// <summary>
        /// Assignment to guest partition failed.
        /// </summary>
        GuestAssignmentFailed = 0x00000039
    }
}
