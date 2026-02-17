namespace RJCP.IO.DeviceMgr
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Microsoft.Win32;
    using Microsoft.Win32.SafeHandles;
    using Native.Win32;
    using RJCP.Core.Environment;

#if NETSTANDARD
    using System.Buffers;
#endif

    /// <summary>
    /// A Device Instance representing a device in the system.
    /// </summary>
    public class DeviceInstance
    {
        private readonly SafeDevInst m_DevInst;

        /// <summary>
        /// Gets a tree of all devices, starting from the root, for devices that are available in the system.
        /// </summary>
        /// <returns>
        /// A tree of <see cref="DeviceInstance"/> objects. If there was a problem retrieving the tree,
        /// <see langword="null"/> is returned
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">This is only supported on Windows NT platforms.</exception>
        public static DeviceInstance GetRoot()
        {
            if (!Platform.IsWinNT())
                throw new PlatformNotSupportedException();

            Log.CfgMgr.TraceEvent(TraceEventType.Verbose, $"Getting device tree");

            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Locate_DevNode(out SafeDevInst devInst, null, CfgMgr32.CM_LOCATE_DEVINST.NORMAL);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Couldn't get root node, return {ret}");
                return null;
            }

            lock (s_CachedLock) {
                DeviceInstance root = GetDeviceInstance(devInst, null);
                if (root != null) root.PopulateChildren(false);
                return root;
            }
        }

        /// <summary>
        /// Gets the list of all devices in the system, even if they're not physicall present.
        /// </summary>
        /// <returns>A list of all devices in the system.</returns>
        /// <exception cref="PlatformNotSupportedException">This is only supported on Windows NT platforms.</exception>
        /// <remarks>
        /// Even though the list is returned as a flat list, the <see cref="Children"/> and <see cref="Parent"/> fields
        /// can be used to build a device tree. The easiest way to get the root node of the tree is to call
        /// <see cref="GetRoot()"/> after calling this method. It will only locate the root node, and won't reiterate
        /// the children.
        /// </remarks>
        public static IList<DeviceInstance> GetList()
        {
            return GetList(LocateMode.Phantom);
        }

        /// <summary>
        /// Gets the list of all devices in the system, even if they're not physicall present.
        /// </summary>
        /// <param name="mode">The query mode when locating devices.</param>
        /// <returns>A list of all devices in the system.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        /// <remarks>
        /// Even though the list is returned as a flat list, the <see cref="Children"/> and <see cref="Parent"/> fields
        /// can be used to build a device tree. The easiest way to get the root node of the tree is to call
        /// <see cref="GetRoot()"/> after calling this method. It will only locate the root node, and won't reiterate
        /// the children.
        /// </remarks>
        public static IList<DeviceInstance> GetList(LocateMode mode)
        {
            if (!Platform.IsWinNT())
                throw new PlatformNotSupportedException();

            CfgMgr32.CM_LOCATE_DEVINST cmMode;
            switch (mode) {
            case LocateMode.Normal: cmMode = CfgMgr32.CM_LOCATE_DEVINST.NORMAL; break;
            case LocateMode.Phantom: cmMode = CfgMgr32.CM_LOCATE_DEVINST.PHANTOM; break;
            default: throw new ArgumentException("Invalid mode", nameof(mode));
            }

            Log.CfgMgr.TraceEvent(TraceEventType.Verbose, $"Getting device list");

            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_Device_ID_List(null, out string[] instances);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Couldn't get list size, return {ret}");
#if NETSTANDARD
                return Array.Empty<DeviceInstance>();
#else
                return new DeviceInstance[0];
#endif
            }

            List<DeviceInstance> devices = new List<DeviceInstance>();
            lock (s_CachedLock) {
                foreach (string instance in instances) {
                    ret = CfgMgr32.CM_Locate_DevNode(out SafeDevInst devInst, instance, cmMode);
                    if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                        if (ret != CfgMgr32.CONFIGRET.CR_NO_SUCH_DEVNODE)
                            Log.CfgMgr.TraceEvent(TraceEventType.Error, $"{instance}: Couldn't locate node, return {ret}");
                    } else {
                        DeviceInstance node = GetDeviceInstance(devInst, null, instance);
                        if (node != null) devices.Add(node);
                    }
                }

                // Now check for the parents
                foreach (DeviceInstance device in devices) {
                    // Check if we have the parent. If we do, we add it.
                    QueryParent(device);
                }

                // Now we rebuild the children tree
                Dictionary<DeviceInstance, List<DeviceInstance>> childrenTree =
                    new Dictionary<DeviceInstance, List<DeviceInstance>>();
                foreach (DeviceInstance device in devices) {
                    if (device.Parent != null) {
                        if (!childrenTree.TryGetValue(device.Parent, out List<DeviceInstance> children)) {
                            children = new List<DeviceInstance>();
                            childrenTree.Add(device.Parent, children);
                        }
                        children.Add(device);
                    }
                }
                foreach (var entry in childrenTree) {
                    // We will replace the entries at the end, so that if another thread is enumerating these entries,
                    // they'll see either the old values or the new values, but the list won't change dynamically on
                    // them.
                    entry.Key.m_Children = entry.Value;
                    entry.Key.m_IsPopulated = true;
                }
            }
            return devices;
        }

        private static void QueryParent(DeviceInstance device)
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_Parent(out SafeDevInst parent, device.m_DevInst, 0);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                if (ret != CfgMgr32.CONFIGRET.CR_NO_SUCH_DEVNODE)
                    Log.CfgMgr.TraceEvent(TraceEventType.Error, $"{device.DebugName}: Couldn't get parent, return {ret}");
                return;
            }

            DeviceInstance parentDev = GetDeviceInstance(parent, null);
            if (device.Parent == null) {
                device.Parent = parentDev;
            } else if (!ReferenceEquals(parentDev, device.Parent)) {
                Log.CfgMgr.TraceEvent(TraceEventType.Warning, $"{device.DebugName}: Assigned parent differs, old={device.Parent.DebugName}, new={parentDev.DebugName}");
            }
            QueryParent(parentDev);
        }

        #region Cached Device Instances
        private static readonly object s_CachedLock = new object();
        private static readonly Dictionary<IntPtr, DeviceInstance> s_CachedInstances = new Dictionary<IntPtr, DeviceInstance>();

        private static DeviceInstance GetDeviceInstance(SafeDevInst devInst, DeviceInstance parent, string name = null)
        {
            // Ensure to lock first. We don't do the lock here, as we may want to lock during enumeration, reducing the
            // overhead of locking for the usual case of iterating only once.

            // This isn't actually dangerous, as the handles are global and don't change.
            IntPtr handle = devInst.DangerousGetHandle();

            if (s_CachedInstances.TryGetValue(handle, out DeviceInstance value)) {
                if (!value.m_DevInst.IsClosed && !value.m_DevInst.IsInvalid)
                    return value;
            }

            value = new DeviceInstance(devInst, name) {
                Parent = parent
            };
            s_CachedInstances[handle] = value;
            return value;
        }
        #endregion

        private DeviceInstance(SafeDevInst handle) : this(handle, null) { }

        private DeviceInstance(SafeDevInst handle, string name)
        {
            if (handle.IsInvalid || handle.IsClosed)
                throw new InvalidOperationException();

            m_DevInst = handle;

            // This API should only be called when we're sure that we have the right name. Often, we need to get the
            // name of the device before instantiating this object, just to see if it's cached. So we can save a call.

            m_Name = name ?? GetDeviceId(handle);
            GetStatus();
            SetProperties();
        }

        private void SetProperties()
        {
            m_DevDesc = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.DEVICEDESC);
            m_Service = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.SERVICE);
            m_Class = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.CLASS);
            m_ClassGuid = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.CLASSGUID);
            m_Driver = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.DRIVER);
            m_Manufacturer = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.MFG);
            m_FriendlyName = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.FRIENDLYNAME);
            m_Location = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.LOCATION_INFORMATION);
            m_PhysicalDevice = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.PHYSICAL_DEVICE_OBJECT_NAME);
            m_ConfigFlags = new DeviceProperty<int>(this, CfgMgr32.CM_DRP.CONFIGFLAGS);
            m_Capabilities = new DeviceProperty<int>(this, CfgMgr32.CM_DRP.CAPABILITIES);
            m_HardwareIds = new DeviceProperty<string[]>(this, CfgMgr32.CM_DRP.HARDWAREID);
            m_CompatibleIds = new DeviceProperty<string[]>(this, CfgMgr32.CM_DRP.COMPATIBLEIDS);
            m_UpperFilters = new DeviceProperty<string[]>(this, CfgMgr32.CM_DRP.UPPERFILTERS);
            m_LowerFilters = new DeviceProperty<string[]>(this, CfgMgr32.CM_DRP.LOWERFILTERS);
            m_LocationPaths = new DeviceProperty<string[]>(this, CfgMgr32.CM_DRP.LOCATION_PATHS);
            m_BaseContainerId = new DeviceProperty<string>(this, CfgMgr32.CM_DRP.BASE_CONTAINERID);
        }

        private static string GetDeviceId(SafeDevInst devInst)
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_Device_ID_Size(out int length, devInst, 0);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Handle 0x{devInst.DangerousGetHandle():x}: Couldn't get device identifier length, return {ret}");
                return null;
            }

            StringBuilder buffer = new StringBuilder(length + 1);
            ret = CfgMgr32.CM_Get_Device_ID(devInst, buffer, length, 0);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Handle 0x{devInst.DangerousGetHandle():x}: Couldn't get device identifier for length {length}, return {ret}");
                return null;
            }

            return buffer.ToString(0, length);
        }

        private void GetStatus()
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_DevNode_Status(out CfgMgr32.DN_STATUS status, out int problem, m_DevInst, 0);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                HasProblem = true;
                ProblemCode = DeviceProblem.DeviceNotThere;
                return;
            }

            Status = DeviceStatusConvert.Get(status);
            HasProblem = (status & CfgMgr32.DN_STATUS.HAS_PROBLEM) != 0;
            ProblemCode = (DeviceProblem)problem;
        }

        private void PopulateChildren(bool overwrite)
        {
            if (!overwrite) {
                if (m_IsPopulated || m_Children.Count > 0) return;
            }

            if (m_DevInst.IsInvalid || m_DevInst.IsClosed)
                throw new ObjectDisposedException(nameof(DeviceInstance));

            List<DeviceInstance> children = new List<DeviceInstance>();

            CfgMgr32.CONFIGRET ret;

            ret = CfgMgr32.CM_Get_Child(out SafeDevInst child, m_DevInst, 0);
            if (ret == CfgMgr32.CONFIGRET.CR_NO_SUCH_DEVINST) {
                m_Children = children;
                m_IsPopulated = true;
                return;
            }
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                Log.CfgMgr.TraceEvent(TraceEventType.Warning, $"{DebugName}: Couldn't get child node, return {ret}");
                m_Children = children;
                m_IsPopulated = true;
                return;
            }
            DeviceInstance node = GetDeviceInstance(child, this);
            children.Add(node);

            bool finished = false;
            while (!finished) {
                ret = CfgMgr32.CM_Get_Sibling(out SafeDevInst sibling, node.m_DevInst, 0);
                switch (ret) {
                case CfgMgr32.CONFIGRET.CR_SUCCESS:
                    node = GetDeviceInstance(sibling, this);
                    children.Add(node);
                    break;
                default:
                    if (ret != CfgMgr32.CONFIGRET.CR_NO_SUCH_DEVINST)
                        Log.CfgMgr.TraceEvent(TraceEventType.Warning, $"{DebugName}: Couldn't get sibling node from {node.DebugName}, return {ret}");
                    finished = true;
                    break;
                }
            }

            // Now recurse into the new nodes and populate them also.
            foreach (DeviceInstance dev in children) {
                dev.PopulateChildren(false);
            }

            // Only make the tree visible once it is complete. This makes assignment atomic at the end (assignment of
            // reference types is atomic).
            m_Children = children;
            m_IsPopulated = true;
        }

        /// <summary>
        /// Gets the underlying handle of the DevInst for the CfgMgr32 API.
        /// </summary>
        /// <value>The underlying configuration manager handle.</value>
        public SafeDevInst Handle
        {
            get
            {
                // We make a copy of the handle. The copy is because if the user closes it, it won't affect this
                // implementation. Also, closing the handle has no effect.
                return new SafeDevInst(m_DevInst.DangerousGetHandle());
            }
        }

        internal SafeDevInst InternalHandle
        {
            get
            {
                return m_DevInst;
            }
        }

        /// <summary>
        /// Gets the parent device instance.
        /// </summary>
        /// <value>The parent device instance.</value>
        public DeviceInstance Parent { get; private set; }

        private bool m_IsPopulated;
        private List<DeviceInstance> m_Children = new List<DeviceInstance>();

        /// <summary>
        /// Get the children device instances from this node.
        /// </summary>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<DeviceInstance> Children
        {
            get { return m_Children; }
        }
#else
        public IList<DeviceInstance> Children
        {
            get { return new ReadOnlyList<DeviceInstance>(m_Children); }
        }
#endif

        /// <summary>
        /// Gets the status of the device at the time it was enumerated.
        /// </summary>
        /// <value>The status of the device at the time it was enumerated.</value>
        public DeviceStatus Status { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance has problem.
        /// </summary>
        /// <value>
        /// Returns <see langword="true"/> if this instance has problem; otherwise, <see langword="false"/>.
        /// </value>
        public bool HasProblem { get; private set; }

        /// <summary>
        /// Gets the problem code for this driver if it has a problem.
        /// </summary>
        /// <value>The problem code for this driver.</value>
        public DeviceProblem ProblemCode { get; private set; }

        private DeviceProperty<string> m_DevDesc;

        /// <summary>
        /// Gets the device description.
        /// </summary>
        /// <value>The device description.</value>
        public string DeviceDescription { get { return m_DevDesc.Value; } }

        private DeviceProperty<string> m_Service;

        /// <summary>
        /// Gets the name of the service.
        /// </summary>
        /// <value>The name of the service.</value>
        public string Service { get { return m_Service.Value; } }

        private DeviceProperty<string> m_Class;

        /// <summary>
        /// Gets the class name of the device.
        /// </summary>
        /// <value>The class name of the device.</value>
        public string Class { get { return m_Class.Value; } }

        private DeviceProperty<string> m_ClassGuid;

        /// <summary>
        /// Gets the class unique identifier of the device.
        /// </summary>
        /// <value>The class unique identifier of the device.</value>
        public string ClassGuid { get { return m_ClassGuid.Value; } }

        private DeviceProperty<string> m_Driver;

        /// <summary>
        /// Gets the driver.
        /// </summary>
        /// <value>The driver.</value>
        public string Driver { get { return m_Driver.Value; } }

        private DeviceProperty<string> m_Manufacturer;

        /// <summary>
        /// Gets the manufacturer of the device or driver.
        /// </summary>
        /// <value>The manufacturer of the device or driver.</value>
        public string Manufacturer { get { return m_Manufacturer.Value; } }

        private DeviceProperty<string> m_FriendlyName;

        /// <summary>
        /// Gets the friendly name of the device.
        /// </summary>
        /// <value>The friendly name of the device.</value>
        public string FriendlyName { get { return m_FriendlyName.Value; } }

        private DeviceProperty<string> m_Location;

        /// <summary>
        /// Gets the location of the device.
        /// </summary>
        /// <value>The location of the device.</value>
        public string Location { get { return m_Location.Value; } }

        private DeviceProperty<string> m_PhysicalDevice;

        /// <summary>
        /// Gets the physical device name.
        /// </summary>
        /// <value>The physical device name.</value>
        public string PhysicalDevice { get { return m_PhysicalDevice.Value; } }

        private DeviceProperty<int> m_ConfigFlags;

        /// <summary>
        /// Gets the configuration flags for the device.
        /// </summary>
        /// <value>The configuration flags for the device.</value>
        public int ConfigFlags { get { return m_ConfigFlags.Value; } }

        private DeviceProperty<int> m_Capabilities;

        /// <summary>
        /// Gets the capability flags of the device.
        /// </summary>
        /// <value>The capabilities of the device.</value>
        public DeviceCapabilities Capabilities { get { return (DeviceCapabilities)m_Capabilities.Value; } }

        private DeviceProperty<string[]> m_HardwareIds;

        /// <summary>
        /// Gets the hardware ids.
        /// </summary>
        /// <value>The hardware ids.</value>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<string> HardwareIds { get { return m_HardwareIds.Value; } }
#else
        public IList<string> HardwareIds { get { return m_HardwareIds.Value; } }
#endif

        private DeviceProperty<string[]> m_CompatibleIds;

        /// <summary>
        /// Gets the compatible ids.
        /// </summary>
        /// <value>The compatible ids.</value>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<string> CompatibleIds { get { return m_CompatibleIds.Value; } }
#else
        public IList<string> CompatibleIds { get { return m_CompatibleIds.Value; } }
#endif

        private DeviceProperty<string[]> m_UpperFilters;

        /// <summary>
        /// Gets the compatible ids.
        /// </summary>
        /// <value>The compatible ids.</value>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<string> UpperFilters { get { return m_UpperFilters.Value; } }
#else
        public IList<string> UpperFilters { get { return m_UpperFilters.Value; } }
#endif

        private DeviceProperty<string[]> m_LowerFilters;

        /// <summary>
        /// Gets the compatible ids.
        /// </summary>
        /// <value>The compatible ids.</value>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<string> LowerFilters { get { return m_LowerFilters.Value; } }
#else
        public IList<string> LowerFilters { get { return m_LowerFilters.Value; } }
#endif

        private DeviceProperty<string[]> m_LocationPaths;

        /// <summary>
        /// Gets the compatible ids (Windows 2003 and later).
        /// </summary>
        /// <value>The compatible ids.</value>
#if NET45_OR_GREATER || NETSTANDARD
        public IReadOnlyList<string> LocationPaths { get { return m_LocationPaths.Value; } }
#else
        public IList<string> LocationPaths { get { return m_LocationPaths.Value; } }
#endif

        private DeviceProperty<string> m_BaseContainerId;

        /// <summary>
        /// Gets the base container identifier (Windows 7).
        /// </summary>
        /// <value>The location of the device.</value>
        public string BaseContainerId { get { return m_BaseContainerId.Value; } }

        /// <summary>
        /// Gets the names of the keys for the device from the registry.
        /// </summary>
        /// <returns>
        /// An array of all the keys for the device from the registry. This can be then used with
        /// <see cref="GetDeviceProperty{T}(string, T)"/>.
        /// </returns>
        public string[] GetDeviceProperties()
        {
            using (RegistryKey driverKey = GetDeviceKey()) {
                if (driverKey == null) {
#if NETSTANDARD
                    return Array.Empty<string>();
#else
                    return new string[0];
#endif
                }
                return driverKey.GetValueNames();
            }
        }

        /// <summary>
        /// Gets the device property.
        /// </summary>
        /// <param name="keyName">Name of the key.</param>
        /// <returns>The result. If the key doesn't exist, <see langword="null"/> is returned.</returns>
        public object GetDeviceProperty(string keyName)
        {
            return GetDeviceProperty(keyName, null);
        }

        /// <summary>
        /// Gets the device property.
        /// </summary>
        /// <param name="keyName">Name of the key.</param>
        /// <param name="defValue">The value to return if the key doesn't exist.</param>
        /// <returns>The result. If the key doesn't exist, the default value is returned.</returns>
        public object GetDeviceProperty(string keyName, object defValue)
        {
            using (RegistryKey driverKey = GetDeviceKey()) {
                return driverKey == null ?
                    defValue :
                    driverKey.GetValue(keyName);
            }
        }

        /// <summary>
        /// Gets the device property.
        /// </summary>
        /// <typeparam name="T">The type to convert the result to.</typeparam>
        /// <param name="keyName">Name of the key.</param>
        /// <returns>The result. If the key doesn't exist, the default value is returned.</returns>
        public T GetDeviceProperty<T>(string keyName)
        {
            return GetDeviceProperty<T>(keyName, default);
        }

        /// <summary>
        /// Gets the device property.
        /// </summary>
        /// <typeparam name="T">The type to convert the result to.</typeparam>
        /// <param name="keyName">Name of the key.</param>
        /// <param name="defValue">The value to return if the key doesn't exist.</param>
        /// <returns>The result. If the key doesn't exist, the default value is returned.</returns>
        public T GetDeviceProperty<T>(string keyName, T defValue)
        {
            using (RegistryKey driverKey = GetDeviceKey()) {
                return driverKey == null ?
                    defValue :
                    (T)driverKey.GetValue(keyName);
            }
        }

        private RegistryKey GetDeviceKey()
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Open_DevNode_Key(
                m_DevInst, Kernel32.REGSAM.KEY_READ, 0, CfgMgr32.RegDisposition.OpenExisting,
                out SafeRegistryHandle key, 0);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                if (ret != CfgMgr32.CONFIGRET.CR_NO_SUCH_REGISTRY_KEY) {
                    Log.CfgMgr.TraceEvent(TraceEventType.Warning,
                        $"{DebugName}: Couldn't get device key, return {ret}");
                }
                return null;
            }
            if (key.IsInvalid || key.IsClosed) {
                Log.CfgMgr.TraceEvent(TraceEventType.Error,
                    $"{DebugName}: Couldn't get device key, registry is invalid or closed");
                return null;
            }

            return RegistryKey.FromHandle(key);
        }

        /// <summary>
        /// Refreshes this instance.
        /// </summary>
        /// <remarks>
        /// This method should be used if it is believed that this node, and the children might have changed. Refreshing
        /// from this tree will remove nodes that are not currently connected / existing, thus removing entries that may
        /// have been populated by <see cref="GetList()"/>.
        /// </remarks>
        public void Refresh()
        {
            lock (s_CachedLock) {
                // First we iterate through all the children and reset the properties. Then we'll reenumerate and
                // replace the children with a Depth First Search, touching all the leaves first. So then if someone is
                // enumerating the items, the list won't change, because we'll provide new instances of the children
                // lists.
                //
                // This also works with caching, as PopulateChildren looks into the global cache, it returns an existing
                // entry and just uses that. Its children were updated in a previous call due to the DepthFirstSearch
                // algorithm of updating the leaves first.
                DepthFirstSearch(this, (devInst) => {
                    // This might fail, when a device is removed. It can be ignored.
                    devInst.GetStatus();

                    // If the user is already querying the properties, it will still work, because they'll use the old
                    // object before reassignment.
                    devInst.SetProperties();

                    // Populating the children from the bottom up refreshes only the children node at a time, because
                    // their children have already been populated. The variable to overwrite is only for the current
                    // node, which is fine, as we're doing a DFS search operating at the leaf nodes first.
                    //
                    // If the node was added since the current three, then this will enumerate also the children.
                    devInst.PopulateChildren(true);
                });
            }
        }

        private static void DepthFirstSearch(DeviceInstance devInst, Action<DeviceInstance> action)
        {
            foreach (DeviceInstance child in devInst.Children) {
                DepthFirstSearch(child, action);
            }
            action(devInst);
        }

        private readonly string m_Name;

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="string" /> that represents this instance.</returns>
        public override string ToString()
        {
            return m_Name;
        }

        private string DebugName
        {
            get
            {
                return m_Name ?? $"Handle {m_DevInst.DangerousGetHandle():x}";
            }
        }

        // There are no objects to dispose (at this time). The SafeDevInst object can be disposed of, but it doesn't do
        // anything anyway. We'll just let the finalizer deal with it.

        // Secondly, disposing cached elements is dangerous. We'd need to remove it from the cache. But there might be
        // multiple instances of the same object somewhere else, and when Code Block X disposes of the object,
        // unexpected Code Block Y sees the object as disposed. That would be bad design.

        // As such, and documented in SafeDevInst, it's just a convenient wrapper, and the Windows API doesn't offer a
        // way to close these objects since Windows 2000 (till Windows 11), so it won't in the future either.
    }
}
