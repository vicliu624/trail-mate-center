using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;

namespace TrailMateCenter.ViewModels;

public sealed partial class ChannelOptionViewModel : ObservableObject, ILocalizationAware
{
    public ChannelOptionViewModel(byte id, string? label = null)
    {
        Id = id;
        _customLabel = label;
        Label = string.IsNullOrWhiteSpace(label)
            ? LocalizationService.Instance.Format("Common.ChannelFormat", id)
            : label;
    }

    public byte Id { get; }
    private readonly string? _customLabel;

    [ObservableProperty]
    private string _label;

    public void RefreshLocalization()
    {
        if (!string.IsNullOrWhiteSpace(_customLabel))
            return;
        Label = LocalizationService.Instance.Format("Common.ChannelFormat", Id);
    }

    public override string ToString() => Label;
}
