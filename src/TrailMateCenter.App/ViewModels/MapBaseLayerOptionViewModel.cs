using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;

namespace TrailMateCenter.ViewModels;

public sealed partial class MapBaseLayerOptionViewModel : ObservableObject, ILocalizationAware
{
    public MapBaseLayerOptionViewModel(MapBaseLayerKind kind, string labelKey)
    {
        Kind = kind;
        LabelKey = labelKey;
        _label = LocalizationService.Instance.GetString(labelKey);
    }

    public MapBaseLayerKind Kind { get; }
    public string LabelKey { get; }

    [ObservableProperty]
    private string _label;

    public void RefreshLocalization()
    {
        Label = LocalizationService.Instance.GetString(LabelKey);
    }

    public override string ToString() => Label;
}
