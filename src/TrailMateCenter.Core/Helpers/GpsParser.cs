using System.Globalization;
using System.Text.RegularExpressions;

namespace TrailMateCenter.Helpers;

public static class GpsParser
{
    private static readonly Regex JsonPattern = new("\"lat\"\\s*:\\s*(?<lat>-?\\d+(?:\\.\\d+)?)\\s*,\\s*\"(lon|lng)\"\\s*:\\s*(?<lon>-?\\d+(?:\\.\\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KvPattern = new("lat\\s*=\\s*(?<lat>-?\\d+(?:\\.\\d+)?)\\s*,?\\s*(lon|lng)\\s*=\\s*(?<lon>-?\\d+(?:\\.\\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PairPattern = new("(?<lat>-?\\d+(?:\\.\\d+)?)\\s*,\\s*(?<lon>-?\\d+(?:\\.\\d+)?)",
        RegexOptions.Compiled);

    public static bool TryExtract(string? text, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = JsonPattern.Match(text);
        if (!match.Success)
            match = KvPattern.Match(text);
        if (!match.Success)
            match = PairPattern.Match(text);

        if (!match.Success)
            return false;

        return double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
    }
}
