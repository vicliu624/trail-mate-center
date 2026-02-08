using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using System.Globalization;

namespace TrailMateCenter.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string ResourceBaseUri = "avares://TrailMateCenter.App/Resources/Strings/Strings.";
    private const string ResourceSuffix = ".axaml";
    private static readonly CultureInfo FallbackCulture = new("en-US");
    private ResourceDictionary? _currentDictionary;
    private CultureInfo _currentCulture = CultureInfo.InvariantCulture;

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CultureChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public void Initialize()
    {
        ApplyCulture(CultureInfo.CurrentUICulture);
    }

    public void ApplyCulture(CultureInfo culture)
    {
        var dictionary = LoadDictionary(culture) ?? LoadDictionary(FallbackCulture);
        if (dictionary is null || Application.Current is null)
            return;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;

        if (_currentDictionary is not null)
            Application.Current.Resources.MergedDictionaries.Remove(_currentDictionary);

        Application.Current.Resources.MergedDictionaries.Insert(0, dictionary);
        _currentDictionary = dictionary;
        _currentCulture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }

    public string GetString(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, Application.Current.RequestedThemeVariant, out var value) == true
            && value is string text)
        {
            return text;
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, GetString(key), args);
    }

    private static ResourceDictionary? LoadDictionary(CultureInfo culture)
    {
        var primary = BuildUri(culture.Name);
        if (TryLoad(primary, out var dictionary))
            return dictionary;

        if (!string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
        {
            var fallback = BuildUri(culture.TwoLetterISOLanguageName);
            if (TryLoad(fallback, out dictionary))
                return dictionary;
        }

        return null;
    }

    private static Uri BuildUri(string cultureName) => new($"{ResourceBaseUri}{cultureName}{ResourceSuffix}");

    private static bool TryLoad(Uri uri, out ResourceDictionary? dictionary)
    {
        try
        {
            dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
            return true;
        }
        catch
        {
            dictionary = null;
            return false;
        }
    }
}
