namespace RJCP.IO.DeviceMgr
{
    using System;

    /// <summary>
    /// Device Capabilities.
    /// </summary>
    [Flags]
    public enum DeviceCapabilities
    {
        /// <summary>
        /// No capabilities listed.
        /// </summary>
        None = 0,

        /// <summary>
        /// Locking is supported.
        /// </summary>
        LockSupported = 0x00000001,

        /// <summary>
        /// Ejecting is supported.
        /// </summary>
        EjectSupported = 0x00000002,

        /// <summary>
        /// Device is removable.
        /// </summary>
        Removable = 0x00000004,

        /// <summary>
        /// Docking Device.
        /// </summary>
        DockDevice = 0x00000008,

        /// <summary>
        /// Unique Identifier.
        /// </summary>
        UniqueId = 0x00000010,

        /// <summary>
        /// Silent install
        /// </summary>
        SilentInstall = 0x00000020,

        /// <summary>
        /// Supports accessing the Raw device.
        /// </summary>
        RawDeviceOk = 0x00000040,

        /// <summary>
        /// Surprise removal supported (without ejecting).
        /// </summary>
        SurpriseRemovalOk = 0x00000080,

        /// <summary>
        /// Hardware disabled
        /// </summary>
        HardwareDisabled = 0x00000100,

        /// <summary>
        /// Non-Dynamic.
        /// </summary>
        Nondynamic = 0x00000200,

        /// <summary>
        /// Secure device
        /// </summary>
        SecureDevice = 0x00000400
    }
}
