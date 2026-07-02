using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public interface IAdminAreaProvider
{
    Task<AdminAreaSearchResult> SearchAsync(
        AdminAreaQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class CachedNominatimAdminAreaProvider : IAdminAreaProvider
{
    private const string DefaultEndpoint = "https://nominatim.openstreetmap.org/search";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly Uri _endpoint;
    private readonly TimeSpan _cacheTtl;

    public CachedNominatimAdminAreaProvider(
        HttpClient? httpClient = null,
        string? cacheDirectory = null,
        Uri? endpoint = null,
        TimeSpan? cacheTtl = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrailMateCenter", "admin-area-cache")
            : cacheDirectory;
        _endpoint = endpoint ?? new Uri(DefaultEndpoint);
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
    }

    public async Task<AdminAreaSearchResult> SearchAsync(
        AdminAreaQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var normalizedText = NormalizeQueryText(query.Text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new AdminAreaSearchResult
            {
                ErrorMessage = "Administrative area query is empty.",
            };
        }

        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory, $"{HashCacheKey(normalizedText, query.IncludeBoundaryGeoJson)}.json");
        var cached = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cached is not null && DateTimeOffset.UtcNow - cached.CachedAt <= _cacheTtl)
        {
            return new AdminAreaSearchResult
            {
                Areas = cached.Areas.Take(Math.Max(1, query.Limit)).ToArray(),
                FromCache = true,
            };
        }

        try
        {
            var uri = BuildSearchUri(normalizedText, Math.Max(1, query.Limit), query.IncludeBoundaryGeoJson);
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var rawResults = await JsonSerializer.DeserializeAsync<List<NominatimSearchResult>>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false) ?? new List<NominatimSearchResult>();

            var areas = rawResults
                .Select(ToAdminAreaRecord)
                .Where(static a => a is not null)
                .Select(static a => a!)
                .ToArray();

            await WriteCacheAsync(cachePath, normalizedText, areas, cancellationToken).ConfigureAwait(false);
            return new AdminAreaSearchResult
            {
                Areas = areas,
                FromCache = false,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (cached is not null)
            {
                return new AdminAreaSearchResult
                {
                    Areas = cached.Areas.Take(Math.Max(1, query.Limit)).ToArray(),
                    FromCache = true,
                    ErrorMessage = ex.Message,
                };
            }

            return new AdminAreaSearchResult
            {
                ErrorMessage = ex.Message,
            };
        }
    }

    private Uri BuildSearchUri(string text, int limit, bool includeBoundary)
    {
        var builder = new UriBuilder(_endpoint);
        var query = new Dictionary<string, string>
        {
            ["format"] = "jsonv2",
            ["q"] = text,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["addressdetails"] = "1",
            ["extratags"] = "1",
            ["polygon_geojson"] = includeBoundary ? "1" : "0",
        };

        builder.Query = string.Join(
            "&",
            query.Select(static kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return builder.Uri;
    }

    private static AdminAreaRecord? ToAdminAreaRecord(NominatimSearchResult result)
    {
        if (result.BoundingBox is null || result.BoundingBox.Length != 4)
            return null;
        if (!double.TryParse(result.BoundingBox[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var south))
            return null;
        if (!double.TryParse(result.BoundingBox[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var north))
            return null;
        if (!double.TryParse(result.BoundingBox[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var west))
            return null;
        if (!double.TryParse(result.BoundingBox[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var east))
            return null;

        var adminLevel = default(int?);
        if (result.ExtraTags is not null &&
            result.ExtraTags.TryGetValue("admin_level", out var adminLevelText) &&
            int.TryParse(adminLevelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAdminLevel))
        {
            adminLevel = parsedAdminLevel;
        }

        var name = result.Name;
        if (string.IsNullOrWhiteSpace(name) && result.Address is not null)
        {
            name = result.Address.TryGetValue("country", out var country) ? country : result.DisplayName;
        }

        return new AdminAreaRecord
        {
            Id = string.IsNullOrWhiteSpace(result.OsmType)
                ? result.PlaceId.ToString(CultureInfo.InvariantCulture)
                : $"{result.OsmType}-{result.OsmId}",
            Name = string.IsNullOrWhiteSpace(name) ? result.DisplayName : name,
            DisplayName = result.DisplayName,
            CountryCode = result.Address is not null && result.Address.TryGetValue("country_code", out var countryCode)
                ? countryCode
                : string.Empty,
            AdminLevel = adminLevel,
            Bounds = new GeoBounds(west, south, east, north).Normalize(),
            BoundaryGeoJson = result.GeoJson.GetRawTextOrEmpty(),
            Provider = "nominatim",
            CachedAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<CachedAdminAreaSearch?> TryReadCacheAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CachedAdminAreaSearch>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(
        string path,
        string query,
        IReadOnlyList<AdminAreaRecord> areas,
        CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(
                    new CachedAdminAreaSearch
                    {
                        Query = query,
                        Areas = areas,
                        CachedAt = DateTimeOffset.UtcNow,
                    },
                    JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);

        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }

    private static string NormalizeQueryText(string text)
    {
        return string.Join(
            " ",
            (text ?? string.Empty)
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string HashCacheKey(string text, bool includeBoundary)
    {
        var raw = $"{text}|polygon={includeBoundary}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record NominatimSearchResult
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; init; }

        [JsonPropertyName("osm_type")]
        public string OsmType { get; init; } = string.Empty;

        [JsonPropertyName("osm_id")]
        public long OsmId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonPropertyName("boundingbox")]
        public string[]? BoundingBox { get; init; }

        [JsonPropertyName("address")]
        public Dictionary<string, string>? Address { get; init; }

        [JsonPropertyName("extratags")]
        public Dictionary<string, string>? ExtraTags { get; init; }

        [JsonPropertyName("geojson")]
        public JsonElement GeoJson { get; init; }
    }
}

internal static class JsonElementExtensions
{
    public static string GetRawTextOrEmpty(this JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? string.Empty
            : element.GetRawText();
    }
}
