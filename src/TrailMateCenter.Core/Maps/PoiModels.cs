using System.Text.Json.Serialization;

namespace TrailMateCenter.Maps;

public sealed record PoiRecord
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "generic";
    public string? Name { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int Priority { get; init; }
    public string Source { get; init; } = "osm";
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

public enum PoiOutputFormat
{
    Readable = 0,
    Compact = 1,
}

public sealed record PoiIndexOptions
{
    public int MinZoom { get; init; } = 10;
    public int MaxZoom { get; init; } = 17;
    public int MaxPoiPerTile { get; init; } = 200;
    public bool IncludeLabels { get; init; } = true;
    public bool IncludeOriginalTags { get; init; }
    public bool GenerateFullPoisJsonl { get; init; } = true;
    public bool GenerateTileIndex { get; init; } = true;
    public PoiOutputFormat OutputFormat { get; init; } = PoiOutputFormat.Readable;

    public PoiIndexOptions Normalize()
    {
        var minZoom = Math.Clamp(MinZoom, TileMath.MinimumZoom, TileMath.MaximumZoom);
        var maxZoom = Math.Clamp(MaxZoom, TileMath.MinimumZoom, TileMath.MaximumZoom);
        if (maxZoom < minZoom)
            (minZoom, maxZoom) = (maxZoom, minZoom);

        return this with
        {
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            MaxPoiPerTile = Math.Max(1, MaxPoiPerTile),
        };
    }
}

public sealed record PoiSourceInfo
{
    public string Type { get; init; } = "osm-pbf";
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = "local";
    public string DownloadUrl { get; init; } = string.Empty;
    public string License { get; init; } = "ODbL";
}

public sealed record PoiAreaInfo
{
    public string Name { get; init; } = "Selection";
    public int? AdminLevel { get; init; }
    public GeoBounds Bounds { get; init; }
}

public sealed record PoiManifestInput
{
    public PoiSourceInfo Source { get; init; } = new();
    public PoiAreaInfo Area { get; init; } = new();
    public PoiIndexOptions Index { get; init; } = new();
    public IReadOnlyCollection<string> PoiTypes { get; init; } = Array.Empty<string>();
    public string NameLanguage { get; init; } = "default";
}

public sealed record PoiIndexWriteSummary
{
    public long SourcePoiCount { get; init; }
    public long FullPoiRowsWritten { get; init; }
    public long IndexRowsWritten { get; init; }
    public int TileFilesWritten { get; init; }
    public bool WasAnyTileClipped { get; init; }
    public IReadOnlyDictionary<int, long> ClippedTileCountByZoom { get; init; } = new Dictionary<int, long>();
}

public sealed record PoiManifest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("generator")]
    public string Generator { get; init; } = "TrailMateCenter";

    [JsonPropertyName("source")]
    public PoiManifestSource Source { get; init; } = new();

    [JsonPropertyName("area")]
    public PoiManifestArea Area { get; init; } = new();

    [JsonPropertyName("index")]
    public PoiManifestIndex Index { get; init; } = new();

    [JsonPropertyName("poi_types")]
    public IReadOnlyList<string> PoiTypes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("name_language")]
    public string NameLanguage { get; init; } = "default";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PoiManifestSource
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
    public string License { get; init; } = "ODbL";
}

public sealed record PoiManifestArea
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Selection";

    [JsonPropertyName("admin_level")]
    public int? AdminLevel { get; init; }

    [JsonPropertyName("bounds")]
    public PoiManifestBounds Bounds { get; init; } = new();
}

public sealed record PoiManifestBounds
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

public sealed record PoiManifestIndex
{
    [JsonPropertyName("scheme")]
    public string Scheme { get; init; } = "web-mercator-xyz";

    [JsonPropertyName("min_zoom")]
    public int MinZoom { get; init; }

    [JsonPropertyName("max_zoom")]
    public int MaxZoom { get; init; }

    [JsonPropertyName("format")]
    public string Format { get; init; } = "jsonl";

    [JsonPropertyName("max_poi_per_tile")]
    public int MaxPoiPerTile { get; init; }

    [JsonPropertyName("include_labels")]
    public bool IncludeLabels { get; init; }

    [JsonPropertyName("include_original_tags")]
    public bool IncludeOriginalTags { get; init; }

    [JsonPropertyName("output_format")]
    public string OutputFormat { get; init; } = "readable";

    [JsonPropertyName("clipped_tiles")]
    public bool ClippedTiles { get; init; }

    [JsonPropertyName("clipped_tile_count_by_zoom")]
    public IReadOnlyDictionary<int, long> ClippedTileCountByZoom { get; init; } = new Dictionary<int, long>();
}
