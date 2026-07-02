namespace TrailMateCenter.Maps;

public sealed record PoiPriorityZoomRule(int MaxZoom, int MinimumPriority);

public sealed record PoiPriorityRules
{
    public static readonly PoiPriorityRules Default = new();

    public IReadOnlyDictionary<string, int> TypePriority { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["emergency"] = 100,
            ["team"] = 100,
            ["water"] = 90,
            ["camp"] = 90,
            ["shelter"] = 85,
            ["trailhead"] = 80,
            ["parking"] = 75,
            ["peak"] = 70,
            ["viewpoint"] = 65,
            ["toilet"] = 60,
            ["ranger"] = 60,
            ["info"] = 60,
            ["bridge"] = 50,
            ["ford"] = 50,
            ["generic"] = 40,
        };

    public IReadOnlyList<PoiPriorityZoomRule> ZoomRules { get; init; } =
    [
        new PoiPriorityZoomRule(10, 85),
        new PoiPriorityZoomRule(12, 70),
        new PoiPriorityZoomRule(14, 50),
    ];

    public int GetPriority(string? type)
    {
        if (!string.IsNullOrWhiteSpace(type) &&
            TypePriority.TryGetValue(type.Trim(), out var priority))
        {
            return priority;
        }

        return TypePriority.TryGetValue("generic", out var generic)
            ? generic
            : 40;
    }

    public bool ShouldIncludeAtZoom(PoiRecord poi, int zoom)
    {
        foreach (var rule in ZoomRules.OrderBy(static r => r.MaxZoom))
        {
            if (zoom <= rule.MaxZoom)
                return poi.Priority >= rule.MinimumPriority;
        }

        return true;
    }
}
