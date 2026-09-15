namespace TrailMateCenter.Maps;

public enum MapAnnotationKind { Place, Road, Poi }
public enum MapAnnotationPreset { Outdoor, Urban }

public sealed record MapAnnotationZoomRange(int Minimum = 1, int Maximum = 18)
{
    public bool Contains(int zoom) => zoom >= Math.Clamp(Minimum, 1, 18) && zoom <= Math.Clamp(Maximum, 1, 18);
}

/// <summary>Display eligibility, independent of raster styling and memory budgets.</summary>
public sealed record MapAnnotationOptions
{
    public MapAnnotationPreset Preset { get; init; } = MapAnnotationPreset.Outdoor;
    public bool PlacesEnabled { get; init; } = true;
    public bool RoadsEnabled { get; init; } = true;
    public bool PoisEnabled { get; init; } = true;
    public MapAnnotationZoomRange PlaceZooms { get; init; } = new(1, 18);
    public MapAnnotationZoomRange RoadZooms { get; init; } = new(7, 18);
    public MapAnnotationZoomRange PoiZooms { get; init; } = new(10, 18);

    public bool IsEnabled(MapAnnotationKind kind, int zoom) => kind switch
    {
        MapAnnotationKind.Place => PlacesEnabled && PlaceZooms.Contains(zoom),
        MapAnnotationKind.Road => RoadsEnabled && RoadZooms.Contains(zoom),
        MapAnnotationKind.Poi => PoisEnabled && PoiZooms.Contains(zoom),
        _ => false,
    };
}

public sealed record MapAnnotationRule(MapAnnotationKind Kind, string Category, int MinimumZoom, int Priority, bool UrbanOnly = false);

/// <summary>Common class/zoom rules consumed by preview and offline export.</summary>
public static class MapAnnotationPolicy
{
    public const string Version = "trailmate-annotations-v1";
    public static IReadOnlyList<MapAnnotationRule> Rules { get; } = Array.AsReadOnly(new[]
    {
        new MapAnnotationRule(MapAnnotationKind.Place, "region_focus", 1, 250),
        new(MapAnnotationKind.Place, "continent", 1, 245),
        new(MapAnnotationKind.Place, "country", 1, 240),
        new(MapAnnotationKind.Place, "capital", 2, 235),
        new(MapAnnotationKind.Place, "state", 4, 230),
        new(MapAnnotationKind.Place, "regional_capital", 4, 225),
        new(MapAnnotationKind.Place, "city", 5, 220),
        new(MapAnnotationKind.Place, "large_natural", 5, 170),
        new(MapAnnotationKind.Place, "town", 7, 205),
        new(MapAnnotationKind.Place, "village", 10, 180),
        new(MapAnnotationKind.Place, "suburb", 12, 170),
        new(MapAnnotationKind.Place, "park", 12, 150),
        new(MapAnnotationKind.Place, "quarter", 14, 145),
        new(MapAnnotationKind.Place, "neighbourhood", 15, 140),
        new(MapAnnotationKind.Place, "building", 17, 110),
        new(MapAnnotationKind.Road, "motorway", 7, 220),
        new(MapAnnotationKind.Road, "trunk", 7, 210),
        new(MapAnnotationKind.Road, "primary", 9, 200),
        new(MapAnnotationKind.Road, "secondary", 13, 185),
        new(MapAnnotationKind.Road, "tertiary", 14, 180),
        new(MapAnnotationKind.Road, "residential", 15, 175),
        new(MapAnnotationKind.Road, "unclassified", 16, 170),
        new(MapAnnotationKind.Road, "living_street", 16, 170),
        new(MapAnnotationKind.Road, "pedestrian", 17, 160),
        new(MapAnnotationKind.Road, "footway", 17, 155),
        new(MapAnnotationKind.Road, "cycleway", 17, 155),
        new(MapAnnotationKind.Road, "service", 18, 150),
        new(MapAnnotationKind.Road, "path", 18, 145),
        new(MapAnnotationKind.Road, "track", 18, 145),
        new(MapAnnotationKind.Road, "steps", 18, 140),
        new(MapAnnotationKind.Poi, "trailhead", 10, 190),
        new(MapAnnotationKind.Poi, "camp", 10, 185),
        new(MapAnnotationKind.Poi, "shelter", 10, 180),
        new(MapAnnotationKind.Poi, "peak", 11, 165),
        new(MapAnnotationKind.Poi, "viewpoint", 11, 160),
        new(MapAnnotationKind.Poi, "emergency", 12, 195),
        new(MapAnnotationKind.Poi, "station", 13, 170),
        new(MapAnnotationKind.Poi, "parking", 14, 140),
        new(MapAnnotationKind.Poi, "school", 14, 135),
        new(MapAnnotationKind.Poi, "water", 15, 180),
        new(MapAnnotationKind.Poi, "toilet", 15, 155),
        new(MapAnnotationKind.Poi, "ranger", 14, 165),
        new(MapAnnotationKind.Poi, "info", 14, 145),
        new(MapAnnotationKind.Poi, "bus_stop", 16, 125),
        new(MapAnnotationKind.Poi, "shop", 17, 100, true),
        new(MapAnnotationKind.Poi, "food", 17, 100, true),
        new(MapAnnotationKind.Poi, "entrance", 18, 110),
    });

    public static MapAnnotationRule? Find(MapAnnotationKind kind, string category) =>
        Rules.FirstOrDefault(r => r.Kind == kind && r.Category == category);

    public static bool IsEligible(MapAnnotationRule rule, int zoom, MapAnnotationOptions options) =>
        zoom >= rule.MinimumZoom && options.IsEnabled(rule.Kind, zoom) &&
        (!rule.UrbanOnly || options.Preset == MapAnnotationPreset.Urban);

    public static string? RoadText(string? name, string? reference, int zoom)
    {
        // Low zooms show identifiers, not full street names. A missing reference
        // does not justify inventing a number or burning a long name into the raster.
        if (zoom <= 10) return string.IsNullOrWhiteSpace(reference) ? null : reference;
        return !string.IsNullOrWhiteSpace(name) ? name : reference;
    }
}
