namespace TrailMateCenter.Places;

public sealed record OsmPlaceMappingResult(string Category, int Rank);

public sealed class OsmPlaceTagMapper
{
    private static readonly IReadOnlyDictionary<string, int> PlaceRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["country"] = 1000,
            ["state"] = 950,
            ["province"] = 940,
            ["region"] = 920,
            ["city"] = 900,
            ["borough"] = 850,
            ["town"] = 840,
            ["municipality"] = 820,
            ["village"] = 760,
            ["suburb"] = 700,
            ["quarter"] = 670,
            ["neighbourhood"] = 650,
            ["hamlet"] = 620,
            ["locality"] = 540,
            ["island"] = 640,
            ["islet"] = 520,
        };

    private static readonly IReadOnlyDictionary<string, int> AdminRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["2"] = 980,
            ["3"] = 950,
            ["4"] = 920,
            ["5"] = 890,
            ["6"] = 860,
            ["7"] = 830,
            ["8"] = 800,
            ["9"] = 760,
            ["10"] = 720,
        };

    public OsmPlaceMappingResult? Map(IReadOnlyDictionary<string, string> tags)
    {
        if (tags is null || tags.Count == 0)
            return null;

        if (TryGet(tags, "place", out var placeValue))
        {
            return new OsmPlaceMappingResult(
                CategoryForPlace(placeValue),
                PlaceRanks.TryGetValue(placeValue, out var rank) ? rank : 560);
        }

        if (IsAdministrativeBoundary(tags))
        {
            var rank = TryGet(tags, "admin_level", out var adminLevel) &&
                AdminRanks.TryGetValue(adminLevel, out var adminRank)
                    ? adminRank
                    : 680;
            return new OsmPlaceMappingResult(PlaceCategories.Admin, rank);
        }

        if (TryGet(tags, "railway", out var railway) &&
            railway is "station" or "halt" or "tram_stop")
        {
            return new OsmPlaceMappingResult(PlaceCategories.Transport, railway == "station" ? 620 : 520);
        }

        if (TryGet(tags, "aeroway", out var aeroway) &&
            aeroway is "aerodrome" or "airport" or "helipad")
        {
            return new OsmPlaceMappingResult(PlaceCategories.Transport, aeroway == "helipad" ? 560 : 680);
        }

        if (TryGet(tags, "public_transport", out var publicTransport) &&
            !string.Equals(publicTransport, "platform", StringComparison.OrdinalIgnoreCase))
        {
            return new OsmPlaceMappingResult(PlaceCategories.Transport, 540);
        }

        if (TryGet(tags, "highway", out var highway))
        {
            if (highway is "bus_stop")
                return new OsmPlaceMappingResult(PlaceCategories.Transport, 500);
            if (highway is "trailhead")
                return new OsmPlaceMappingResult(PlaceCategories.Outdoor, 620);
        }

        if (TryGet(tags, "amenity", out var amenity))
            return MapAmenity(amenity);

        if (TryGet(tags, "tourism", out var tourism))
            return MapTourism(tourism);

        if (TryGet(tags, "natural", out var natural))
            return MapNatural(natural);

        if (tags.ContainsKey("shop"))
            return new OsmPlaceMappingResult(PlaceCategories.Shop, 430);

        if (tags.ContainsKey("historic"))
            return new OsmPlaceMappingResult(PlaceCategories.Landmark, 520);

        if (TryGet(tags, "leisure", out var leisure))
        {
            return new OsmPlaceMappingResult(
                leisure is "park" or "nature_reserve" ? PlaceCategories.Outdoor : PlaceCategories.Landmark,
                leisure is "park" or "nature_reserve" ? 560 : 460);
        }

        if (tags.ContainsKey("healthcare"))
            return new OsmPlaceMappingResult(PlaceCategories.Medical, 600);

        if (tags.ContainsKey("emergency"))
            return new OsmPlaceMappingResult(PlaceCategories.Emergency, 650);

        return null;
    }

    private static OsmPlaceMappingResult MapAmenity(string amenity)
    {
        return amenity switch
        {
            "hospital" => new OsmPlaceMappingResult(PlaceCategories.Medical, 720),
            "clinic" or "doctors" or "dentist" or "pharmacy" =>
                new OsmPlaceMappingResult(PlaceCategories.Medical, 620),
            "police" or "fire_station" or "rescue_station" =>
                new OsmPlaceMappingResult(PlaceCategories.Emergency, 700),
            "restaurant" or "cafe" or "fast_food" or "food_court" or "pub" or "bar" =>
                new OsmPlaceMappingResult(PlaceCategories.Food, 500),
            "drinking_water" =>
                new OsmPlaceMappingResult(PlaceCategories.Water, 620),
            "bus_station" or "ferry_terminal" =>
                new OsmPlaceMappingResult(PlaceCategories.Transport, 600),
            "shelter" =>
                new OsmPlaceMappingResult(PlaceCategories.Outdoor, 580),
            "parking" or "fuel" or "bank" or "atm" =>
                new OsmPlaceMappingResult(PlaceCategories.Generic, 420),
            _ => new OsmPlaceMappingResult(PlaceCategories.Generic, 400),
        };
    }

    private static OsmPlaceMappingResult MapTourism(string tourism)
    {
        return tourism switch
        {
            "hotel" or "motel" or "guest_house" or "hostel" =>
                new OsmPlaceMappingResult(PlaceCategories.Lodging, 620),
            "camp_site" or "caravan_site" or "alpine_hut" or "wilderness_hut" or "chalet" =>
                new OsmPlaceMappingResult(PlaceCategories.Outdoor, 610),
            "viewpoint" =>
                new OsmPlaceMappingResult(PlaceCategories.Outdoor, 570),
            "information" =>
                new OsmPlaceMappingResult(PlaceCategories.Landmark, 460),
            _ => new OsmPlaceMappingResult(PlaceCategories.Landmark, 480),
        };
    }

    private static OsmPlaceMappingResult MapNatural(string natural)
    {
        return natural switch
        {
            "peak" or "volcano" =>
                new OsmPlaceMappingResult(PlaceCategories.Natural, 700),
            "spring" or "water" or "bay" =>
                new OsmPlaceMappingResult(PlaceCategories.Water, 600),
            "beach" or "cape" or "cave_entrance" or "wood" =>
                new OsmPlaceMappingResult(PlaceCategories.Natural, 540),
            _ => new OsmPlaceMappingResult(PlaceCategories.Natural, 460),
        };
    }

    private static string CategoryForPlace(string placeValue)
    {
        return placeValue is "country" or "state" or "province" or "region"
            ? PlaceCategories.Admin
            : PlaceCategories.Settlement;
    }

    private static bool IsAdministrativeBoundary(IReadOnlyDictionary<string, string> tags)
    {
        return TryGet(tags, "boundary", out var boundary) &&
               string.Equals(boundary, "administrative", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string> tags,
        string key,
        out string value)
    {
        if (tags.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
