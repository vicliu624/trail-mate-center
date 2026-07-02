using System.Text.Json.Serialization;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record AdminAreaQuery
{
    public string Text { get; init; } = string.Empty;
    public int Limit { get; init; } = 10;
    public bool IncludeBoundaryGeoJson { get; init; } = true;
}

public sealed record AdminAreaRecord
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public int? AdminLevel { get; init; }
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public string Provider { get; init; } = "nominatim";
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AdminAreaSearchResult
{
    public IReadOnlyList<AdminAreaRecord> Areas { get; init; } = Array.Empty<AdminAreaRecord>();
    public bool FromCache { get; init; }
    public string? ErrorMessage { get; init; }
}

internal sealed record CachedAdminAreaSearch
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("areas")]
    public IReadOnlyList<AdminAreaRecord> Areas { get; init; } = Array.Empty<AdminAreaRecord>();

    [JsonPropertyName("cached_at")]
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}
