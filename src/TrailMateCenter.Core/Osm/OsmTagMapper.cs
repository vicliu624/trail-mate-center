using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record OsmPoiTagRule(string Key, string? Value, string PoiType);

public sealed record OsmPoiMappingResult(string Type, int Priority);

public sealed class OsmTagMapper
{
    public static readonly IReadOnlyList<OsmPoiTagRule> DefaultRules =
    [
        new("amenity", "drinking_water", "water"),
        new("natural", "spring", "water"),
        new("tourism", "camp_site", "camp"),
        new("tourism", "alpine_hut", "shelter"),
        new("tourism", "wilderness_hut", "shelter"),
        new("amenity", "shelter", "shelter"),
        new("natural", "peak", "peak"),
        new("tourism", "viewpoint", "viewpoint"),
        new("amenity", "parking", "parking"),
        new("highway", "trailhead", "trailhead"),
        new("amenity", "toilets", "toilet"),
        new("amenity", "hospital", "emergency"),
        new("amenity", "clinic", "emergency"),
        new("emergency", "phone", "emergency"),
        new("emergency", "rescue_station", "emergency"),
        new("emergency", "mountain_rescue", "emergency"),
        new("tourism", "information", "ranger"),
        new("amenity", "ranger_station", "ranger"),
        new("office", "ranger", "ranger"),
        new("man_made", "bridge", "bridge"),
        new("ford", null, "ford"),
    ];

    private readonly IReadOnlyList<OsmPoiTagRule> _rules;
    private readonly PoiPriorityRules _priorityRules;

    public OsmTagMapper(
        IReadOnlyList<OsmPoiTagRule>? rules = null,
        PoiPriorityRules? priorityRules = null)
    {
        _rules = rules ?? DefaultRules;
        _priorityRules = priorityRules ?? PoiPriorityRules.Default;
    }

    public OsmPoiMappingResult? Map(IReadOnlyDictionary<string, string> tags, ISet<string>? selectedTypes = null)
    {
        if (tags is null || tags.Count == 0)
            return null;

        foreach (var rule in _rules)
        {
            if (!tags.TryGetValue(rule.Key, out var actualValue))
                continue;
            if (!string.IsNullOrWhiteSpace(rule.Value) &&
                !string.Equals(actualValue, rule.Value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (selectedTypes is not null && !selectedTypes.Contains(rule.PoiType))
                return null;

            return new OsmPoiMappingResult(rule.PoiType, _priorityRules.GetPriority(rule.PoiType));
        }

        return null;
    }
}
