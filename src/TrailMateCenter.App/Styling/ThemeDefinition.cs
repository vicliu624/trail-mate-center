using System;

namespace TrailMateCenter.Styling;

public sealed record ThemeDefinition(
    string Id,
    string NameKey,
    Uri PaletteUri,
    Uri ThemeUri);
