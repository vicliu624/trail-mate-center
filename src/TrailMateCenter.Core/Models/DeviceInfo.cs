namespace TrailMateCenter.Models;

public sealed record DeviceInfo(
    string Model,
    string FirmwareVersion,
    ushort ProtocolVersion,
    Capabilities Capabilities);
