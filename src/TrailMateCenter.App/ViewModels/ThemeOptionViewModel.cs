using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;
using TrailMateCenter.Styling;

namespace TrailMateCenter.ViewModels;

public sealed partial class ThemeOptionViewModel : ObservableObject, ILocalizationAware
{
    public ThemeOptionViewModel(ThemeDefinition definition)
    {
        Definition = definition;
        _displayName = LocalizationService.Instance.GetString(definition.NameKey);
    }

    public ThemeDefinition Definition { get; }

    [ObservableProperty]
    private string _displayName;

    public void RefreshLocalization()
    {
        DisplayName = LocalizationService.Instance.GetString(Definition.NameKey);
    }

    public override string ToString() => DisplayName;
}
