using OsmSharp.Tags;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record OsmAnnotationClass(MapAnnotationRule Rule, string Name, string? Reference);

/// <summary>Extracts geographical meaning before zoom and viewport selection.</summary>
public static class OsmAnnotationClassifier
{
    public static OsmAnnotationClass? Classify(TagsCollectionBase? tags, bool regionFocus = false, bool largeArea = false)
    {
        if (tags is null) return null;
        string? Tag(string key) => tags.TryGetValue(key, out var value) ? value : null;
        var name = Tag("name:zh") ?? Tag("name") ?? string.Empty;
        var reference = Tag("ref");
        OsmAnnotationClass? Result(MapAnnotationKind kind, string category)
        {
            var rule = MapAnnotationPolicy.Find(kind, category);
            return rule is null ? null : new(rule, name, reference);
        }

        if (Tag("highway") is { } highway && (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(reference)))
        {
            if (highway.EndsWith("_link", StringComparison.Ordinal)) highway = highway[..^5];
            if (Result(MapAnnotationKind.Road, highway) is { } road) return road;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            var place = Tag("place");
            if (regionFocus && place == "city") return Result(MapAnnotationKind.Place, "region_focus");
            if (place == "city" && Tag("capital") is "yes" or "2") return Result(MapAnnotationKind.Place, "capital");
            if (place == "city" && Tag("capital") == "4") return Result(MapAnnotationKind.Place, "regional_capital");
            var category = place switch
            {
                "province" => "state", "hamlet" => "village", "neighborhood" => "neighbourhood",
                "locality" => "quarter", _ => place,
            };
            if (category is not null && Result(MapAnnotationKind.Place, category) is { } location) return location;
            if (Tag("leisure") == "park") return Result(MapAnnotationKind.Place, "park");
            if (largeArea && (Tag("natural") is "water" or "wood" || Tag("landuse") == "reservoir"))
                return Result(MapAnnotationKind.Place, "large_natural");
        }

        var poi = (Tag("amenity"), Tag("tourism"), Tag("natural")) switch
        {
            ("drinking_water", _, _) or (_, _, "spring") => "water",
            (_, "camp_site", _) => "camp",
            ("shelter", _, _) or (_, "alpine_hut" or "wilderness_hut", _) => "shelter",
            (_, _, "peak") => "peak", (_, "viewpoint", _) => "viewpoint",
            ("parking", _, _) => "parking", ("toilets", _, _) => "toilet",
            ("hospital" or "clinic", _, _) => "emergency",
            ("school" or "university" or "college", _, _) => "school",
            ("ranger_station", _, _) => "ranger", (_, "information", _) => "info",
            ("restaurant" or "cafe" or "fast_food", _, _) => "food", _ => null,
        };
        poi ??= Tag("emergency") is "phone" or "rescue_station" or "mountain_rescue" ? "emergency" : null;
        poi ??= Tag("highway") == "trailhead" ? "trailhead" : null;
        poi ??= Tag("railway") is "station" or "halt" || Tag("public_transport") == "station" ? "station" : null;
        poi ??= Tag("highway") == "bus_stop" ? "bus_stop" : null;
        poi ??= Tag("shop") is { } shop && shop != "no" ? "shop" : null;
        poi ??= Tag("entrance") is { } entrance && entrance != "no" ? "entrance" : null;
        if (poi is not null) return Result(MapAnnotationKind.Poi, poi);
        if (!string.IsNullOrWhiteSpace(name) && Tag("building") is { } building && building != "no")
            return Result(MapAnnotationKind.Place, "building");
        return null;
    }
}
