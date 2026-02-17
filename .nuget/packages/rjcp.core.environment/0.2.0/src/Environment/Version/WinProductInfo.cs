namespace RJCP.Core.Environment.Version
{
    /// <summary>
    /// The Operating System Product Info. Supported in Vista and later.
    /// </summary>
    public enum WinProductInfo : long
    {
        /// <summary>
        /// Product unknown.
        /// </summary>
        Undefined = 0x00000000,

        /// <summary>
        /// Product is unlicensed.
        /// </summary>
        Unlicensed = 0xABCDABCD,

        /// <summary>
        /// Ultimate.
        /// </summary>
        Ultimate = 0x00000001,

        /// <summary>
        /// Home Basic.
        /// </summary>
        Home_Basic = 0x00000002,

        /// <summary>
        /// Home Premium.
        /// </summary>
        Home_Premium = 0x00000003,

        /// <summary>
        /// Enterprise.
        /// </summary>
        Enterprise = 0x00000004,

        /// <summary>
        /// Home Basic N.
        /// </summary>
        Home_Basic_N = 0x00000005,

        /// <summary>
        /// Business.
        /// </summary>
        Business = 0x00000006,

        /// <summary>
        /// Server Standard (full installation).
        /// </summary>
        Standard_Server = 0x00000007,

        /// <summary>
        /// Server Datacenter (full installation).
        /// </summary>
        DataCenter_Server = 0x00000008,

        /// <summary>
        /// Windows Small Business Server.
        /// </summary>
        SmallBusiness_Server = 0x00000009,

        /// <summary>
        /// Server Enterprise (full installation).
        /// </summary>
        Enterprise_Server = 0x0000000A,

        /// <summary>
        /// Starter.
        /// </summary>
        Starter = 0x0000000B,

        /// <summary>
        /// Server Datacenter (core installation).
        /// </summary>
        DataCenter_Server_Core = 0x0000000C,

        /// <summary>
        /// Server Standard (core installation).
        /// </summary>
        Standard_Server_Core = 0x0000000D,

        /// <summary>
        /// Server Enterprise (core installation).
        /// </summary>
        Enterprise_Server_Core = 0x0000000E,

        /// <summary>
        /// Server Enterprise for Itanium-based Systems.
        /// </summary>
        Enterprise_Server_IA64 = 0x0000000F,

        /// <summary>
        /// Business N.
        /// </summary>
        Business_N = 0x00000010,

        /// <summary>
        /// Web Server (full installation).
        /// </summary>
        Web_Server = 0x00000011,

        /// <summary>
        /// HPC Edition.
        /// </summary>
        Cluster_Server = 0x00000012,

        /// <summary>
        /// Windows Storage Server 2008 R2 Essentials.
        /// </summary>
        Home_Server = 0x00000013,

        /// <summary>
        /// Storage Server Express.
        /// </summary>
        Storage_Express_Server = 0x00000014,

        /// <summary>
        /// Storage Server Standard.
        /// </summary>
        Storage_Standard_Server = 0x00000015,

        /// <summary>
        /// Storage Server Workgroup.
        /// </summary>
        Storage_Workgroup_Server = 0x00000016,

        /// <summary>
        /// Storage Server Enterprise.
        /// </summary>
        Storage_Enterprise_Server = 0x0000000017,

        /// <summary>
        /// Windows Server 2008 for Windows Essential Server Solutions.
        /// </summary>
        Server_For_SmallBusiness = 0x00000018,

        /// <summary>
        /// Small Business Server Premium.
        /// </summary>
        SmallBusiness_Server_Premium = 0x00000019,

        /// <summary>
        /// Home Premium N.
        /// </summary>
        Home_Premium_N = 0x0000001A,

        /// <summary>
        /// Enterprise N.
        /// </summary>
        Enterprise_N = 0x0000001B,

        /// <summary>
        /// Ultimate N.
        /// </summary>
        Ultimate_N = 0x0000001C,

        /// <summary>
        /// Web Server (core installation).
        /// </summary>
        Web_Server_Core = 0x0000001D,

        /// <summary>
        /// Windows Essential Business Server Management Server.
        /// </summary>
        MediumBusiness_Server_Management = 0x0000001E,

        /// <summary>
        /// Windows Essential Business Server Security Server.
        /// </summary>
        MediumBusiness_Server_Security = 0x0000001F,

        /// <summary>
        /// Windows Essential Business Server Messaging Server.
        /// </summary>
        MediumBusiness_Server_Messaging = 0x00000020,

        /// <summary>
        /// Server Foundation.
        /// </summary>
        Server_Foundation = 0x00000021,

        /// <summary>
        /// Windows Home Server 2011.
        /// </summary>
        Home_Premium_Server = 0x00000022,

        /// <summary>
        /// Windows Server 2008 without Hyper-V for Windows Essential Server Solutions.
        /// </summary>
        Server_For_SmallBusiness_V = 0x00000023,

        /// <summary>
        /// Server Standard without Hyper-V (full installation).
        /// </summary>
        Standard_Server_V = 0x00000024,

        /// <summary>
        /// Server Datacenter without Hyper-V (full installation).
        /// </summary>
        DataCenter_Server_V = 0x00000025,

        /// <summary>
        /// Server Enterprise without Hyper-V (full installation).
        /// </summary>
        Enterprise_Server_V = 0x00000026,

        /// <summary>
        /// Server Datacenter without Hyper-V (core installation).
        /// </summary>
        DataCenter_Server_Core_V = 0x00000027,

        /// <summary>
        /// Server Standard without Hyper-V (core installation).
        /// </summary>
        Standard_Server_Core_V = 0x00000028,

        /// <summary>
        /// Server Enterprise without Hyper-V (core installation).
        /// </summary>
        Enterprise_Server_Core_V = 0x00000029,

        /// <summary>
        /// Hyper-V Server.
        /// </summary>
        HyperV = 0x0000002A,

        /// <summary>
        /// Storage Server Express (core installation).
        /// </summary>
        Storage_Express_Server_Core = 0x0000002B,

        /// <summary>
        /// Storage Server Standard (core installation).
        /// </summary>
        Storage_Standard_Server_Core = 0x0000002C,

        /// <summary>
        /// Storage Server Workgroup (core installation).
        /// </summary>
        Storage_Workgroup_Server_Core = 0x0000002D,

        /// <summary>
        /// Storage Server Enterprise (core installation).
        /// </summary>
        Storage_Enterprise_Core = 0x0000002E,

        /// <summary>
        /// Starter N.
        /// </summary>
        Starter_N = 0x0000002F,

        /// <summary>
        /// Professional.
        /// </summary>
        Professional = 0x00000030,

        /// <summary>
        /// Professional N.
        /// </summary>
        Professional_N = 0x00000031,

        /// <summary>
        /// Windows Small Business Server 2011 Essentials.
        /// </summary>
        Sb_Solution_Server = 0x00000032,

        /// <summary>
        /// Server For SB Solutions.
        /// </summary>
        Server_For_Sb_Solutions = 0x00000033,

        /// <summary>
        /// Server Solutions Premium.
        /// </summary>
        Standard_Server_Solutions = 0x00000034,

        /// <summary>
        /// Server Solutions Premium (core installation).
        /// </summary>
        Standard_Server_Solutions_Core = 0x00000035,

        /// <summary>
        /// Server For SB Solutions EM.
        /// </summary>
        Sb_Solution_Server_Em = 0x00000036,

        /// <summary>
        /// Server For SB Solutions EM.
        /// </summary>
        Server_For_Sb_Solutions_Em = 0x00000037,

        /// <summary>
        /// Windows Multipoint Server.
        /// </summary>
        Solution_EmbeddedServer = 0x00000038,

        /// <summary>
        /// Windows Multipoint Server Core.
        /// </summary>
        Solution_EmbeddedServerCore = 0x00000039,

        /// <summary>
        /// Windows Embedded Professional.
        /// </summary>
        Professional_Embedded = 0x0000003A,

        /// <summary>
        /// Windows Essential Server Solution Management.
        /// </summary>
        EssentialBusiness_Server_Mgmt = 0x0000003B,

        /// <summary>
        /// Windows Essential Server Solution Additional.
        /// </summary>
        EssentialBusiness_Server_Addl = 0x0000003C,

        /// <summary>
        /// Windows Essential Server Solution Management SVC.
        /// </summary>
        EssentialBusiness_Server_MgmtSvc = 0x0000003D,

        /// <summary>
        /// Windows Essential Server Solution Additional SVC.
        /// </summary>
        EssentialBusiness_Server_AddlSvc = 0x0000003E,

        /// <summary>
        /// Small Business Server Premium (core installation).
        /// </summary>
        SmallBusiness_Server_Premium_Core = 0x0000003F,

        /// <summary>
        /// Server Hyper Core V.
        /// </summary>
        Cluster_Server_V = 0x00000040,

        /// <summary>
        /// Windows Embedded.
        /// </summary>
        Embedded = 0x00000041,

        /// <summary>
        /// Starter E (not supported).
        /// </summary>
        Starter_E = 0x00000042,

        /// <summary>
        /// Home Basic E (not supported).
        /// </summary>
        Home_Basic_E = 0x00000043,

        /// <summary>
        /// Home Premium E (not supported).
        /// </summary>
        Home_Premium_E = 0x00000044,

        /// <summary>
        /// Professional E (not supported).
        /// </summary>
        Professional_E = 0x00000045,

        /// <summary>
        /// Enterprise E (Not supported).
        /// </summary>
        Enterprise_E = 0x00000046,

        /// <summary>
        /// Ultimate E (not supported).
        /// </summary>
        Ultimate_E = 0x00000047,

        /// <summary>
        /// Enterprise (Evaluation Edition).
        /// </summary>
        Enterprise_Evaluation = 0x00000048,

        /// <summary>
        /// Windows MultiPoint Server Standard (full installation).
        /// </summary>
        Multipoint_Standard_Server = 0x0000004C,

        /// <summary>
        /// Windows MultiPoint Server Premium (full installation).
        /// </summary>
        Multipoint_Premium_Server = 0x0000004D,

        /// <summary>
        /// Server Standard (evaluation installation).
        /// </summary>
        Standard_Evaluation_Server = 0x0000004F,

        /// <summary>
        /// Server Datacenter (Evaluation Edition).
        /// </summary>
        Datacenter_Evaluation_Server = 0x00000050,

        /// <summary>
        /// Enterprise N (evaluation installation).
        /// </summary>
        Enterprise_N_Evaluation = 0x00000054,

        /// <summary>
        /// Windows Embedded Automotive.
        /// </summary>
        Embedded_Automotive = 0x00000055,

        /// <summary>
        /// Windows Embedded Industrial A
        /// </summary>
        Embedded_Industry_A = 0x00000056,

        /// <summary>
        /// Thin PC.
        /// </summary>
        ThinPC = 0x00000057,

        /// <summary>
        /// Windows Embedded A
        /// </summary>
        Embedded_A = 0x0000058,

        /// <summary>
        /// Windows Embedded Industrial.
        /// </summary>
        Embedded_Industry = 0x00000059,

        /// <summary>
        /// Windows Embedded E.
        /// </summary>
        Embedded_E = 0x0000005A,

        /// <summary>
        /// Windows Embedded Industry E.
        /// </summary>
        Embedded_Industry_E = 0x0000005B,

        /// <summary>
        /// Windows Embedded Industry A E.
        /// </summary>
        Embedded_Industry_A_E = 0x0000005C,

        /// <summary>
        /// Storage Server Workgroup (evaluation installation).
        /// </summary>
        Storage_Workgroup_Evaluation_Server = 0x0000005F,

        /// <summary>
        /// Storage Server Standard (evaluation installation).
        /// </summary>
        Storage_Standard_Evaluation_Server = 0x00000060,

        /// <summary>
        /// Windows Core for Arm.
        /// </summary>
        Core_Arm = 0x00000061,

        /// <summary>
        /// Windows Core N.
        /// </summary>
        Core_N = 0x00000062,

        /// <summary>
        /// Windows 8 China.
        /// </summary>
        Core_CountrySpecific = 0x00000063,

        /// <summary>
        /// Windows 8 Single Language.
        /// </summary>
        Core_SingleLanguage = 0x00000064,

        /// <summary>
        /// Windows 8.
        /// </summary>
        Core = 0x00000065,

        /// <summary>
        /// Professional with Media Center.
        /// </summary>
        Professional_Wmc = 0x00000067,

        /// <summary>
        /// Embedded industry eval.
        /// </summary>
        Embedded_Industry_Eval = 0x00000069,

        /// <summary>
        /// Embedded E industry eval.
        /// </summary>
        Embedded_E_Industry_Eval = 0x000006A,

        /// <summary>
        /// Embedded eval.
        /// </summary>
        Embedded_Eval = 0x0000006B,

        /// <summary>
        /// Embedded E eval.
        /// </summary>
        Embedded_E_Eval = 0x0000006C,

        /// <summary>
        /// Nano server.
        /// </summary>
        Nano_Server = 0x0000006D,

        /// <summary>
        /// Cloud storage server.
        /// </summary>
        Cloud_Storage_Server = 0x0000006E,

        /// <summary>
        /// Core connected.
        /// </summary>
        Core_Connected = 0x0000006F,

        /// <summary>
        /// Professional student.
        /// </summary>
        Professional_Student = 0x00000070,

        /// <summary>
        /// Core connected N.
        /// </summary>
        Core_Connected_N = 0x00000071,

        /// <summary>
        /// Professional student N.
        /// </summary>
        Professional_Student_N = 0x00000072,

        /// <summary>
        /// Core connected single language.
        /// </summary>
        Core_Connected_Single_Language = 0x00000073,

        /// <summary>
        /// Core connected country specific.
        /// </summary>
        Core_Connected_Country_Specific = 0x00000074,

        /// <summary>
        /// Connected car.
        /// </summary>
        Connected_Car = 0x00000075,

        /// <summary>
        /// Industry handheld.
        /// </summary>
        Industry_Handheld = 0x00000076,

        /// <summary>
        /// PPI pro.
        /// </summary>
        PPI_Pro = 0x00000077,

        /// <summary>
        /// Arm64 server.
        /// </summary>
        Arm64_Server = 0x00000078,

        /// <summary>
        /// Education.
        /// </summary>
        Education = 0x00000079,

        /// <summary>
        /// Education n.
        /// </summary>
        Education_N = 0x0000007A,

        /// <summary>
        /// IOTUAP.
        /// </summary>
        IOTUAP = 0x0000007B,

        /// <summary>
        /// Cloud host infrastructure server.
        /// </summary>
        Cloud_Host_Infrastructure_Server = 0x0000007C,

        /// <summary>
        /// Enterprise S.
        /// </summary>
        Enterprise_S = 0x0000007D,

        /// <summary>
        /// Enterprise S N.
        /// </summary>
        Enterprise_S_N = 0x0000007E,

        /// <summary>
        /// Professional S.
        /// </summary>
        Professional_S = 0x0000007F,

        /// <summary>
        /// Professional S N.
        /// </summary>
        Professional_S_N = 0x00000080,

        /// <summary>
        /// Enterprise S eval.
        /// </summary>
        Enterprise_S_Eval = 0x00000081,

        /// <summary>
        /// Enterprise S N eval.
        /// </summary>
        Enterprise_S_N_Eval = 0x00000082,

        /// <summary>
        /// Holographic.
        /// </summary>
        Holographic = 0x00000087,

        /// <summary>
        /// Holographic business.
        /// </summary>
        Holographic_Business = 0x00000088,

        /// <summary>
        /// Pro single language.
        /// </summary>
        Pro_Single_Language = 0x0000008A,

        /// <summary>
        /// Pro china
        /// </summary>
        Pro_China = 0x0000008B,

        /// <summary>
        /// Enterprise subscription.
        /// </summary>
        Enterprise_Subscription = 0x0000008C,

        /// <summary>
        /// Enterprise subscription N.
        /// </summary>
        Enterprise_Subscription_N = 0x0000008D,

        /// <summary>
        /// Datacenter nano server.
        /// </summary>
        Datacenter_Nano_Server = 0x0000008F,

        /// <summary>
        /// Standard nano server.
        /// </summary>
        Standard_Nano_Server = 0x00000090,

        /// <summary>
        /// Datacenter a server core.
        /// </summary>
        Datacenter_A_Server_Core = 0x00000091,

        /// <summary>
        /// Standard A server core.
        /// </summary>
        Standard_A_Server_Core = 0x00000092,

        /// <summary>
        /// Datacenter WS server core.
        /// </summary>
        Datacenter_WS_Server_Core = 0x00000093,

        /// <summary>
        /// Standard WS server core.
        /// </summary>
        Standard_WS_Server_Core = 0x00000094,

        /// <summary>
        /// The utility VM.
        /// </summary>
        Utility_VM = 0x00000095,

        /// <summary>
        /// Datacenter eval server core.
        /// </summary>
        Datacenter_Eval_Server_Core = 0x0000009F,

        /// <summary>
        /// Standard eval server core.
        /// </summary>
        Standard_Eval_Server_Core = 0x000000A0,

        /// <summary>
        /// Pro workstation.
        /// </summary>
        Pro_Workstation = 0x000000A1,

        /// <summary>
        /// Pro workstation N.
        /// </summary>
        Pro_Workstation_N = 0x000000A2,

        /// <summary>
        /// Pro for education.
        /// </summary>
        Pro_For_Education = 0x000000A4,

        /// <summary>
        /// Pro for education N.
        /// </summary>
        Pro_For_Education_N = 0x000000A5,

        /// <summary>
        /// Azure server core.
        /// </summary>
        Azure_Server_Core = 0x000000A8,

        /// <summary>
        /// Azure nano server.
        /// </summary>
        Azure_Nano_Server = 0x000000A9,

        /// <summary>
        /// Enterprise G.
        /// </summary>
        Enterprise_G = 0x000000AB,

        /// <summary>
        /// Enterprise G N.
        /// </summary>
        Enterprise_G_N = 0x000000AC,

        /// <summary>
        /// Server RDSH.
        /// </summary>
        Server_RDSH = 0x000000AF,

        /// <summary>
        /// Cloud.
        /// </summary>
        Cloud = 0x000000B2,

        /// <summary>
        /// Cloud N.
        /// </summary>
        Cloud_N = 0x000000B3,

        /// <summary>
        /// Hub OS.
        /// </summary>
        HubOS = 0x000000B4,

        /// <summary>
        /// One core update OS.
        /// </summary>
        One_Core_Update_OS = 0x000000B6,

        /// <summary>
        /// Cloud E.
        /// </summary>
        Cloud_E = 0x000000B7,

        /// <summary>
        /// Andromeda
        /// </summary>
        Andromeda = 0x000000B8,

        /// <summary>
        /// IOT OS.
        /// </summary>
        IOT_OS = 0x000000B9,

        /// <summary>
        /// Cloud E N.
        /// </summary>
        Cloud_E_N = 0x000000BA,

        /// <summary>
        /// IOT Edge OS.
        /// </summary>
        IOT_EDGEOS = 0x000000BB,

        /// <summary>
        /// IOT Enterprise.
        /// </summary>
        IOT_Enterprise = 0x000000BC,

        /// <summary>
        /// Lite
        /// </summary>
        Lite = 0x000000BD,

        /// <summary>
        /// IOT Enterprise S.
        /// </summary>
        IOT_Enterprise_S = 0x000000BF,

        /// <summary>
        /// X-Box system OS.
        /// </summary>
        XBox_System_OS = 0x000000C0,

        /// <summary>
        /// X-Box native OS.
        /// </summary>
        XBox_Native_OS = 0x000000C1,

        /// <summary>
        /// X-Box game OS.
        /// </summary>
        XBox_Game_OS = 0x000000C2,

        /// <summary>
        /// X-Box era OS.
        /// </summary>
        XBox_Era_OS = 0x000000C3,

        /// <summary>
        /// X-Box durango host OS.
        /// </summary>
        XBox_Durango_Host_OS = 0x000000C4,

        /// <summary>
        /// X-Box scarlett host OS.
        /// </summary>
        XBox_Scarlett_Host_OS = 0x000000C5,

        /// <summary>
        /// X-Box Keystone host OS.
        /// </summary>
        XBox_Keystone = 0x000000C6,

        /// <summary>
        /// Azure Server Cloud Host.
        /// </summary>
        Azure_Server_CloudHost = 0x000000C7,

        /// <summary>
        /// Azure Server Cloud.
        /// </summary>
        Azure_Server_CloudMos = 0x000000C8,

        /// <summary>
        /// Windows Cloud Edition N.
        /// </summary>
        CloudEdition_N = 0x000000CA,

        /// <summary>
        /// Windows Cloud Edition.
        /// </summary>
        CloudEdition = 0x000000CB,

        /// <summary>
        /// Azure Stack HCI Server Core.
        /// </summary>
        AzureStackHci_Server_Core = 0x00000196,

        /// <summary>
        /// Datacenter Server Azure Edition.
        /// </summary>
        Datacenter_Server_Azure_Edition = 0x00000197,

        /// <summary>
        /// Datacenter Server Azure Edition Core.
        /// </summary>
        Datacenter_Server_Core_Azure_Edition = 0x00000198,

        /// <summary>
        /// Windows XP MediaCenter.
        /// </summary>
        MediaCenter = 0xF8000001,

        /// <summary>
        /// Windows XP Tablet PC.
        /// </summary>
        TabletPc = 0xF8000002
    }
}
