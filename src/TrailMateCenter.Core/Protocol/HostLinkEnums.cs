namespace TrailMateCenter.Protocol;

public enum HostLinkFrameType : byte
{
    Hello = 0x01,
    HelloAck = 0x02,
    Ack = 0x03,

    CmdTxMsg = 0x10,
    CmdGetConfig = 0x11,
    CmdSetConfig = 0x12,
    CmdSetTime = 0x13,
    CmdGetGps = 0x14,
    CmdTxAppData = 0x15,

    EvRxMsg = 0x80,
    EvTxResult = 0x81,
    EvStatus = 0x82,
    EvLog = 0x83,
    EvGps = 0x84,
    EvAppData = 0x85,
    EvTeamState = 0x86,
}

public enum HostLinkErrorCode : byte
{
    Ok = 0,
    BadCrc = 1,
    Unsupported = 2,
    Busy = 3,
    InvalidParam = 4,
    NotInMode = 5,
    Internal = 6,
}

[Flags]
public enum HostLinkCapabilities : uint
{
    CapTxMsg = 1u << 0,
    CapConfig = 1u << 1,
    CapSetTime = 1u << 2,
    CapStatus = 1u << 3,
    CapLogs = 1u << 4,
    CapGps = 1u << 5,
    CapAppData = 1u << 6,
    CapTeamState = 1u << 7,
    CapAprsGateway = 1u << 8,
}

[Flags]
public enum HostLinkAppDataFlags : byte
{
    HasTeamMetadata = 1 << 0,
    WantResponse = 1 << 1,
    WasEncryptedOnAir = 1 << 2,
    MoreChunks = 1 << 3,
}

public enum HostLinkConfigKey : byte
{
    MeshProtocol = 1,
    Region = 2,
    Channel = 3,
    DutyCycle = 4,
    ChannelUtil = 5,
    AprsEnable = 20,
    AprsIgateCallsign = 21,
    AprsIgateSsid = 22,
    AprsToCall = 23,
    AprsPath = 24,
    AprsTxMinIntervalSec = 25,
    AprsDedupeWindowSec = 26,
    AprsSymbolTable = 27,
    AprsSymbolCode = 28,
    AprsPositionIntervalSec = 29,
    AprsNodeIdMap = 30,
    AprsSelfEnable = 31,
    AprsSelfCallsign = 32,
}

public enum HostLinkStatusKey : byte
{
    Battery = 1,
    Charging = 2,
    LinkState = 3,
    MeshProtocol = 4,
    Region = 5,
    Channel = 6,
    DutyCycle = 7,
    ChannelUtil = 8,
    LastError = 9,
}

public enum HostLinkLinkState : byte
{
    Stopped = 0,
    Waiting = 1,
    Connected = 2,
    Handshaking = 3,
    Ready = 4,
    Error = 5,
}
