using System.Globalization;
using System.Text;

namespace TrailMateCenter.Places;

public static class PlaceNameNormalizer
{
    private static readonly string[] AliasSeparators = [";", "|"];

    public static IReadOnlyList<string> CollectNames(
        IReadOnlyDictionary<string, string> tags,
        string? preferredLanguage = null)
    {
        if (tags is null || tags.Count == 0)
            return Array.Empty<string>();

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (var part in SplitAliases(value))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0 || !seen.Add(trimmed))
                    continue;

                names.Add(trimmed);
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredLanguage) &&
            !string.Equals(preferredLanguage, "default", StringComparison.OrdinalIgnoreCase))
        {
            AddTag(tags, $"name:{preferredLanguage.Trim()}", Add);
        }

        AddTag(tags, "name", Add);
        AddTag(tags, "int_name", Add);
        AddTag(tags, "name:en", Add);
        AddTag(tags, "name:zh", Add);
        AddTag(tags, "official_name", Add);
        AddTag(tags, "short_name", Add);
        AddTag(tags, "local_name", Add);
        AddTag(tags, "alt_name", Add);
        AddTag(tags, "old_name", Add);

        return names;
    }

    public static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }
        }

        return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    private static void AddTag(
        IReadOnlyDictionary<string, string> tags,
        string key,
        Action<string?> add)
    {
        if (tags.TryGetValue(key, out var value))
            add(value);
    }

    private static IEnumerable<string> SplitAliases(string value)
    {
        return value.Split(AliasSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
