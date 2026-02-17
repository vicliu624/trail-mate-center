namespace RJCP.IO.DeviceMgr
{
    /// <summary>
    /// Defines how devices should be enumerated.
    /// </summary>
    public enum LocateMode
    {
        /// <summary>
        /// Retrieves the device instance handle for the specified device only if the device is currently configured in
        /// the device tree.
        /// </summary>
        Normal,

        /// <summary>
        /// Retrieves a device instance handle for the specified device if the device is currently configured in the
        /// device tree or the device is a nonpresent device that is not currently configured in the device tree.
        /// </summary>
        Phantom
    }
}
