using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using TrailMateCenter.Localization;
using TrailMateCenter.Transport;

namespace TrailMateCenter.ViewModels;

public sealed partial class SerialPortInfoViewModel : ObservableObject, ILocalizationAware
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
                label = LocalizationService.Instance.GetString("Status.Port.UnknownDevice");
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
            return parts.Count == 0
                ? LocalizationService.Instance.GetString("Status.Port.InfoUnknown")
                : string.Join(LocalizationService.Instance.GetString("Common.Separator"), parts);
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayDetail));
    }
}
