namespace RJCP.IO.DeviceMgr
{
    using System;
    using System.Diagnostics;
    using Native.Win32;

#if NETSTANDARD
    using System.Buffers;
#endif

    internal class DeviceProperty<T>
    {
        private readonly DeviceInstance m_DevInst;
        private readonly CfgMgr32.CM_DRP m_DeviceProperty;
        private bool m_Retrieved;
        private object m_Value;

        public DeviceProperty(DeviceInstance devInst, CfgMgr32.CM_DRP deviceProperty)
        {
            m_DevInst = devInst;
            m_DeviceProperty = deviceProperty;

            if (typeof(T) != typeof(string) && typeof(T) != typeof(int) && typeof(T) != typeof(string[])) {
                string message = string.Format("Type {0} is not supported", typeof(T).Name);
                throw new NotSupportedException(message);
            }
        }

        public T Value
        {
            get
            {
                if (!m_Retrieved) {
                    m_Retrieved = true;
                    if (typeof(T) == typeof(string)) {
                        m_Value = GetPropertyString();
                    } else if (typeof(T) == typeof(int)) {
                        m_Value = GetPropertyInt();
                    } else if (typeof(T) == typeof(string[])) {
                        m_Value = GetPropertyStringArray();
                    }
                }
                return (T)m_Value;
            }
        }

        private string GetPropertyString()
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_DevNode_Registry_Property(
                m_DevInst.InternalHandle, m_DeviceProperty, out int _, out string buffer);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                if (ShouldWarnProperty(ret))
                    Log.CfgMgr.TraceEvent(TraceEventType.Warning,
                        $"{m_DevInst}: Couldn't get property for {m_DeviceProperty}, return {ret}");
                return string.Empty;
            }
            return buffer;
        }

        private string[] GetPropertyStringArray()
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_DevNode_Registry_Property(
                 m_DevInst.InternalHandle, m_DeviceProperty, out int _, out string[] buffer);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                if (ShouldWarnProperty(ret))
                    Log.CfgMgr.TraceEvent(TraceEventType.Warning,
                        $"{m_DevInst}: Couldn't get property for {m_DeviceProperty}, return {ret}");
                return buffer;
            }
            return buffer;
        }

        private int GetPropertyInt()
        {
            CfgMgr32.CONFIGRET ret = CfgMgr32.CM_Get_DevNode_Registry_Property(
                m_DevInst.InternalHandle, m_DeviceProperty, out int _, out int value);
            if (ret != CfgMgr32.CONFIGRET.CR_SUCCESS) {
                if (ShouldWarnProperty(ret))
                    Log.CfgMgr.TraceEvent(TraceEventType.Warning,
                        $"{m_DevInst}: Couldn't get property for {m_DeviceProperty}, return {ret}");
                return 0;
            }

            return value;
        }

        private bool ShouldWarnProperty(CfgMgr32.CONFIGRET ret)
        {
            if (ret == CfgMgr32.CONFIGRET.CR_NO_SUCH_VALUE) return false;
            if (ret == CfgMgr32.CONFIGRET.CR_INVALID_PROPERTY) return false;
            if (ret == CfgMgr32.CONFIGRET.CR_NO_SUCH_DEVNODE &&
                m_DeviceProperty == CfgMgr32.CM_DRP.PHYSICAL_DEVICE_OBJECT_NAME) return false;

            return true;
        }
    }
}
