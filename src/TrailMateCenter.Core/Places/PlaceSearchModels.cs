using System.Text.Json.Serialization;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Places;

public sealed record PlaceRecord
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = PlaceCategories.Generic;
    public string PrimaryName { get; init; } = string.Empty;
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int Rank { get; init; }
    public string Source { get; init; } = "osm";
    public string OsmType { get; init; } = string.Empty;
    public long? OsmId { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

public static class PlaceCategories
{
    public const string Admin = "admin";
    public const string Emergency = "emergency";
    public const string Food = "food";
    public const string Generic = "generic";
    public const string Landmark = "landmark";
    public const string Lodging = "lodging";
    public const string Medical = "medical";
    public const string Natural = "natural";
    public const string Outdoor = "outdoor";
    public const string Settlement = "settlement";
    public const string Shop = "shop";
    public const string Transport = "transport";
    public const string Water = "water";
}

public sealed record PlaceExtractionOptions
{
    public string PbfPath { get; init; } = string.Empty;
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public string NameLanguage { get; init; } = "default";
    public bool IncludeOriginalTags { get; init; }
    public int? MaxPlaces { get; init; }
    public int ProgressInterval { get; init; } = 25_000;
}

public sealed record PlaceExtractionProgress(long ProcessedElements, long ExtractedPlaceCount);

public sealed record PlaceSourceInfo
{
    public string Type { get; init; } = "osm-pbf";
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = "local";
    public string DownloadUrl { get; init; } = string.Empty;
    public string License { get; init; } = "ODbL-1.0";
}

public sealed record PlaceAreaInfo
{
    public string Name { get; init; } = "Selection";
    public int? AdminLevel { get; init; }
    public GeoBounds Bounds { get; init; }
}

public sealed record PlaceSearchPackManifestInput
{
    public string PackId { get; init; } = string.Empty;
    public PlaceSourceInfo Source { get; init; } = new();
    public PlaceAreaInfo Area { get; init; } = new();
    public string NameLanguage { get; init; } = "default";
}

public sealed record PlaceSearchPackWriteSummary
{
    public string PackId { get; init; } = string.Empty;
    public long PlaceCount { get; init; }
    public long NameRowsWritten { get; init; }
    public IReadOnlyDictionary<string, long> CategoryCounts { get; init; } = new Dictionary<string, long>();
}

public sealed record PlaceSearchPackExportRequest
{
    public string OutputRoot { get; init; } = string.Empty;
    public string PbfPath { get; init; } = string.Empty;
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public string AreaName { get; init; } = "Selection";
    public int? AreaAdminLevel { get; init; }
    public string SourceProvider { get; init; } = "local";
    public string SourceDownloadUrl { get; init; } = string.Empty;
    public string NameLanguage { get; init; } = "default";
    public bool IncludeOriginalTags { get; init; }
}

public sealed record PlaceSearchPackExportResult
{
    public bool Success { get; init; }
    public string PlaceRoot { get; init; } = string.Empty;
    public long PlaceCount { get; init; }
    public long NameRowsWritten { get; init; }
    public string? ErrorMessage { get; init; }

    public static PlaceSearchPackExportResult Fail(string message)
    {
        return new PlaceSearchPackExportResult
        {
            Success = false,
            ErrorMessage = message,
        };
    }
}

public sealed record PlaceSearchPackManifest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("format")]
    public string Format { get; init; } = "place-search-binary-v1";

    [JsonPropertyName("generator")]
    public string Generator { get; init; } = "TrailMateCenter";

    [JsonPropertyName("pack_id")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public PlaceSearchPackManifestFiles Files { get; init; } = new();

    [JsonPropertyName("binary")]
    public PlaceSearchPackManifestBinary Binary { get; init; } = new();

    [JsonPropertyName("categories")]
    public IReadOnlyList<PlaceSearchPackCategory> Categories { get; init; } = Array.Empty<PlaceSearchPackCategory>();

    [JsonPropertyName("source")]
    public PlaceSearchPackManifestSource Source { get; init; } = new();

    [JsonPropertyName("area")]
    public PlaceSearchPackManifestArea Area { get; init; } = new();

    [JsonPropertyName("extraction")]
    public PlaceSearchPackManifestExtraction Extraction { get; init; } = new();

    [JsonPropertyName("records")]
    public PlaceSearchPackManifestRecords Records { get; init; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PlaceSearchPackCategory
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed record PlaceSearchPackManifestFiles
{
    [JsonPropertyName("places")]
    public string Places { get; init; } = "places.bin";

    [JsonPropertyName("names")]
    public string Names { get; init; } = "names.bin";
}

public sealed record PlaceSearchPackManifestBinary
{
    [JsonPropertyName("endianness")]
    public string Endianness { get; init; } = "little";

    [JsonPropertyName("string_encoding")]
    public string StringEncoding { get; init; } = "utf-8";

    [JsonPropertyName("string_length")]
    public string StringLength { get; init; } = "uint16-bytes";

    [JsonPropertyName("max_string_bytes")]
    public int MaxStringBytes { get; init; } = 512;

    [JsonPropertyName("coordinates")]
    public string Coordinates { get; init; } = "int32-e7";

    [JsonPropertyName("place_record")]
    public string PlaceRecord { get; init; } =
        "uint64 key_hash, int32 lat_e7, int32 lon_e7, uint16 rank, uint16 category_id, uint8 osm_type_id, int64 osm_id, string id, string primary_name";

    [JsonPropertyName("name_record")]
    public string NameRecord { get; init; } =
        "uint64 place_offset, uint64 key_hash, int32 lat_e7, int32 lon_e7, uint16 rank, uint16 category_id, string display_name, string normalized";
}

public sealed record PlaceSearchPackManifestSource
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "osm-pbf";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "local";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("license")]
    public string License { get; init; } = "ODbL-1.0";
}

public sealed record PlaceSearchPackManifestArea
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Selection";

    [JsonPropertyName("admin_level")]
    public int? AdminLevel { get; init; }

    [JsonPropertyName("bounds")]
    public PlaceSearchPackManifestBounds Bounds { get; init; } = new();
}

public sealed record PlaceSearchPackManifestBounds
{
    [JsonPropertyName("west")]
    public double West { get; init; }

    [JsonPropertyName("south")]
    public double South { get; init; }

    [JsonPropertyName("east")]
    public double East { get; init; }

    [JsonPropertyName("north")]
    public double North { get; init; }
}

public sealed record PlaceSearchPackManifestExtraction
{
    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "osm-node-search";

    [JsonPropertyName("name_language")]
    public string NameLanguage { get; init; } = "default";

    [JsonPropertyName("includes_nodes")]
    public bool IncludesNodes { get; init; } = true;

    [JsonPropertyName("includes_ways")]
    public bool IncludesWays { get; init; }

    [JsonPropertyName("includes_relations")]
    public bool IncludesRelations { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } =
        "This extractor emits searchable OSM nodes that already carry coordinates. Way and relation representative points require a topology-aware extraction pass.";
}

public sealed record PlaceSearchPackManifestRecords
{
    [JsonPropertyName("place_count")]
    public long PlaceCount { get; init; }

    [JsonPropertyName("name_rows")]
    public long NameRows { get; init; }

    [JsonPropertyName("category_counts")]
    public IReadOnlyDictionary<string, long> CategoryCounts { get; init; } = new Dictionary<string, long>();
}

public sealed record PlaceSearchCatalog
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("format")]
    public string Format { get; init; } = "place-search-catalog-v1";

    [JsonPropertyName("packs")]
    public IReadOnlyList<PlaceSearchCatalogPack> Packs { get; init; } = Array.Empty<PlaceSearchCatalogPack>();

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PlaceSearchCatalogPack
{
    [JsonPropertyName("pack_id")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("admin_level")]
    public int? AdminLevel { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("bounds")]
    public PlaceSearchPackManifestBounds Bounds { get; init; } = new();

    [JsonPropertyName("source")]
    public PlaceSearchPackManifestSource Source { get; init; } = new();

    [JsonPropertyName("records")]
    public PlaceSearchPackManifestRecords Records { get; init; } = new();

    [JsonPropertyName("files")]
    public PlaceSearchPackManifestFiles Files { get; init; } = new();
}

public sealed record PlaceSearchPackLicenseFile
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("sources")]
    public IReadOnlyList<PlaceSearchPackLicenseSource> Sources { get; init; } =
    [
        new PlaceSearchPackLicenseSource()
    ];
}

public sealed record PlaceSearchPackLicenseSource
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = "OpenStreetMap";

    [JsonPropertyName("license")]
    public string License { get; init; } = "ODbL-1.0";

    [JsonPropertyName("attribution")]
    public string Attribution { get; init; } = "OpenStreetMap contributors";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "https://www.openstreetmap.org/copyright";
}
