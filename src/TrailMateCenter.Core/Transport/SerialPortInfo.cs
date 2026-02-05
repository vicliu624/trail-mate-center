namespace TrailMateCenter.Transport;

public sealed record SerialPortInfo
{
    public string PortName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? FriendlyName { get; init; }
    public string? Manufacturer { get; init; }
    public string? PnpDeviceId { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }
}
