namespace RJCP.Core.Environment.Version
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Security.Permissions;
    using Microsoft.Win32;
    using Native.Win32;
    using Resources;

    /// <summary>
    /// Class to get information about the local machine.
    /// </summary>
    internal class WinVersionQuery : WinVersion
    {
        /// <summary>
        /// Default constructor, getting information about the local machine. Use the static method <c>LocalMachine</c>
        /// instead for efficiency.
        /// </summary>
        public WinVersionQuery()
        {
            if (!Platform.IsWinNT())
                throw new PlatformNotSupportedException(Messages.PlatformNotSupportedEx);

            bool isNativeQuery = true;

#if NETFRAMEWORK
            // Create a security permission object to describe the UnmanagedCode permission:
            SecurityPermission perm =
               new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);

            // Check that you have permission to access unmanaged code. If you don't have permission to access unmanaged
            // code, then this call will throw a SecurityException. Even though the CallUnmanagedCodeWithPermission
            // method is called from a stack frame that already calls Assert for unmanaged code, you still cannot call
            // native code. Because you use Deny here, the permission gets overwritten.
            try {
                perm.Assert();
            } catch {
                isNativeQuery = false;
            }
#endif

            if (isNativeQuery) {
                if (!GetVersionEx())
                    GetVersion();

                DetectArchitecture();
                GetProductInfo();
                DetectWin2003R2();
                DetectWinXP();
                DetectWinXPx64();
            } else {
                GetNativeVersion();
            }
            DetectWin10();
            Lock();
        }

        /// <summary>
        /// Gets the Operating System version.
        /// </summary>
        /// <remarks>
        /// We use the Microsoft recommended way of getting the Operating System Version using GetVersionEx(), first
        /// with OSVERSIONINFO and then with OSVERSIONINFOEX if supported.
        /// </remarks>
        /// <returns><see langword="true"/> if we successfully retrieved OS version information.</returns>
        private bool GetVersionEx()
        {
            bool result;

            // Get the basic information. If this shows we've got a newer operating system, we can get more detailed
            // information later.
            Kernel32.OSVERSIONINFO info = new Kernel32.OSVERSIONINFO();
            try {
                result = Kernel32.GetVersionEx(info);
                if (!result) {
                    int error = Marshal.GetLastWin32Error();
                    string message = string.Format(Messages.Win32Ex_GetVersionEx, error);
                    throw new Win32Exception(error, message);
                }
            } catch {
                // The GetVersionEx() call doesn't exist, or it returned an error
                return false;
            }

            PlatformId = (WinPlatform)info.PlatformId;
            MajorVersion = info.MajorVersion;
            MinorVersion = info.MinorVersion;
            BuildNumber = info.BuildNumber;
            CSDVersion = info.CSDVersion;

            if (PlatformId == WinPlatform.WinNT) {
                if (MajorVersion < 4) {
                    // Windows 3.51 or earlier
                    return true;
                } else if (MajorVersion == 4) {
                    if (MinorVersion == 0) {
                        if (CSDVersion != "Service Pack 6") {
                            // Earlier than WinNT 4.0 SP6
                            return true;
                        }
                    }
                }
            } else if (PlatformId == WinPlatform.Win9x) {
                BuildNumber = (info.BuildNumber & 0xFFFF);
                return true;
            }

            Kernel32.OSVERSIONINFOEX infoex = new Kernel32.OSVERSIONINFOEX();
            result = Kernel32.GetVersionEx(infoex);
            if (!result) {
                int error = Marshal.GetLastWin32Error();
                string message = string.Format(Messages.Win32Ex_GetVersionEx, error);
                throw new Win32Exception(error, message);
            }

            int ntstatus;
            Kernel32.OSVERSIONINFOEX rtlInfoEx = new Kernel32.OSVERSIONINFOEX();
            try {
                ntstatus = NtDll.RtlGetVersion(rtlInfoEx);
            } catch {
                // The RtlGetVersionEx() call doesn't exist, or it returned an error
                ntstatus = -1;
            }

            bool newer;
            if (ntstatus == 0) {
                // The direct call worked, and should overcome the API breakage that depends on a manifest. Just in case
                // that the real method returns a value that is older than the "real" windows API.
                Version vInfo = new Version(infoex.MajorVersion, infoex.MinorVersion, infoex.BuildNumber);
                Version vRtl = new Version(rtlInfoEx.MajorVersion, rtlInfoEx.MinorVersion, rtlInfoEx.BuildNumber);
                newer = vRtl >= vInfo;
            } else {
                newer = false;
            }

            if (newer) {
                PlatformId = (WinPlatform)rtlInfoEx.PlatformId;
                MajorVersion = rtlInfoEx.MajorVersion;
                MinorVersion = rtlInfoEx.MinorVersion;
                BuildNumber = rtlInfoEx.BuildNumber;
                CSDVersion = rtlInfoEx.CSDVersion;
                SuiteFlags = (WinSuite)rtlInfoEx.SuiteMask;
                ProductType = (WinProductType)rtlInfoEx.ProductType;
                ServicePackMajor = rtlInfoEx.ServicePackMajor;
                ServicePackMinor = rtlInfoEx.ServicePackMinor;
            } else {
                PlatformId = (WinPlatform)infoex.PlatformId;
                MajorVersion = infoex.MajorVersion;
                MinorVersion = infoex.MinorVersion;
                BuildNumber = infoex.BuildNumber;
                CSDVersion = infoex.CSDVersion;
                SuiteFlags = (WinSuite)infoex.SuiteMask;
                ProductType = (WinProductType)infoex.ProductType;
                ServicePackMajor = infoex.ServicePackMajor;
                ServicePackMinor = infoex.ServicePackMinor;
            }

            IsExtendedPropsSet = true;
            return true;
        }

        /// <summary>
        /// In case we couldn't get the version using the recommended way.
        /// </summary>
        /// <remarks>
        /// See http://msdn.microsoft.com/en-us/library/ms724439%28VS.85%29.aspx. This method doesn't support WinCE, as
        /// we don't get information about the platform ID. Win32s is determined by the upper bit in the system and then
        /// by the operating system version number.
        /// </remarks>
        private void GetVersion()
        {
            uint version = Kernel32.GetVersion();

            MajorVersion = (int)(version & 0xFF);
            MinorVersion = (int)((version & 0xFF00) >> 8);

            if ((version & (1 << 31)) != 0) {
                // Win9x or Win32s
                BuildNumber = 0;
                if (MajorVersion == 3) {
                    PlatformId = WinPlatform.Win32s;
                } else {
                    PlatformId = WinPlatform.Win9x;
                }
            } else {
                // WinNT
                BuildNumber = (int)((version & 0x7FFF0000) >> 16);
                PlatformId = WinPlatform.WinNT;
            }
        }

        /// <summary>
        /// A method to get OS information natively from the .NET subsystem.
        /// </summary>
        /// <returns><see langword="true"/> if the information could be obtained; <see langword="false"/> otherwise.</returns>
        private void GetNativeVersion()
        {
            OperatingSystem os = Environment.OSVersion;

            switch (os.Platform) {
            case PlatformID.Win32S:
                PlatformId = WinPlatform.Win32s;
                break;
            case PlatformID.Win32Windows:
                PlatformId = WinPlatform.Win9x;
                break;
            case PlatformID.WinCE:
                PlatformId = WinPlatform.WinCE;
                break;
            case PlatformID.Win32NT:
                PlatformId = WinPlatform.WinNT;
                break;
            default:
                PlatformId = WinPlatform.Unknown;
                break;
            }

            if (PlatformId == WinPlatform.WinNT) {
                // Try to get the information from the registry
                ProductType = WinProductType.Unknown;
                try {
                    RegistryKey rk =
                        Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\ProductOptions");
                    if (rk != null) {
                        if (rk.GetValue("ProductType") is string producttype) {
                            if (producttype.Equals("WinNT")) ProductType = WinProductType.Workstation;
                            if (producttype.Equals("ServerNT")) ProductType = WinProductType.Server;
                        }
                    }
                } catch {
                    ProductType = WinProductType.Unknown;
                }

                // Found by reverse compiling System.Environment.OSVersion
                ServicePackMajor = os.Version.Revision >> 0x10;
                ServicePackMinor = os.Version.Revision & 0xFFFF;
            }
            MajorVersion = os.Version.Major;
            MinorVersion = os.Version.Minor;
            BuildNumber = os.Version.Build;
            CSDVersion = os.ServicePack;
            NativeArchitecture = WinArchitecture.Unknown;
        }

        private void DetectArchitecture()
        {
            if (DetectArchitectureWithWow2()) return;
            DetectArchitectureWithSystemInfo();
        }

        private bool DetectArchitectureWithWow2()
        {
            try {
                bool result = Kernel32.IsWow64Process2(Kernel32.GetCurrentProcess(), out ushort processMachine, out ushort nativeMachine);
                if (!result) return false;

                NativeArchitecture = FromImageFileMachine(nativeMachine);
                if (processMachine == Kernel32.IMAGE_FILE_MACHINE.UNKNOWN) {
                    // This is not a WoW process, so it's the same as the native architecture.
                    Architecture = NativeArchitecture;
                } else {
                    Architecture = FromImageFileMachine(processMachine);
                }
                return true;
            } catch (EntryPointNotFoundException) {
                return false;
            }
        }

        private void DetectArchitectureWithSystemInfo()
        {
            Kernel32.SYSTEM_INFO lpSystemInfo;

            // GetNativeSystemInfo is independent if we're 64-bit or not But it needs _WIN32_WINNT 0x0501
            ushort processorNativeArchitecture;
            try {
                Kernel32.GetNativeSystemInfo(out lpSystemInfo);
                processorNativeArchitecture = lpSystemInfo.uProcessorInfo.wProcessorArchitecture;
            } catch (EntryPointNotFoundException) {
                processorNativeArchitecture = Kernel32.PROCESSOR_ARCHITECTURE.UNKNOWN;
            }

            if (processorNativeArchitecture == Kernel32.PROCESSOR_ARCHITECTURE.UNKNOWN) {
                Kernel32.GetSystemInfo(out lpSystemInfo);
                processorNativeArchitecture = lpSystemInfo.uProcessorInfo.wProcessorArchitecture;
            }

            NativeArchitecture = FromProcessorArchitecture(processorNativeArchitecture);

            switch (NativeArchitecture) {
            case WinArchitecture.IA64:
            case WinArchitecture.x64:
                bool result = Kernel32.IsWow64Process(Kernel32.GetCurrentProcess(), out bool wow64);
                if (result && wow64) {
                    Architecture = WinArchitecture.x86;
                }
                break;
            }

            if (Architecture == WinArchitecture.Unknown)
                Architecture = NativeArchitecture;
        }

        /// <summary>
        /// Get product information that is available in Windows Vista and later.
        /// </summary>
        private void GetProductInfo()
        {
            if (MajorVersion < 0 || MinorVersion < 0) {
                // Don't have service pack information
                throw new InvalidOperationException(Messages.InvalidOperationEx_InternalGetOsVersion);
            }

            if (!IsExtendedPropsSet) return;

            uint productInfo = 0;
            bool result;
            try {
                result = Kernel32.GetProductInfo(MajorVersion, MinorVersion,
                    ServicePackMajor, ServicePackMinor, out productInfo);
            } catch {
                // The operating system doesn't support this function call
                result = false;
            }
            if (!result) productInfo = 0;

            ProductInfo = (WinProductInfo)productInfo;
        }

        private void DetectWin2003R2()
        {
            if (MajorVersion == Win2003.MajorVersion && MinorVersion == Win2003.MinorVersion) {
                ServerR2 = Kernel32.GetSystemMetrics(Kernel32.SYSTEM_METRICS.SM_SERVERR2) != 0;
            }
        }

        private void DetectWinXP()
        {
            int result;

            if (MajorVersion == WinXP.MajorVersion && MinorVersion == WinXP.MinorVersion) {
                ProductInfo = WinProductInfo.Undefined;

                result = Kernel32.GetSystemMetrics(Kernel32.SYSTEM_METRICS.SM_MEDIACENTER);
                if (result != 0) ProductInfo = WinProductInfo.MediaCenter;

                result = Kernel32.GetSystemMetrics(Kernel32.SYSTEM_METRICS.SM_TABLETPC);
                if (result != 0) ProductInfo = WinProductInfo.TabletPc;

                result = Kernel32.GetSystemMetrics(Kernel32.SYSTEM_METRICS.SM_STARTER);
                if (result != 0) ProductInfo = WinProductInfo.Starter;

                if (ProductInfo == WinProductInfo.Undefined) {
                    if (SuiteFlag(WinSuite.Personal)) {
                        ProductInfo = WinProductInfo.Home_Premium;
                    } else {
                        ProductInfo = WinProductInfo.Professional;
                    }
                }
            }
        }

        private void DetectWinXPx64()
        {
            if (MajorVersion == WinXPx64.MajorVersion && MinorVersion == WinXPx64.MinorVersion) {
                if (ProductType == WinProductType.Workstation) {
                    ProductInfo = WinProductInfo.Professional;
                }
            }
        }

        private static WinArchitecture FromImageFileMachine(ushort imageFileMachine)
        {
            switch (imageFileMachine) {
            case Kernel32.IMAGE_FILE_MACHINE.UNKNOWN: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.TARGET_HOST: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.I386: return WinArchitecture.x86;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS_R3000: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS_R3000_BE: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS_R4000: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS_R10000: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS_WCEMIPSV2: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.ALPHA: return WinArchitecture.Alpha;
            case Kernel32.IMAGE_FILE_MACHINE.ALPHA64: return WinArchitecture.Alpha64;
            case Kernel32.IMAGE_FILE_MACHINE.SH3: return WinArchitecture.SHX;
            case Kernel32.IMAGE_FILE_MACHINE.SH3DSP: return WinArchitecture.SHX;
            case Kernel32.IMAGE_FILE_MACHINE.SH3E: return WinArchitecture.SHX;
            case Kernel32.IMAGE_FILE_MACHINE.SH4: return WinArchitecture.SHX;
            case Kernel32.IMAGE_FILE_MACHINE.SH5: return WinArchitecture.SHX;
            case Kernel32.IMAGE_FILE_MACHINE.ARM: return WinArchitecture.ARM;
            case Kernel32.IMAGE_FILE_MACHINE.ARM_THUMB: return WinArchitecture.ARM;
            case Kernel32.IMAGE_FILE_MACHINE.ARMNT: return WinArchitecture.ARM;
            case Kernel32.IMAGE_FILE_MACHINE.ARM64: return WinArchitecture.ARM64;
            case Kernel32.IMAGE_FILE_MACHINE.AM33: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.POWERPC: return WinArchitecture.PPC;
            case Kernel32.IMAGE_FILE_MACHINE.POWERPCFP: return WinArchitecture.PPC;
            case Kernel32.IMAGE_FILE_MACHINE.IA64: return WinArchitecture.IA64;
            case Kernel32.IMAGE_FILE_MACHINE.MIPS16: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPSFPU: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.MIPSFPU16: return WinArchitecture.Mips;
            case Kernel32.IMAGE_FILE_MACHINE.TRICORE: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.AMD64: return WinArchitecture.x64;
            case Kernel32.IMAGE_FILE_MACHINE.M32R: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.CEF: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.CEE: return WinArchitecture.Unknown;
            case Kernel32.IMAGE_FILE_MACHINE.EBC: return WinArchitecture.Unknown;
            default: return WinArchitecture.Unknown;
            }
        }

        private static WinArchitecture FromProcessorArchitecture(ushort processorArchitecture)
        {
            switch (processorArchitecture) {
            case Kernel32.PROCESSOR_ARCHITECTURE.INTEL: return WinArchitecture.x64;
            case Kernel32.PROCESSOR_ARCHITECTURE.MIPS: return WinArchitecture.Mips;
            case Kernel32.PROCESSOR_ARCHITECTURE.ALPHA: return WinArchitecture.Alpha;
            case Kernel32.PROCESSOR_ARCHITECTURE.PPC: return WinArchitecture.PPC;
            case Kernel32.PROCESSOR_ARCHITECTURE.SHX: return WinArchitecture.SHX;
            case Kernel32.PROCESSOR_ARCHITECTURE.ARM: return WinArchitecture.ARM;
            case Kernel32.PROCESSOR_ARCHITECTURE.IA64: return WinArchitecture.IA64;
            case Kernel32.PROCESSOR_ARCHITECTURE.MSIL: return WinArchitecture.MSIL;
            case Kernel32.PROCESSOR_ARCHITECTURE.AMD64: return WinArchitecture.x64;
            case Kernel32.PROCESSOR_ARCHITECTURE.ARM64: return WinArchitecture.ARM64;
            default: return WinArchitecture.Unknown;
            }
        }

        private void DetectWin10()
        {
            if (MajorVersion != 10) return;

            try {
                RegistryKey rk =
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (rk != null) {
                    object ubrobj = rk.GetValue("UBR");
                    if (ubrobj is int ubr) {
                        UpdateBuildNumber = ubr;
                    }
                }
            } catch (SecurityException) {              // Ignore that we can't access the key
            } catch (UnauthorizedAccessException) {    // Ignore that we can't access the key
            } catch (System.IO.IOException) {          // Should never occur
            } catch (FormatException) {                // Ignore invalid registry value
            } catch (OverflowException) {              // Ignore invalid registry value
            }
        }

        /// <summary>
        /// Calculates the Windows version for a non-exact match.
        /// </summary>
        /// <param name="lastMatch">The last match.</param>
        /// <returns>A string that should be presented to the user.</returns>
        /// <remarks>
        /// This class has an internal database of Windows Versions obtained from MSDN. It might be that the internal
        /// database is out of date, applications can override this to provide more accurate information.
        /// </remarks>
        protected override string CalculateWinVersion(WinVersion lastMatch)
        {
            try {
                string release = WinBrand.BrandingFormatString("%WINDOWS_SHORT%");
                if (!string.IsNullOrWhiteSpace(release)) return release;
            } catch (EntryPointNotFoundException) {
                /* Ignore the exception and use the default implementation */
            }

            return base.CalculateWinVersion(lastMatch);
        }
    }
}
