namespace RJCP.Native.Win32
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using IO.DeviceMgr;

    internal static partial class Kernel32
    {
        private static Version OSVersion = null;

        public static Version GetOsVersion()
        {
            if (OSVersion != null) return OSVersion;

            OSVersion = InternalGetOsVersion();
            return OSVersion;
        }

        private static Version InternalGetOsVersion()
        {
            OSVERSIONINFO info = new OSVERSIONINFO();
            try {
                bool result = GetVersionEx(info);
                if (!result) {
                    int error = Marshal.GetLastWin32Error();
                    Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Couldn't get OS Version, return 0x{error:x}");
                    return Environment.OSVersion.Version;
                }

                if (info.PlatformId != WinPlatformId.WinNT) {
                    throw new PlatformNotSupportedException();
                }

                if (info.MajorVersion <= 5) {
                    return new Version(info.MajorVersion, info.MinorVersion);
                }
            } catch (EntryPointNotFoundException) {
                // The GetVersionEx() call doesn't exist. Just return the .NET implementation.
                return Environment.OSVersion.Version;
            }

            // For later Operating Systems, the official API lies for reasons of backwards compatibility. So we'll use
            // the NtDll user space to get the "real" version regardless of the manifest. This is important, as the OS
            // APIs return values, not based on the manifest of this application.

            int ntstatus;
            OSVERSIONINFOEX rtlInfoEx = new OSVERSIONINFOEX();
            try {
                ntstatus = NtDll.RtlGetVersion(rtlInfoEx);
            } catch (EntryPointNotFoundException) {
                // The RtlGetVersion() call doesn't exist, or it returned an error
                Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Couldn't get NT OS Version, entry point not found");
                ntstatus = -1;
            }

            if (ntstatus == 0)
                return new Version(rtlInfoEx.MajorVersion, rtlInfoEx.MinorVersion);

            Log.CfgMgr.TraceEvent(TraceEventType.Error, $"Couldn't get NT OS Version, NTStatus 0x{ntstatus:x}");
            return new Version(info.MajorVersion, info.MinorVersion);
        }

        public static bool IsWin8OrNewer(Version osVersion)
        {
            return osVersion >= new Version(6, 2);
        }

        public static bool IsWin10OrNewer(Version osVersion)
        {
            return osVersion >= new Version(10, 0);
        }
    }
}
