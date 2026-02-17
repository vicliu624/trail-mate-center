namespace RJCP.Core.Environment.Version
{
    using System;
    using System.Text;
    using Resources;

    /// <summary>
    /// Base class for containing Operating System Version Information for Windows based Systems.
    /// </summary>
    /// <remarks>
    /// This class represents information about a particular Windows Operating System. To get information about your
    /// system, use the <see cref="LocalMachine"/> class.
    /// </remarks>
    [CLSCompliant(false)]
    public class WinVersion : IComparable, ICloneable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class.
        /// </summary>
        public WinVersion() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for a specific Platform ID.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        public WinVersion(WinPlatform platformId)
        {
            m_PlatformId = platformId;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for a specific Platform
        /// ID.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, bool readOnly)
            : this(platformId)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for a specific Windows version.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        public WinVersion(WinPlatform platformId, int majorVersion, int minorVersion)
            : this(platformId)
        {
            m_MajorVersion = majorVersion;
            m_MinorVersion = minorVersion;
            if (platformId == WinPlatform.WinNT && Version52(majorVersion, minorVersion)) {
                m_IsExtendedPropsSet = true;
                m_ProductType = WinProductType.Workstation;
            }
        }

        private static bool Version52(int majorVersion, int minorVersion)
        {
            if (majorVersion >= 6) return true;
            if (majorVersion == 5 && minorVersion >= 2) return true;
            return false;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for a specific Windows
        /// version.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, bool readOnly)
            : this(platformId, majorVersion, minorVersion)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for a specific Windows version with a CSD.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="csd">
        /// A null-terminated string, such as "Service Pack 3", that indicates the latest Service Pack installed on the
        /// system. If no Service Pack has been installed, the string is empty.
        /// </param>
        public WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, string csd)
            : this(platformId, majorVersion, minorVersion)
        {
            m_CSDVersion = csd;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for a specific Windows
        /// version with a CSD.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="csd">
        /// A null-terminated string, such as "Service Pack 3", that indicates the latest Service Pack installed on the
        /// system. If no Service Pack has been installed, the string is empty.
        /// </param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, string csd, bool readOnly)
            : this(platformId, majorVersion, minorVersion, csd)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for a product type.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        public WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType)
        {
            m_PlatformId = platformId;
            m_MajorVersion = majorVersion;
            m_MinorVersion = minorVersion;
            m_IsExtendedPropsSet = true;
            m_ProductType = productType;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for a product type.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType, bool readOnly)
            : this(platformId, majorVersion, minorVersion, productType)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for product type for R2.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="R2"><b>True</b> if this is an R2 release.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        public WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, bool R2, WinProductType productType)
            : this(platformId, majorVersion, minorVersion, productType)
        {
            m_ServerR2 = R2;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for product type for R2.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="R2"><b>True</b> if this is an R2 release.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, bool R2, WinProductType productType, bool readOnly)
            : this(platformId, majorVersion, minorVersion, R2, productType)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class with Service Pack information.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="build">The build number.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType, int build)
            : this(platformId, majorVersion, minorVersion, productType)
        {
            m_BuildNumber = build;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class with Service Pack
        /// information.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="build">The build number.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType, int build, bool readOnly)
            : this(platformId, majorVersion, minorVersion, productType, build)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class with Service Pack information.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="build">The build number.</param>
        /// <param name="spmajor">The service pack major version number.</param>
        /// <param name="spminor">The service pack minor version number.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType, int build, int spmajor, int spminor)
            : this(platformId, majorVersion, minorVersion, productType)
        {
            m_BuildNumber = build;
            m_ServicePackMajor = spmajor;
            m_ServicePackMinor = spminor;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class with Service Pack
        /// information.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="productType">The product type, Server, Workstation, etc.</param>
        /// <param name="build">The build number.</param>
        /// <param name="spmajor">The service pack major version number.</param>
        /// <param name="spminor">The service pack minor version number.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinProductType productType, int build, int spmajor, int spminor, bool readOnly)
            : this(platformId, majorVersion, minorVersion, productType, build, spmajor, spminor)
        {
            IsReadOnly = readOnly;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WinVersion"/> class for a specific architecture.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="arch">The architecture.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinArchitecture arch)
            : this(platformId, majorVersion, minorVersion)
        {
            m_NativeArchitecture = arch;
        }

        /// <summary>
        /// For internal use, initializes a new instance of the <see cref="WinVersion"/> class for a specific
        /// architecture.
        /// </summary>
        /// <param name="platformId">The Platform ID to define.</param>
        /// <param name="majorVersion">The minor version of the OS.</param>
        /// <param name="minorVersion">The major version of the OS.</param>
        /// <param name="arch">The architecture.</param>
        /// <param name="readOnly"><see langword="true"/> if this object is read only after instantiation.</param>
        private WinVersion(WinPlatform platformId, int majorVersion, int minorVersion, WinArchitecture arch, bool readOnly)
            : this(platformId, majorVersion, minorVersion, arch)
        {
            IsReadOnly = readOnly;
        }

        private static readonly object s_Lock = new object();
        private static WinVersionQuery s_Current = null;

        /// <summary>
        /// Get the version of the OS that we're running on.
        /// </summary>
        public static WinVersion LocalMachine
        {
            get
            {
                if (s_Current is null) {
                    lock (s_Lock) {
                        if (s_Current is null) {
                            s_Current = new WinVersionQuery();
                        }
                    }
                }

                return s_Current;
            }
        }

        #region Static properties describing known Operating Systems
        private static readonly WinVersion _Win32s = new WinVersion(WinPlatform.Win32s, 3, -1, true);
        private static readonly WinVersion _WinCE = new WinVersion(WinPlatform.WinCE, 3, -1, true);
        private static readonly WinVersion _Win95 = new WinVersion(WinPlatform.Win9x, 4, 0, "", true);
        private static readonly WinVersion _Win95OSR2 = new WinVersion(WinPlatform.Win9x, 4, 0, "B", true);
        private static readonly WinVersion _Win95OSR2C = new WinVersion(WinPlatform.Win9x, 4, 0, "C", true);
        private static readonly WinVersion _Win98 = new WinVersion(WinPlatform.Win9x, 4, 10, "", true);
        private static readonly WinVersion _Win98SE = new WinVersion(WinPlatform.Win9x, 4, 10, "A", true);
        private static readonly WinVersion _WinME = new WinVersion(WinPlatform.Win9x, 4, 90, true);
        private static readonly WinVersion _WinNT351 = new WinVersion(WinPlatform.WinNT, 3, 51, true);
        private static readonly WinVersion _WinNT4 = new WinVersion(WinPlatform.WinNT, 4, 0, true);
        private static readonly WinVersion _Win2000 = new WinVersion(WinPlatform.WinNT, 5, 0, true);
        private static readonly WinVersion _WinXP = new WinVersion(WinPlatform.WinNT, 5, 1, WinProductType.Workstation, true);
        private static readonly WinVersion _WinXPSP0 = new WinVersion(WinPlatform.WinNT, 5, 1, WinProductType.Workstation, 2600, 0, 0, true);
        private static readonly WinVersion _WinXPSP1 = new WinVersion(WinPlatform.WinNT, 5, 1, WinProductType.Workstation, 2600, 1, 0, true);
        private static readonly WinVersion _WinXPSP2 = new WinVersion(WinPlatform.WinNT, 5, 1, WinProductType.Workstation, 2600, 2, 0, true);
        private static readonly WinVersion _WinXPSP3 = new WinVersion(WinPlatform.WinNT, 5, 1, WinProductType.Workstation, 2600, 3, 0, true);
        private static readonly WinVersion _WinXPx64 = new WinVersion(WinPlatform.WinNT, 5, 2, WinProductType.Workstation, true);
        private static readonly WinVersion _Win2003 = new WinVersion(WinPlatform.WinNT, 5, 2, WinProductType.Server, true);
        private static readonly WinVersion _Win2003R2 = new WinVersion(WinPlatform.WinNT, 5, 2, true, WinProductType.Server, true);
        private static readonly WinVersion _Vista = new WinVersion(WinPlatform.WinNT, 6, 0, WinProductType.Workstation, true);
        private static readonly WinVersion _Win2008 = new WinVersion(WinPlatform.WinNT, 6, 0, WinProductType.Server, true);
        private static readonly WinVersion _Win7 = new WinVersion(WinPlatform.WinNT, 6, 1, WinProductType.Workstation, true);
        private static readonly WinVersion _Win2008R2 = new WinVersion(WinPlatform.WinNT, 6, 1, WinProductType.Server, true);
        private static readonly WinVersion _Win8 = new WinVersion(WinPlatform.WinNT, 6, 2, WinProductType.Workstation, true);
        private static readonly WinVersion _WinRT = new WinVersion(WinPlatform.WinNT, 6, 2, WinArchitecture.ARM, true);
        private static readonly WinVersion _Win2012 = new WinVersion(WinPlatform.WinNT, 6, 2, WinProductType.Server, true);
        private static readonly WinVersion _Win8_1 = new WinVersion(WinPlatform.WinNT, 6, 3, WinProductType.Workstation, true);
        private static readonly WinVersion _Win8_1RT = new WinVersion(WinPlatform.WinNT, 6, 3, WinArchitecture.ARM, true);
        private static readonly WinVersion _Win2012R2 = new WinVersion(WinPlatform.WinNT, 6, 3, WinProductType.Server, true);
        private static readonly WinVersion _Win10 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, true);
        private static readonly WinVersion _Win10_1507 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 10240, true);
        private static readonly WinVersion _Win10_1511 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 10586, true);
        private static readonly WinVersion _Win10_1607 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 14393, true);
        private static readonly WinVersion _Win10_1703 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 15063, true);
        private static readonly WinVersion _Win10_1709 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 16299, true);
        private static readonly WinVersion _Win10_1803 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 17134, true);
        private static readonly WinVersion _Win10_1809 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 17763, true);
        private static readonly WinVersion _Win10_1903 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 18362, true);
        private static readonly WinVersion _Win10_1909 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 18363, true);
        private static readonly WinVersion _Win10_2004 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 19041, true);
        private static readonly WinVersion _Win10_20H2 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 19042, true);
        private static readonly WinVersion _Win10_21H1 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 19043, true);
        private static readonly WinVersion _Win10_21H2 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 19044, true);
        private static readonly WinVersion _Win10_22H2 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 19045, true);
        private static readonly WinVersion _Win11_21H2 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 22000, true);
        private static readonly WinVersion _Win11_22H2 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Workstation, 22621, true);
        private static readonly WinVersion _Win2016 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Server, 14393, true);
        private static readonly WinVersion _Win2019 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Server, 17763, true);
        private static readonly WinVersion _Win2022 = new WinVersion(WinPlatform.WinNT, 10, 0, WinProductType.Server, 22000, true);

        private sealed class WinVersionLookupEntry
        {
            public readonly WinVersion OSVersion;
            public readonly string WinVersionString;

            public WinVersionLookupEntry(WinVersion version, string verstring)
            {
                OSVersion = version;
                WinVersionString = verstring;
            }
        }

        /// <summary>
        /// The WinVersionString lookup database.
        /// </summary>
        /// <remarks>
        /// Entries in the database are searched from top to bottom. Items marked as Unknown, -1, Empty, Unknown or
        /// false are not compared. The first entry is taken and returned. An OSProductType of DomainController will
        /// also match OSProductType.Server for convenience. See <see cref="CalculateWinVersion()"/> for how to sort this
        /// table.
        /// </remarks>
        private static readonly WinVersionLookupEntry[] WinVersionDatabase = new WinVersionLookupEntry[] {
            new WinVersionLookupEntry(new WinVersion(WinPlatform.Win32s, true), "Windows 32s"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinCE, true), "Windows 32s"),
            new WinVersionLookupEntry(_Win95, "Windows 95"),
            new WinVersionLookupEntry(_Win95OSR2, "Windows 95OSR2"),
            new WinVersionLookupEntry(_Win95OSR2C, "Windows 95OSR2"),
            new WinVersionLookupEntry(_Win98, "Windows 98"),
            new WinVersionLookupEntry(_Win98SE, "Windows 98SE"),
            new WinVersionLookupEntry(_WinME, "Windows ME"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.Win9x, 4, -1, true), "Windows 9x"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.Win9x, true), "Windows 9x"),
            new WinVersionLookupEntry(_WinNT351, "Windows NT 3.51"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 3, -1, true), "Windows NT 3.x"),
            new WinVersionLookupEntry(_WinNT4, "Windows NT 4"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 4, -1, true), "Windows NT 4.x"),
            new WinVersionLookupEntry(_Win2000, "Windows 2000"),
            new WinVersionLookupEntry(_WinXP, "Windows XP"),
            new WinVersionLookupEntry(_Win2003R2, "Windows 2003R2"),
            new WinVersionLookupEntry(_Win2003, "Windows 2003"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 5, 2, true), "Windows XP/2003"),
            new WinVersionLookupEntry(_WinXPx64, "Windows XP"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 5, -1, true), "Windows NT 5.x"),
            new WinVersionLookupEntry(_Vista, "Windows Vista"),
            new WinVersionLookupEntry(_Win2008, "Windows 2008"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 6, 0, true), "Windows Vista/2008"),
            new WinVersionLookupEntry(_Win7, "Windows 7"),
            new WinVersionLookupEntry(_Win2008R2, "Windows 2008R2"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 6, 1, true), "Windows 7/2008R2"),
            new WinVersionLookupEntry(_WinRT, "Windows RT"),
            new WinVersionLookupEntry(_Win8, "Windows 8"),
            new WinVersionLookupEntry(_Win2012, "Windows 2012"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 6, 2, true), "Windows 8/2012"),
            new WinVersionLookupEntry(_Win8_1RT, "Windows 8.1 RT"),
            new WinVersionLookupEntry(_Win8_1, "Windows 8.1"),
            new WinVersionLookupEntry(_Win2012R2, "Windows 2012R2"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 6, 3, true), "Windows 8.1/2012R2"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 6, -1, true), "Windows NT 6.x"),
            new WinVersionLookupEntry(_Win10_1507, "Windows 10 v1507 - Threshold 1"),
            new WinVersionLookupEntry(_Win10_1511, "Windows 10 v1511 - Threshold 2"),
            new WinVersionLookupEntry(_Win10_1607, "Windows 10 v1607 - Redstone 1"),
            new WinVersionLookupEntry(_Win2016, "Windows 10 Server 2016"),
            new WinVersionLookupEntry(_Win10_1703, "Windows 10 v1703 - Redstone 2"),
            new WinVersionLookupEntry(_Win10_1709, "Windows 10 v1709 - Redstone 3"),
            new WinVersionLookupEntry(_Win10_1803, "Windows 10 v1803 - Redstone 4"),
            new WinVersionLookupEntry(_Win10_1809, "Windows 10 v1809 - Redstone 5"),
            new WinVersionLookupEntry(_Win2019, "Windows 10 Server 2019"),
            new WinVersionLookupEntry(_Win10_1903, "Windows 10 v1903"),
            new WinVersionLookupEntry(_Win10_1909, "Windows 10 v1909"),
            new WinVersionLookupEntry(_Win10_2004, "Windows 10 v2004"),
            new WinVersionLookupEntry(_Win10_20H2, "Windows 10 v20H2"),
            new WinVersionLookupEntry(_Win10_21H1, "Windows 10 v21H1"),
            new WinVersionLookupEntry(_Win10_21H2, "Windows 10 v21H2"),
            new WinVersionLookupEntry(_Win10_22H2, "Windows 10 v22H2"),
            new WinVersionLookupEntry(_Win11_21H2, "Windows 11 v21H2"),
            new WinVersionLookupEntry(_Win2022, "Windows 11 Server 2022"),
            new WinVersionLookupEntry(_Win11_22H2, "Windows 11 v22H2"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, 10, -1, true), "Windows NT 10.x"),
            new WinVersionLookupEntry(new WinVersion(WinPlatform.WinNT, true), "Windows NT"),
        };

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Win32S.
        /// </summary>
        public static WinVersion Win32s { get { return _Win32s; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows CE.
        /// </summary>
        public static WinVersion WinCE { get { return _WinCE; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 95.
        /// </summary>
        public static WinVersion Win95 { get { return _Win95; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 95 OSR2.
        /// </summary>
        public static WinVersion Win95OSR2 { get { return _Win95OSR2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 98.
        /// </summary>
        public static WinVersion Win98 { get { return _Win98; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 98SE.
        /// </summary>
        public static WinVersion Win98SE { get { return _Win98SE; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows ME.
        /// </summary>
        public static WinVersion WinME { get { return _WinME; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows NT 3.51.
        /// </summary>
        public static WinVersion WinNT351 { get { return _WinNT351; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows NT 4.0.
        /// </summary>
        public static WinVersion WinNT4 { get { return _WinNT4; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 2000.
        /// </summary>
        public static WinVersion Win2000 { get { return _Win2000; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 32-bit.
        /// </summary>
        public static WinVersion WinXP { get { return _WinXP; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 32-bit.
        /// </summary>
        public static WinVersion WinXPSP0 { get { return _WinXPSP0; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 32-bit.
        /// </summary>
        public static WinVersion WinXPSP1 { get { return _WinXPSP1; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 32-bit.
        /// </summary>
        public static WinVersion WinXPSP2 { get { return _WinXPSP2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 32-bit.
        /// </summary>
        public static WinVersion WinXPSP3 { get { return _WinXPSP3; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows XP 64-bit.
        /// </summary>
        public static WinVersion WinXPx64 { get { return _WinXPx64; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2003.
        /// </summary>
        public static WinVersion Win2003 { get { return _Win2003; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2003 R2.
        /// </summary>
        public static WinVersion Win2003R2 { get { return _Win2003R2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Vista.
        /// </summary>
        public static WinVersion Vista { get { return _Vista; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2008.
        /// </summary>
        public static WinVersion Win2008 { get { return _Win2008; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 7.
        /// </summary>
        public static WinVersion Win7 { get { return _Win7; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2008 R2.
        /// </summary>
        public static WinVersion Win2008R2 { get { return _Win2008R2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 8.
        /// </summary>
        public static WinVersion Win8 { get { return _Win8; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2012.
        /// </summary>
        public static WinVersion Win2012 { get { return _Win2012; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 8.1.
        /// </summary>
        public static WinVersion Win8_1 { get { return _Win8_1; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2012 R2.
        /// </summary>
        public static WinVersion Win2012R2 { get { return _Win2012R2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10 { get { return _Win10; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1507 { get { return _Win10_1507; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1511 { get { return _Win10_1511; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1607 { get { return _Win10_1607; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1703 { get { return _Win10_1703; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1709 { get { return _Win10_1709; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1803 { get { return _Win10_1803; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1809 { get { return _Win10_1809; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1903 { get { return _Win10_1903; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_1909 { get { return _Win10_1909; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_2004 { get { return _Win10_2004; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_20H2 { get { return _Win10_20H2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_21H1 { get { return _Win10_21H1; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_21H2 { get { return _Win10_21H2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 10.
        /// </summary>
        public static WinVersion Windows10_22H2 { get { return _Win10_22H2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2016.
        /// </summary>
        public static WinVersion Win2016 { get { return _Win2016; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2019.
        /// </summary>
        public static WinVersion Win2019 { get { return _Win2019; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 11 22H1.
        /// </summary>
        public static WinVersion Windows11_21H2 { get { return _Win11_21H2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows 11 22H2.
        /// </summary>
        public static WinVersion Windows11_22H2 { get { return _Win11_22H2; } }

        /// <summary>
        /// A predefined <see cref="WinVersion"/> object identifying Windows Server 2022.
        /// </summary>
        public static WinVersion Win2022 { get { return _Win2022; } }
        #endregion

        #region Properties
        private WinPlatform m_PlatformId = WinPlatform.Unknown;

        /// <summary>
        /// The Operating System Platform ID.
        /// </summary>
        public WinPlatform PlatformId
        {
            get { return m_PlatformId; }
            set
            {
                CheckLock(nameof(PlatformId));
                m_PlatformId = value;
                m_WinVersionInfo = null;
            }
        }

        private int m_MajorVersion = -1;

        /// <summary>
        /// The Operating System Major Version number.
        /// </summary>
        public int MajorVersion
        {
            get { return m_MajorVersion; }
            set
            {
                CheckLock(nameof(MajorVersion));
                m_MajorVersion = value;
                m_WinVersionInfo = null;
            }
        }

        private int m_MinorVersion = -1;

        /// <summary>
        /// The Operating System Minor Version number.
        /// </summary>
        public int MinorVersion
        {
            get { return m_MinorVersion; }
            set
            {
                CheckLock(nameof(MinorVersion));
                m_MinorVersion = value;
                m_WinVersionInfo = null;
            }
        }

        private int m_BuildNumber = -1;

        /// <summary>
        /// The Operating System Version Build Number.
        /// </summary>
        public int BuildNumber
        {
            get { return m_BuildNumber; }
            set
            {
                CheckLock(nameof(BuildNumber));
                m_BuildNumber = value;
                m_WinVersionInfo = null;
            }
        }

        private int m_UpdateBuildNumber = -1;

        /// <summary>
        /// Gets or sets the update build number, as an alternative to the service pack information.
        /// </summary>
        /// <value>The update build number.</value>
        /// <remarks>
        /// Windows 10 and later doesn't use Service Pack information, instead a "UBR" value.
        /// </remarks>
        public int UpdateBuildNumber
        {
            get { return m_UpdateBuildNumber; }
            set
            {
                CheckLock(nameof(UpdateBuildNumber));
                m_UpdateBuildNumber = value;
                m_WinVersionInfo = null;
            }
        }

        private string m_CSDVersion;

        /// <summary>
        /// The Operating System extension, such as Service Pack or update details as a string.
        /// </summary>
        public string CSDVersion
        {
            get { return m_CSDVersion; }
            set
            {
                CheckLock(nameof(CSDVersion));
                m_CSDVersion = value;
                m_WinVersionInfo = null;
            }
        }

        private WinArchitecture m_NativeArchitecture = WinArchitecture.Unknown;

        /// <summary>
        /// The Operating System Architecture.
        /// </summary>
        public WinArchitecture NativeArchitecture
        {
            get { return m_NativeArchitecture; }
            set
            {
                CheckLock(nameof(NativeArchitecture));
                m_NativeArchitecture = value;
                m_WinVersionInfo = null;
            }
        }

        private WinArchitecture m_Architecture = WinArchitecture.Unknown;

        /// <summary>
        /// The Architecture for the Process.
        /// </summary>
        public WinArchitecture Architecture
        {
            get { return m_Architecture; }
            set
            {
                CheckLock(nameof(Architecture));
                m_Architecture = value;
                m_WinVersionInfo = null;
            }
        }

        private WinSuite m_SuiteFlags = WinSuite.None;

        /// <summary>
        /// The Operating System Suite flags (variant for configuration differences).
        /// </summary>
        public WinSuite SuiteFlags
        {
            get { return m_SuiteFlags; }
            set
            {
                CheckLock(nameof(SuiteFlags));
                m_SuiteFlags = value;
                m_WinVersionInfo = null;
            }
        }

        private WinProductType m_ProductType = WinProductType.Unknown;

        /// <summary>
        /// The Operating System Product Type, Server, Workstation, Domain Controller, etc.
        /// </summary>
        public WinProductType ProductType
        {
            get { return m_ProductType; }
            set
            {
                CheckLock(nameof(ProductType));
                m_ProductType = value;
                m_IsExtendedPropsSet = true;
                m_WinVersionInfo = null;
            }
        }

        /// <summary>
        /// Returns if the current object represents a server.
        /// </summary>
        /// <remarks>
        /// If the Operating system is configured as a DomainController or a Server, this property returns
        /// <see cref="WinVersion"/>.
        /// </remarks>
        public bool IsServer
        {
            get { return m_ProductType == WinProductType.Server || m_ProductType == WinProductType.DomainController; }
        }

        private int m_ServicePackMajor = -1;

        /// <summary>
        /// The Operating System Major Service Pack number.
        /// </summary>
        public int ServicePackMajor
        {
            get { return m_ServicePackMajor; }
            set
            {
                CheckLock(nameof(ServicePackMajor));
                m_ServicePackMajor = value;
                m_IsExtendedPropsSet = true;
                m_WinVersionInfo = null;
            }
        }

        private int m_ServicePackMinor = -1;

        /// <summary>
        /// The Operating System Minor Service Pack number.
        /// </summary>
        public int ServicePackMinor
        {
            get { return m_ServicePackMinor; }
            set
            {
                CheckLock(nameof(ServicePackMinor));
                m_ServicePackMinor = value;
                m_IsExtendedPropsSet = true;
                m_WinVersionInfo = null;
            }
        }

        private bool m_ServerR2;

        /// <summary>
        /// The Operating System release type, if this is an R2 release (Windows 2003 only).
        /// </summary>
        public bool ServerR2
        {
            get { return m_ServerR2; }
            set
            {
                CheckLock(nameof(ServerR2));
                m_ServerR2 = value;
                m_WinVersionInfo = null;
            }
        }

        private WinProductInfo m_ProductInfo = WinProductInfo.Undefined;

        /// <summary>
        /// The Operating System Product Information flags.
        /// </summary>
        public WinProductInfo ProductInfo
        {
            get { return m_ProductInfo; }
            set
            {
                CheckLock(nameof(ProductInfo));
                m_ProductInfo = value;
                m_WinVersionInfo = null;
            }
        }
        #endregion

        /// <summary>
        /// The Operating System as a Version string.
        /// </summary>
        /// <remarks>
        /// The Major and Minor version numbers correspond to the Operating System major and minor number. For example,
        /// Windows 7 has a version of "6.2". If the build number is provided, this is given as the third digit. If
        /// there is a service pack version, this is provided in the fourth field, with the lowest byte being the
        /// service pack minor version, the upper byte being the service pack major number.
        /// </remarks>
        public Version Version
        {
            get
            {
                if (m_MajorVersion < 0 || m_MinorVersion < 0) return new Version();
                if (m_BuildNumber < 0) return new Version(m_MajorVersion, m_MinorVersion);
                if (m_UpdateBuildNumber >= 0) return new Version(m_MajorVersion, m_MinorVersion, m_BuildNumber, m_UpdateBuildNumber);
                if (m_ServicePackMajor >= 0 && m_ServicePackMinor >= 0)
                    return new Version(m_MajorVersion, m_MinorVersion, m_BuildNumber, (m_ServicePackMajor << 8) | m_ServicePackMinor);
                return new Version(m_MajorVersion, m_MinorVersion, m_BuildNumber);
            }
        }

        /// <summary>
        /// Get the Operating System version as a string.
        /// </summary>
        public string VersionString
        {
            get
            {
                Version v = Version;
                if (m_UpdateBuildNumber >= 0) return v.ToString();

                Version vo = new Version(v.Major, v.Minor, v.Build, 0);
                return vo.ToString();
            }
        }

        /// <summary>
        /// Get the platform ID of this OSVersion object as a string.
        /// </summary>
        public string PlatformIdString
        {
            get
            {
                switch (m_PlatformId) {
                case WinPlatform.Win32s: return "Windows 32s";
                case WinPlatform.WinCE: return "Windows CE";
                case WinPlatform.Win9x: return "Windows 9x";
                case WinPlatform.WinNT: return "Windows NT";
                default: return string.Empty;
                }
            }
        }

        /// <summary>
        /// Test if this <see cref="WinVersion"/> object is of a particular OS Suite.
        /// </summary>
        /// <param name="test">The flags to test for.</param>
        /// <returns><see langword="true"/> if the flag requested is set; <see langword="false"/> otherwise.</returns>
        public bool SuiteFlag(WinSuite test)
        {
            return (m_SuiteFlags & test) > 0;
        }

        /// <summary>
        /// Get the Suite as a string for this <see cref="WinVersion"/> object.
        /// </summary>
        public string SuiteString
        {
            get
            {
                StringBuilder sb = new StringBuilder();

                if (SuiteFlag(WinSuite.SmallBusiness)) SuiteStringAdd(sb, "Small Business");
                if (SuiteFlag(WinSuite.Enterprise)) {
                    if (this == _Win2000) {
                        SuiteStringAdd(sb, "Advanced");
                    } else {
                        SuiteStringAdd(sb, "Enterprise");
                    }
                }
                if (SuiteFlag(WinSuite.BackOffice)) SuiteStringAdd(sb, "BackOffice");
                if (SuiteFlag(WinSuite.Communications)) SuiteStringAdd(sb, "Communications");
                if (SuiteFlag(WinSuite.Terminal)) SuiteStringAdd(sb, "Terminal Services");
                if (SuiteFlag(WinSuite.SmallBusinessRestricted)) SuiteStringAdd(sb, "Small Business Restricted");
                if (SuiteFlag(WinSuite.EmbeddedNT)) SuiteStringAdd(sb, "Embedded");
                if (SuiteFlag(WinSuite.Datacenter)) SuiteStringAdd(sb, "Datacenter");
                if (SuiteFlag(WinSuite.Personal)) SuiteStringAdd(sb, "Home Edition");
                if (SuiteFlag(WinSuite.Blade)) SuiteStringAdd(sb, "Web Edition");
                if (SuiteFlag(WinSuite.EmbeddedRestricted)) SuiteStringAdd(sb, "Embedded Restricted");
                if (SuiteFlag(WinSuite.SecurityAppliance)) SuiteStringAdd(sb, "Security Appliance");
                if (SuiteFlag(WinSuite.StorageServer)) SuiteStringAdd(sb, "Storage Server");
                if (SuiteFlag(WinSuite.ComputeServer)) SuiteStringAdd(sb, "Compute Server");
                if (SuiteFlag(WinSuite.HomeServer)) SuiteStringAdd(sb, "Home Server");
                return sb.ToString();
            }
        }

        private static void SuiteStringAdd(StringBuilder sb, string suite)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(suite);
        }

        /// <summary>
        /// Get the Product Type as a string for this object.
        /// </summary>
        public string ProductTypeString
        {
            get
            {
                switch (m_ProductType) {
                case WinProductType.Workstation:
                    if (this == _WinNT4) {
                        return "Workstation";
                    } else if (this == _Win2000) {
                        return "Professional";
                    }
                    return string.Empty;

                case WinProductType.DomainController: return "Domain Controller";

                case WinProductType.Server: return "Server";

                default: return string.Empty;
                }
            }
        }

        /// <summary>
        /// Get the Product Info as a string for this object.
        /// </summary>
        public string ProductInfoString
        {
            get
            {
                switch (m_ProductInfo) {
                case WinProductInfo.Undefined: return string.Empty;

                case WinProductInfo.MediaCenter: return "MediaCenter";

                case WinProductInfo.Starter: return "Starter";
                case WinProductInfo.Starter_E: return "Starter E";
                case WinProductInfo.Starter_N: return "Starter N";
                case WinProductInfo.Home_Basic: return "Home Basic";
                case WinProductInfo.Home_Basic_E: return "Home Basic E";
                case WinProductInfo.Home_Basic_N: return "Home Basic N";
                case WinProductInfo.Home_Premium: return "Home Premium";
                case WinProductInfo.Home_Premium_E: return "Home Premium E";
                case WinProductInfo.Home_Premium_N: return "Home Premium N";
                case WinProductInfo.Professional: return "Professional";
                case WinProductInfo.Professional_E: return "Professional E";
                case WinProductInfo.Professional_N: return "Professional N";
                case WinProductInfo.Professional_Student: return "Professional Student";
                case WinProductInfo.Professional_Student_N: return "Professional Student N";
                case WinProductInfo.Professional_S: return "Professional S";
                case WinProductInfo.Professional_S_N: return "Professional S N";
                case WinProductInfo.Pro_Single_Language: return "Professional Single Language";
                case WinProductInfo.Pro_China: return "Professional China";
                case WinProductInfo.Pro_Workstation: return "Professional Workstation";
                case WinProductInfo.Pro_Workstation_N: return "Professional Workstation N";
                case WinProductInfo.Pro_For_Education: return "Professional Education";
                case WinProductInfo.Pro_For_Education_N: return "Professional Education N";
                case WinProductInfo.Business: return "Business";
                case WinProductInfo.Business_N: return "Business N";
                case WinProductInfo.Ultimate: return "Ultimate";
                case WinProductInfo.Ultimate_E: return "Ultimate E";
                case WinProductInfo.Ultimate_N: return "Ultimate N";
                case WinProductInfo.Enterprise: return "Enterprise";
                case WinProductInfo.Enterprise_E: return "Enterprise E";
                case WinProductInfo.Enterprise_N: return "Enterprise N";
                case WinProductInfo.Enterprise_S: return "Enterprise S";
                case WinProductInfo.Enterprise_S_N: return "Enterprise S N";
                case WinProductInfo.Enterprise_Evaluation: return "Enterprise (Eval)";
                case WinProductInfo.Enterprise_N_Evaluation: return "Enterprise N (Eval)";
                case WinProductInfo.Enterprise_S_Eval: return "Enterprise S (Eval)";
                case WinProductInfo.Enterprise_S_N_Eval: return "Enterprise S N (Eval)";
                case WinProductInfo.Enterprise_Subscription: return "Enterprise Subscription";
                case WinProductInfo.Enterprise_Subscription_N: return "Enterprise Subscription N";
                case WinProductInfo.Enterprise_G: return "Enterprise G";
                case WinProductInfo.Enterprise_G_N: return "Enterprise G N";
                case WinProductInfo.Education: return "Education";
                case WinProductInfo.Education_N: return "Education N";
                case WinProductInfo.Core: return "Windows 8";
                case WinProductInfo.Core_N: return "Windows 8 N";
                case WinProductInfo.Core_Arm: return "Windows Core ARM";
                case WinProductInfo.Core_CountrySpecific: return "Windows 8 China";
                case WinProductInfo.Core_SingleLanguage: return "Windows 8 Single Language";
                case WinProductInfo.Core_Connected: return "Windows Core Connected";
                case WinProductInfo.Core_Connected_N: return "Windows Core Connected N";
                case WinProductInfo.Core_Connected_Country_Specific: return "Windows Core Connected China";
                case WinProductInfo.Core_Connected_Single_Language: return "Windows Core Connected Single Language";
                case WinProductInfo.Professional_Wmc: return "Professional with Media Center";

                case WinProductInfo.Standard_Server: return "Standard Server";
                case WinProductInfo.Standard_Server_Core: return "Standard Server (Core)";
                case WinProductInfo.Standard_Server_Core_V: return "Standard Server (Core, no Hyper-V)";
                case WinProductInfo.Standard_Server_V: return "Standard Server (no Hyper-V)";
                case WinProductInfo.Standard_Server_Solutions: return "Server Solutions Premium";
                case WinProductInfo.Standard_Server_Solutions_Core: return "Server Solutions Premium (Core)";
                case WinProductInfo.Standard_Evaluation_Server: return "Standard Server (Eval)";
                case WinProductInfo.Standard_Eval_Server_Core: return "Standard Server (Core, Eval)";
                case WinProductInfo.Standard_A_Server_Core: return "Standard Server A (Core)";
                case WinProductInfo.Standard_WS_Server_Core: return "Standard Server WS (Core)";

                case WinProductInfo.MediumBusiness_Server_Management: return "Business Management Server";
                case WinProductInfo.MediumBusiness_Server_Messaging: return "Business Messaging Server";
                case WinProductInfo.MediumBusiness_Server_Security: return "Business Security Server";

                case WinProductInfo.DataCenter_Server: return "DataCenter";
                case WinProductInfo.DataCenter_Server_Core: return "DataCenter (Core)";
                case WinProductInfo.DataCenter_Server_Core_V: return "DataCenter (Core, no Hyper-V)";
                case WinProductInfo.DataCenter_Server_V: return "DataCenter (no Hyper-V)";
                case WinProductInfo.Datacenter_Evaluation_Server: return "DataCenter (Eval)";
                case WinProductInfo.Datacenter_Eval_Server_Core: return "DataCenter (Core, Eval)";
                case WinProductInfo.Datacenter_A_Server_Core: return "DataCenter A (Core)";
                case WinProductInfo.Datacenter_WS_Server_Core: return "DataCenter WS (Core)";
                case WinProductInfo.Datacenter_Server_Azure_Edition: return "DataCenter Azure";
                case WinProductInfo.Datacenter_Server_Core_Azure_Edition: return "DataCenter Azure (Core)";

                case WinProductInfo.Enterprise_Server: return "Enterprise Server";
                case WinProductInfo.Enterprise_Server_Core: return "Enterprise Server (Core)";
                case WinProductInfo.Enterprise_Server_Core_V: return "Enterprise Server (Core, no Hyper-V)";
                case WinProductInfo.Enterprise_Server_V: return "Enterprise Server (no Hyper-V)";
                case WinProductInfo.Enterprise_Server_IA64: return "Enterprise Server Itanium";

                case WinProductInfo.Server_RDSH: return "RDSH Server";

                case WinProductInfo.Home_Server: return "Storage Server 2008R2 Essentials";
                case WinProductInfo.Home_Premium_Server: return "Windows Home Server 2011";
                case WinProductInfo.Arm64_Server: return "Arm64 Server";
                case WinProductInfo.Storage_Enterprise_Server: return "Storage Enterprise Server";
                case WinProductInfo.Storage_Enterprise_Core: return "Storage Enterprise Server (Core)";
                case WinProductInfo.Storage_Express_Server: return "Storage Express Server";
                case WinProductInfo.Storage_Express_Server_Core: return "Storage Express Server (Core)";
                case WinProductInfo.Storage_Standard_Server: return "Storage Standard Server";
                case WinProductInfo.Storage_Standard_Server_Core: return "Storage Standard Server (Core)";
                case WinProductInfo.Storage_Standard_Evaluation_Server: return "Storage Standard Server (Eval)";
                case WinProductInfo.Storage_Workgroup_Server: return "Storage Workgroup Server";
                case WinProductInfo.Storage_Workgroup_Server_Core: return "Storage Workgroup Server (Core)";
                case WinProductInfo.Storage_Workgroup_Evaluation_Server: return "Storage Workgroup Server (Eval)";

                case WinProductInfo.Web_Server: return "Web Server";
                case WinProductInfo.Web_Server_Core: return "Web Server (Core)";

                case WinProductInfo.SmallBusiness_Server: return "Small Business Server";
                case WinProductInfo.SmallBusiness_Server_Premium: return "Small Business Server Premium";
                case WinProductInfo.SmallBusiness_Server_Premium_Core: return "Small Business Server Premium (Core)";
                case WinProductInfo.Server_For_SmallBusiness: return "Windows Server 2008 for Windows Essential Server Solutions";
                case WinProductInfo.Server_For_SmallBusiness_V: return "Windows Server 2008 for Windows Essential Server Solutions (no Hyper-V)";
                case WinProductInfo.Sb_Solution_Server: return "Windows Small Business Server 2011 Essentials";
                case WinProductInfo.Server_For_Sb_Solutions: return "Server for SB Solutions";
                case WinProductInfo.Sb_Solution_Server_Em: return "Server for SB Solutions EM";
                case WinProductInfo.Server_For_Sb_Solutions_Em: return "Server for SB Solutions EM";

                case WinProductInfo.Cluster_Server: return "HPC Edition";
                case WinProductInfo.Cluster_Server_V: return "HPC Edition (no Hyper-V)";
                case WinProductInfo.HyperV: return "Hyper-V Edition";
                case WinProductInfo.Server_Foundation: return "Server Foundation";
                case WinProductInfo.One_Core_Update_OS: return "One Core Update OS";

                case WinProductInfo.Cloud_Storage_Server: return "Cloud Storage Server";
                case WinProductInfo.Cloud_Host_Infrastructure_Server: return "Cloud Host Infrastructure Server";
                case WinProductInfo.Cloud: return "Cloud";
                case WinProductInfo.Cloud_N: return "Cloud N";
                case WinProductInfo.Cloud_E: return "Cloud E";
                case WinProductInfo.Cloud_E_N: return "Cloud E N";
                case WinProductInfo.CloudEdition: return "Cloud Edition";
                case WinProductInfo.CloudEdition_N: return "Cloud Edition N";

                case WinProductInfo.Azure_Server_Core: return "Azure Server (Core)";
                case WinProductInfo.Azure_Server_CloudHost: return "Azure Server Cloud Host";
                case WinProductInfo.Azure_Server_CloudMos: return "Azure Server Cloud MOS";
                case WinProductInfo.AzureStackHci_Server_Core: return "Azure Stack HCI Server (Core)";

                case WinProductInfo.Embedded: return "Windows Embedded";
                case WinProductInfo.Embedded_A: return "Windows Embedded A";
                case WinProductInfo.Embedded_E: return "Windows Embedded E";
                case WinProductInfo.Embedded_Eval: return "Windows Embedded Evaluation";
                case WinProductInfo.Embedded_E_Eval: return "Windows Embedded E Evaluation";
                case WinProductInfo.Embedded_Automotive: return "Windows Embedded Automotive";
                case WinProductInfo.Embedded_Industry: return "Windows Embedded Industry";
                case WinProductInfo.Embedded_Industry_A: return "Windows Embedded Industry A";
                case WinProductInfo.Embedded_Industry_E: return "Windows Embedded Industry E";
                case WinProductInfo.Embedded_Industry_A_E: return "Windows Embedded Industry A E";
                case WinProductInfo.Embedded_Industry_Eval: return "Windows Embedded Industry Evaluation";
                case WinProductInfo.Embedded_E_Industry_Eval: return "Windows Embedded Industry E Evaluation";
                case WinProductInfo.Connected_Car: return "Connected Car";
                case WinProductInfo.Solution_EmbeddedServer: return "Windows Multipoint Server";
                case WinProductInfo.Solution_EmbeddedServerCore: return "Windows Multipoint Server Core";
                case WinProductInfo.Multipoint_Standard_Server: return "Windows Multipoint Server Standard";
                case WinProductInfo.Multipoint_Premium_Server: return "Windows Multipoint Server Premium";
                case WinProductInfo.Professional_Embedded: return "Windows Professional Embedded";
                case WinProductInfo.EssentialBusiness_Server_Mgmt: return "Windows Essential Server Solution Management";
                case WinProductInfo.EssentialBusiness_Server_Addl: return "Windows Essential Server Solution Additional";
                case WinProductInfo.EssentialBusiness_Server_MgmtSvc: return "Windows Essential Server Solution Management SVC";
                case WinProductInfo.EssentialBusiness_Server_AddlSvc: return "Windows Essential Server Solution Additional SVC";
                case WinProductInfo.Industry_Handheld: return "Industry Handheld";
                case WinProductInfo.PPI_Pro: return "PPI Professional";
                case WinProductInfo.IOTUAP: return "IOTUAP";
                case WinProductInfo.ThinPC: return "Thin PC";
                case WinProductInfo.Holographic: return "Holographic";
                case WinProductInfo.Holographic_Business: return "Holographic Business";
                case WinProductInfo.Utility_VM: return "Utility VM";
                case WinProductInfo.HubOS: return "HubOS";
                case WinProductInfo.Andromeda: return "Andromeda";
                case WinProductInfo.Lite: return "Lite";
                case WinProductInfo.TabletPc: return "Tablet PC";
                case WinProductInfo.IOT_OS: return "IoT OS";
                case WinProductInfo.IOT_EDGEOS: return "IoT Edge OS";
                case WinProductInfo.IOT_Enterprise: return "IoT Enterprise";
                case WinProductInfo.IOT_Enterprise_S: return "IoT Enterprise S";

                case WinProductInfo.Nano_Server: return "Windows Nano Server";
                case WinProductInfo.Datacenter_Nano_Server: return "Datacenter Nano Server";
                case WinProductInfo.Standard_Nano_Server: return "Standard Nano Server";
                case WinProductInfo.Azure_Nano_Server: return "Azure Nano Server";

                case WinProductInfo.XBox_System_OS: return "X-Box System OS";
                case WinProductInfo.XBox_Native_OS: return "X-Box Native OS";
                case WinProductInfo.XBox_Game_OS: return "X-Box Game OS";
                case WinProductInfo.XBox_Era_OS: return "X-Box Era OS";
                case WinProductInfo.XBox_Durango_Host_OS: return "X-Box Durango OS";
                case WinProductInfo.XBox_Scarlett_Host_OS: return "X-Box Scarlett OS";
                case WinProductInfo.XBox_Keystone: return "X-Box Keystone";

                case WinProductInfo.Unlicensed: return "Unlicensed";
                default: return string.Empty;
                }
            }
        }

        private bool IsSameOSKernelType(WinVersion compare)
        {
            if (compare.PlatformId == WinPlatform.Unknown) return false;
            if (compare.ProductType == WinProductType.Unknown) return false;
            if (compare.MajorVersion == -1 || m_MajorVersion == -1) return false;
            if (compare.MinorVersion == -1 || m_MinorVersion == -1) return false;
            if (compare.BuildNumber == -1 && m_BuildNumber != -1) return false;

            if (compare.PlatformId != m_PlatformId) return false;
            if (compare.MajorVersion != m_MajorVersion) return false;
            if (compare.MinorVersion != m_MinorVersion) return false;

            switch (compare.ProductType) {
            case WinProductType.Server:
            case WinProductType.DomainController:
                if (m_ProductType != WinProductType.Server && m_ProductType != WinProductType.DomainController) return false;
                break;
            case WinProductType.Workstation:
                if (m_ProductType != WinProductType.Workstation) return false;
                break;
            default:
                return false;
            }

            if (compare.BuildNumber > m_BuildNumber) return false;

            return true;
        }

        private string CalculateWinVersion()
        {
            WinVersion lastMatch = null;

            // When searching, we start from the earliest to the latest, looking for the closest match.
            // * An exact match of PlatformId, ProductType, Major.Minor.Build, CSDVersion, ServerR2, NativeArchitecture
            //   returns the entry in the table.
            // * If the entry in the table matches PlatformId, ProductType, Major.Minor, it is considered to be the same
            //   OS Kernel Type, so that we remember the highest version, but not higher than the one we're trying to
            //   match.
            //   * Then, if we find an entry that has either the Major or Minor set to -1, and we had a match, we return
            //     the last match, but indicate that this one is newer.
            //   * So order the table to be specific to the build number, and end that list with a build of -1, for the
            //     more generic version (e.g. Windows 10 and Windows 11 only differentiate themselves with the build).
            //   * That last entry, will be given, as it partially matches, but is therefore earlier than any other more
            //     specific match prior.

            foreach (WinVersionLookupEntry entry in WinVersionDatabase) {
                if (entry.OSVersion.PlatformId != WinPlatform.Unknown && entry.OSVersion.PlatformId != m_PlatformId) continue;
                if (entry.OSVersion.MajorVersion != -1 && entry.OSVersion.MajorVersion != m_MajorVersion) continue;
                if (entry.OSVersion.MinorVersion != -1 && entry.OSVersion.MinorVersion != m_MinorVersion) continue;

                // Cache looking for the last closest match, where the PlatformID, Major.Minor version, Product Type
                if (IsSameOSKernelType(entry.OSVersion)) {
                    lastMatch = entry.OSVersion;
                }

                if (entry.OSVersion.BuildNumber != -1 && entry.OSVersion.BuildNumber != m_BuildNumber) continue;
                if (entry.OSVersion.CSDVersion != null) {
                    if (m_CSDVersion == null) continue;
                    if (!entry.OSVersion.CSDVersion.Equals(m_CSDVersion.Trim())) continue;
                }
                if (entry.OSVersion.ProductType != WinProductType.Unknown) {
                    if ((entry.OSVersion.ProductType == WinProductType.Server || entry.OSVersion.ProductType == WinProductType.DomainController) &&
                        (m_ProductType == WinProductType.Server || m_ProductType == WinProductType.DomainController)) {
                        // These are equivalent
                    } else {
                        if (entry.OSVersion.ProductType != m_ProductType) continue;
                    }
                }
                if (entry.OSVersion.ServerR2 && !m_ServerR2) continue;
                if (entry.OSVersion.NativeArchitecture != WinArchitecture.Unknown && entry.OSVersion.NativeArchitecture != m_NativeArchitecture) continue;

                if (entry.OSVersion.MajorVersion == -1 ||
                    entry.OSVersion.MajorVersion != -1 && entry.OSVersion.MinorVersion == -1) {
                    if (lastMatch == null)
                        return entry.WinVersionString;
                    break;
                } else {
                    return entry.WinVersionString;
                }
            }

            return CalculateWinVersion(lastMatch);
        }

        /// <summary>
        /// Calculates the Windows version for a non-exact match.
        /// </summary>
        /// <param name="lastMatch">The Windows version.</param>
        /// <returns>A string that should be presented to the user.</returns>
        /// <remarks>
        /// This class has an internal database of Windows Versions obtained from MSDN. It might be that the internal
        /// database is out of date, applications can override this to provide more accurate information.
        /// </remarks>
        protected virtual string CalculateWinVersion(WinVersion lastMatch)
        {
            if (lastMatch != null)
                return string.Format("{0} or later", lastMatch.WinVersionString);

            return string.Empty;
        }

        private string m_WinVersionInfo = null;

        /// <summary>
        /// Get the Windows version as a simple string, the same as the Enum value <c>OSVersionKind</c>.
        /// </summary>
        public string WinVersionString
        {
            get
            {
                if (m_WinVersionInfo == null) m_WinVersionInfo = CalculateWinVersion();
                return m_WinVersionInfo;
            }
        }

        private bool m_IsExtendedPropsSet = false;

        /// <summary>
        /// Check if this OSVersion object has extended properties set.
        /// </summary>
        public bool IsExtendedPropsSet
        {
            get { return m_IsExtendedPropsSet; }
            set
            {
                CheckLock(nameof(IsExtendedPropsSet));
                m_IsExtendedPropsSet = value;
            }
        }

        /// <summary>
        /// Check if this object is read only.
        /// </summary>
        /// <value>
        /// If this is <see langword="true"/>, the object is read only and the properties cannot be modified.
        /// </value>
        public bool IsReadOnly { get; private set; }

        /// <summary>
        /// Lock this object so that the properties cannot be modified.
        /// </summary>
        public void Lock()
        {
            IsReadOnly = true;
        }

        private void CheckLock(string property)
        {
            if (!IsReadOnly) return;
            string message = string.Format(Messages.InvalidOperationEx_CheckLock, property);
            throw new InvalidOperationException(message);
        }

        #region Cloneable Interfaces
        /// <summary>
        /// Instantiate a new object based on an existing object that is not Sealed.
        /// </summary>
        /// <param name="o">The object to copy from.</param>
        public WinVersion(WinVersion o)
        {
            CopyThis(o);
        }

        /// <summary>
        /// Make this object a copy of the object provided.
        /// </summary>
        /// <param name="o">The object to copy from.</param>
        public virtual void Copy(WinVersion o)
        {
            CheckLock(nameof(Copy));
            CopyThis(o);
        }

        /// <summary>
        /// Create a new object that is not Sealed, based on this object.
        /// </summary>
        /// <returns>A new OSVersion object that is modifiable.</returns>
        public virtual WinVersion CreateCopy()
        {
            return new WinVersion(this);
        }

        /// <summary>
        /// Create a new object that is not Sealed, based on this object.
        /// </summary>
        /// <returns>A new OSVersion object that is modifiable.</returns>
        public virtual object Clone()
        {
            return CreateCopy();
        }

        private void CopyThis(WinVersion o)
        {
            m_PlatformId = o.m_PlatformId;

            m_MajorVersion = o.m_MajorVersion;
            m_MinorVersion = o.m_MinorVersion;
            m_BuildNumber = o.m_BuildNumber;
            m_CSDVersion = o.m_CSDVersion;

            m_SuiteFlags = o.m_SuiteFlags;
            m_ProductType = o.m_ProductType;

            m_ServicePackMajor = o.m_ServicePackMajor;
            m_ServicePackMinor = o.m_ServicePackMinor;

            m_ServerR2 = o.m_ServerR2;
            m_ProductInfo = o.m_ProductInfo;
            m_NativeArchitecture = o.m_NativeArchitecture;

            IsReadOnly = false;
            m_IsExtendedPropsSet = o.m_IsExtendedPropsSet;
        }
        #endregion

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            // Fixes CS0659.
            return base.GetHashCode();
        }

        /// <summary>
        /// Returns a <see cref="string"/> that represents this instance.
        /// </summary>
        /// <remarks>
        /// A long meaningful string is returned that represents the operating system details of this object.
        /// </remarks>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(WinVersionString);

            if (m_IsExtendedPropsSet) {
                ToStringBuild(sb, ProductTypeString);
            }

            // Check if we add x86 or x64
            if (m_NativeArchitecture != WinArchitecture.Unknown) {
                if (m_PlatformId == WinPlatform.WinNT) {
                    if (m_ProductType == WinProductType.Workstation) {
                        if (m_NativeArchitecture != WinArchitecture.x86) {
                            // x86 is still the norm for Win7
                            ToStringBuild(sb, m_NativeArchitecture.ToString());
                        }
                    } else {
                        if (Version >= new Version(6, 0)) {
                            if (m_NativeArchitecture != WinArchitecture.x64) {
                                // x64 is the norm for Win Server 2008 and later
                                ToStringBuild(sb, m_NativeArchitecture.ToString());
                            }
                        } else {
                            if (m_NativeArchitecture != WinArchitecture.x86) {
                                // x86 is the norm for Win Server 2003 and earlier
                                ToStringBuild(sb, m_NativeArchitecture.ToString());
                            }
                        }
                    }
                }
            } else {
                // Special case for WinXP x64, only if the architecture is unknown, we know it's x64.
                if (this == _WinXPx64) {
                    ToStringBuild(sb, WinArchitecture.x64.ToString());
                }
            }

            string pinfo = ProductInfoString;
            if (pinfo.Length > 0) {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('(').Append(pinfo).Append(')');
            }
            if (m_CSDVersion != null)
                ToStringBuild(sb, m_CSDVersion.Trim());
            sb.Append(", v").Append(VersionString);
            return sb.ToString();
        }

        private static void ToStringBuild(StringBuilder sb, string text)
        {
            if (sb.Length > 0 && text.Length > 0) {
                sb.Append(' ');
            }
            if (text.Length > 0) {
                sb.Append(text);
            }
        }

        #region Comparison
        /// <summary>
        /// Determines whether the specified <see cref="object"/> is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to compare with this instance.</param>
        /// <returns>
        /// <see langword="true"/> if the specified <see cref="object"/> is equal to this instance; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        public override bool Equals(object obj)
        {
            WinVersion p = obj as WinVersion;
            if (p != null) return this == p;
            return false;
        }

        private static int Compare(WinVersion o, WinVersion p)
        {
            if (o is null && p is null) return 0;
            if (o is null) return -1;
            if (p is null) return 1;

            if (o.m_PlatformId != WinPlatform.Unknown && p.m_PlatformId != WinPlatform.Unknown) {
                int oo = OrderPlatformId(o.m_PlatformId);
                int po = OrderPlatformId(p.m_PlatformId);
                if (oo < po) return -1;
                if (oo > po) return 1;
            }

            if (o.m_MajorVersion < 0 && p.m_MajorVersion < 0) return 0;
            if (o.m_MajorVersion < p.m_MajorVersion) return -1;
            if (o.m_MajorVersion > p.m_MajorVersion) return 1;

            if (o.m_MinorVersion < 0 && p.m_MinorVersion < 0) return 0;
            if (o.m_MinorVersion < p.m_MinorVersion) return -1;
            if (o.m_MinorVersion > p.m_MinorVersion) return 1;

            // Only compare if both are defined, else we assume they're equal
            if (o.m_BuildNumber > 0 && p.m_BuildNumber > 0) {
                if (o.m_BuildNumber < p.m_BuildNumber) return -1;
                if (o.m_BuildNumber > p.m_BuildNumber) return 1;
            }

            if (o.m_PlatformId == WinPlatform.Win9x) {
                // We compare the CSD string instead of the service pack
                string os = o.m_CSDVersion == null ? "" : o.m_CSDVersion.Trim();
                string ps = p.m_CSDVersion == null ? "" : p.m_CSDVersion.Trim();
                return string.Compare(os, ps, true, System.Globalization.CultureInfo.InvariantCulture);
            }

            // The server R2 flag indicates a newer version if everything else matches
            if (!o.m_ServerR2 && p.m_ServerR2) return -1;
            if (o.m_ServerR2 && !p.m_ServerR2) return 1;

            if (o.m_BuildNumber > 0 && p.m_BuildNumber > 0) {
                if (o.m_IsExtendedPropsSet && p.m_IsExtendedPropsSet) {
                    if (o.m_ServicePackMajor > 0 || p.m_ServicePackMajor > 0) {
                        if (o.m_ServicePackMajor < p.m_ServicePackMajor) return -1;
                        if (o.m_ServicePackMajor > p.m_ServicePackMajor) return 1;
                    }

                    if (o.m_ServicePackMinor > 0 || p.m_ServicePackMinor > 0) {
                        if (o.m_ServicePackMinor < p.m_ServicePackMinor) return -1;
                        if (o.m_ServicePackMinor > p.m_ServicePackMinor) return 1;
                    }
                }
            }

            return 0;
        }

        private static int OrderPlatformId(WinPlatform a)
        {
            switch (a) {
            case WinPlatform.Win32s: return 0;
            case WinPlatform.WinCE: return 1;
            case WinPlatform.Win9x: return 2;
            case WinPlatform.WinNT: return 3;
            default: return (int)a;
            }
        }

        /// <summary>
        /// Compares two OSVersion objects if they are the same.
        /// </summary>
        /// <remarks>
        /// Two OSVersion objects are the same if they have the same major, minor, build, service pack and
        /// OSVersionKind.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class is the same as a
        /// predefined object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the objects match.</returns>
        public static bool operator ==(WinVersion o, WinVersion p)
        {
            return Compare(o, p) == 0;
        }

        /// <summary>
        /// Compares two OSVersion objects if they are the same.
        /// </summary>
        /// <remarks>
        /// Two OSVersion objects are different if they have the any differences in major, minor, build, service pack or
        /// OSVersionKind.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class is the same as a
        /// predefined object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the versions do not match.</returns>
        public static bool operator !=(WinVersion o, WinVersion p)
        {
            return Compare(o, p) != 0;
        }

        /// <summary>
        /// Compares two OSVersion objects for versions.
        /// </summary>
        /// <remarks>
        /// Versions are compared against the major, minor, build and then the service pack. If they all match, then the
        /// enumeration is finally checked in the order defined.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class against a predefined
        /// object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the first is older than the second.</returns>
        public static bool operator <(WinVersion o, WinVersion p)
        {
            return Compare(o, p) < 0;
        }

        /// <summary>
        /// Compares two OSVersion objects for versions.
        /// </summary>
        /// <remarks>
        /// Versions are compared against the major, minor, build and then the service pack. If they all match, then the
        /// enumeration is finally checked in the order defined.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class against a predefined
        /// object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the first is newer than the second.</returns>
        public static bool operator >(WinVersion o, WinVersion p)
        {
            return Compare(o, p) > 0;
        }

        /// <summary>
        /// Compares two OSVersion objects for versions.
        /// </summary>
        /// <remarks>
        /// Versions are compared against the major, minor, build and then the service pack. If they all match, then the
        /// enumeration is finally checked in the order defined.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class against a predefined
        /// object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the first is older or the same as the second.</returns>
        public static bool operator <=(WinVersion o, WinVersion p)
        {
            return Compare(o, p) <= 0;
        }

        /// <summary>
        /// Compares two OSVersion objects for versions.
        /// </summary>
        /// <remarks>
        /// Versions are compared against the major, minor, build and then the service pack. If they all match, then the
        /// enumeration is finally checked in the order defined.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class against a predefined
        /// object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="o">The first OSVersion object.</param>
        /// <param name="p">The second OSVersion object.</param>
        /// <returns><b>true</b> if the first is newer or the same as the second.</returns>
        public static bool operator >=(WinVersion o, WinVersion p)
        {
            return Compare(o, p) >= 0;
        }

        /// <summary>
        /// Compares an OSVersion object against this object.
        /// </summary>
        /// <remarks>
        /// Versions are compared against the major, minor, build and then the service pack. If they all match, then the
        /// enumeration is finally checked in the order defined.
        /// <para>
        /// You can use this to check if your operating system, returned by the OSVerQuery class against a predefined
        /// object, e.g. Win95, Vista, Win7, etc.
        /// </para>
        /// </remarks>
        /// <param name="obj">The other OSVersion object.</param>
        /// <returns>
        /// -1 indicates that this object is older than the other OSVersion. 0 indicates they are the same. 1 indicates
        /// this object is newer than the other OSVersion.
        /// </returns>
        public virtual int CompareTo(object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            WinVersion p = obj as WinVersion
                ?? throw new ArgumentException(Messages.ArgumentEx_NotOsVersion, nameof(obj));
            return Compare(this, p);
        }
        #endregion
    }
}
