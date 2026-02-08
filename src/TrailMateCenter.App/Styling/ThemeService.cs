using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TrailMateCenter.Styling;

public sealed class ThemeService
{
    private static readonly Uri BaseUri = new("avares://TrailMateCenter.App/");
    private static readonly ThemeDefinition FallbackTheme = new(
        "tactical",
        "Ui.Theme.Tactical",
        new Uri("avares://TrailMateCenter.App/Styles/TacticalPalette.axaml"),
        new Uri("avares://TrailMateCenter.App/Styles/TacticalTheme.axaml"));
    private ResourceInclude? _paletteInclude;
    private StyleInclude? _themeInclude;

    public static IReadOnlyList<ThemeDefinition> BuiltInThemes { get; } = new[]
    {
        FallbackTheme,
        new ThemeDefinition(
            "sandstorm",
            "Ui.Theme.Sandstorm",
            new Uri("avares://TrailMateCenter.App/Styles/SandstormPalette.axaml"),
            new Uri("avares://TrailMateCenter.App/Styles/SandstormTheme.axaml")),
    };

    public static ThemeDefinition DefaultTheme => BuiltInThemes.FirstOrDefault() ?? FallbackTheme;

    public static ThemeService Instance { get; } = new();

    public ThemeDefinition CurrentTheme { get; private set; } = FallbackTheme;

    public event EventHandler? ThemeChanged;

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Application.Current is null)
            return;

        if (CurrentTheme.Id == theme.Id && _paletteInclude is not null && _themeInclude is not null)
            return;

        var resources = Application.Current.Resources.MergedDictionaries;
        RemoveResourceInclude(resources, _paletteInclude?.Source);
        RemoveResourceInclude(resources, theme.PaletteUri);

        var palette = new ResourceInclude(BaseUri) { Source = theme.PaletteUri };
        resources.Insert(0, palette);
        _paletteInclude = palette;

        var styles = Application.Current.Styles;
        RemoveStyleInclude(styles, _themeInclude?.Source);
        RemoveStyleInclude(styles, theme.ThemeUri);

        var style = new StyleInclude(BaseUri) { Source = theme.ThemeUri };
        styles.Add(style);
        _themeInclude = style;

        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void RemoveResourceInclude(IList<IResourceProvider> dictionaries, Uri? source)
    {
        if (source is null)
            return;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (dictionaries[i] is ResourceInclude include && include.Source == source)
                dictionaries.RemoveAt(i);
        }
    }

    private static void RemoveStyleInclude(Styles styles, Uri? source)
    {
        if (source is null)
            return;

        for (var i = styles.Count - 1; i >= 0; i--)
        {
            if (styles[i] is StyleInclude include && include.Source == source)
                styles.RemoveAt(i);
        }
    }
}
