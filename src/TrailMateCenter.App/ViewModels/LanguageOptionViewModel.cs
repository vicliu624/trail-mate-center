using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using TrailMateCenter.Localization;

namespace TrailMateCenter.ViewModels;

public sealed partial class LanguageOptionViewModel : ObservableObject, ILocalizationAware
{
    public LanguageOptionViewModel(string cultureName, string displayNameKey)
    {
        Culture = new CultureInfo(cultureName);
        DisplayNameKey = displayNameKey;
        _displayName = LocalizationService.Instance.GetString(DisplayNameKey);
    }

    public CultureInfo Culture { get; }
    public string DisplayNameKey { get; }

    [ObservableProperty]
    private string _displayName;

    public void RefreshLocalization()
    {
        DisplayName = LocalizationService.Instance.GetString(DisplayNameKey);
    }

    public override string ToString() => DisplayName;
}
