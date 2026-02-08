using System.Collections.Generic;
using TrailMateCenter.Transport;

namespace TrailMateCenter.ViewModels;

public sealed class SerialPortInfoViewModel
{
    public SerialPortInfoViewModel(SerialPortInfo info)
    {
        Info = info;
    }

    public SerialPortInfo Info { get; }

    public string PortName => Info.PortName;
    public string Description => Info.Description ?? string.Empty;
    public string FriendlyName => Info.FriendlyName ?? string.Empty;
    public string Manufacturer => Info.Manufacturer ?? string.Empty;
    public string VendorId => Info.VendorId ?? string.Empty;
    public string ProductId => Info.ProductId ?? string.Empty;
    public string PnpDeviceId => Info.PnpDeviceId ?? string.Empty;

    public string DisplayTitle
    {
        get
        {
            var label = FriendlyName;
            if (string.IsNullOrWhiteSpace(label))
                label = Description;
            if (string.IsNullOrWhiteSpace(label))
                label = "未识别设备";
            return $"{PortName}  {label}";
        }
    }

    public string DisplayDetail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Manufacturer))
                parts.Add(Manufacturer);
            if (!string.IsNullOrWhiteSpace(VendorId) || !string.IsNullOrWhiteSpace(ProductId))
                parts.Add($"VID:{VendorId} PID:{ProductId}");
            if (!string.IsNullOrWhiteSpace(PnpDeviceId))
                parts.Add(PnpDeviceId);
            return parts.Count == 0 ? "设备信息未知" : string.Join(" · ", parts);
        }
    }
}
