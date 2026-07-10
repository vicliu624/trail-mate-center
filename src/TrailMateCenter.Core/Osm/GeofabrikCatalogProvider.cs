using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record GeofabrikRegionRecord
{
    public string Id { get; init; } = string.Empty;
    public string ParentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PbfUrl { get; init; } = string.Empty;
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
}

public sealed record GeofabrikCatalogResult
{
    public IReadOnlyList<GeofabrikRegionRecord> Regions { get; init; } = Array.Empty<GeofabrikRegionRecord>();
    public bool FromCache { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GeofabrikCatalogProvider
{
    private const string DefaultCatalogUrl = "https://download.geofabrik.de/index-v1.json";
    private const double MinimumTargetCoverageRatio = 0.85;
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _catalogUri;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _cacheTtl;
    private readonly GeoJsonBoundaryImporter _boundaryImporter = new();

    public GeofabrikCatalogProvider(
        HttpClient? httpClient = null,
        string? cacheDirectory = null,
        Uri? catalogUri = null,
        TimeSpan? cacheTtl = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _catalogUri = catalogUri ?? new Uri(DefaultCatalogUrl);
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrailMateCenter", "geofabrik-cache")
            : cacheDirectory;
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
    }

    public async Task<GeofabrikCatalogResult> GetCatalogAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = GetCatalogCachePath();
        if (!forceRefresh && IsFreshCache(cachePath))
        {
            var cached = await TryParseCatalogFileAsync(cachePath, fromCache: true, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                return cached;
        }

        try
        {
            using var response = await _httpClient.GetAsync(_catalogUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(tempPath))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(cachePath))
                File.Delete(cachePath);
            File.Move(tempPath, cachePath);

            return await TryParseCatalogFileAsync(cachePath, fromCache: false, cancellationToken).ConfigureAwait(false)
                ?? new GeofabrikCatalogResult { ErrorMessage = "Geofabrik catalog parse failed." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var cached = await TryParseCatalogFileAsync(cachePath, fromCache: true, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                return cached with { ErrorMessage = ex.Message };

            return new GeofabrikCatalogResult { ErrorMessage = ex.Message };
        }
    }

    public async Task<IReadOnlyList<GeofabrikRegionRecord>> SearchAsync(
        string text,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (catalog.Regions.Count == 0)
            return Array.Empty<GeofabrikRegionRecord>();

        var query = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
            return catalog.Regions
                .Where(static r => !string.IsNullOrWhiteSpace(r.PbfUrl))
                .OrderBy(static r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, limit))
                .ToArray();

        return catalog.Regions
            .Where(r =>
                !string.IsNullOrWhiteSpace(r.PbfUrl) &&
                (r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 r.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => ScoreRegion(r, query))
            .ThenBy(static r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public async Task<IReadOnlyList<GeofabrikRegionRecord>> FindCoveringRegionsAsync(
        GeoBounds bounds,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (catalog.Regions.Count == 0)
            return Array.Empty<GeofabrikRegionRecord>();

        var target = bounds.Normalize();
        return catalog.Regions
            .Where(region =>
                !string.IsNullOrWhiteSpace(region.PbfUrl) &&
                HasUsableBounds(region.Bounds) &&
                IsSuitableMatch(region.Bounds, target))
            .OrderBy(static region => Area(region.Bounds))
            .ThenBy(static region => region.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public string GetCatalogCachePath()
    {
        return Path.Combine(_cacheDirectory, "index-v1.json");
    }

    private async Task<GeofabrikCatalogResult?> TryParseCatalogFileAsync(
        string path,
        bool fromCache,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var regions = ParseCatalog(document.RootElement);
            return new GeofabrikCatalogResult
            {
                Regions = regions,
                FromCache = fromCache,
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<GeofabrikRegionRecord> ParseCatalog(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GeofabrikRegionRecord>();
        }

        var rawRecords = new List<RawGeofabrikRecord>();
        foreach (var feature in features.EnumerateArray())
        {
            var raw = ParseFeature(feature);
            if (raw is not null)
                rawRecords.Add(raw);
        }

        var byId = rawRecords.ToDictionary(static r => r.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<GeofabrikRegionRecord>(rawRecords.Count);
        foreach (var raw in rawRecords)
        {
            var lineage = new Stack<string>();
            var cursor = raw;
            var guard = 0;
            while (cursor is not null && guard++ < 20)
            {
                lineage.Push(cursor.Name);
                cursor = string.IsNullOrWhiteSpace(cursor.ParentId) ||
                         !byId.TryGetValue(cursor.ParentId, out var parent)
                    ? null
                    : parent;
            }

            result.Add(new GeofabrikRegionRecord
            {
                Id = raw.Id,
                ParentId = raw.ParentId,
                Name = raw.Name,
                DisplayName = string.Join(" / ", lineage.Where(static p => !string.IsNullOrWhiteSpace(p))),
                PbfUrl = raw.PbfUrl,
                Bounds = raw.Bounds,
                BoundaryGeoJson = raw.BoundaryGeoJson,
            });
        }

        return result
            .OrderBy(static r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private RawGeofabrikRecord? ParseFeature(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object ||
            !feature.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetString(properties, "id");
        var name = GetString(properties, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return null;

        var pbfUrl = string.Empty;
        if (properties.TryGetProperty("urls", out var urls) &&
            urls.ValueKind == JsonValueKind.Object &&
            urls.TryGetProperty("pbf", out var pbfElement) &&
            pbfElement.ValueKind == JsonValueKind.String)
        {
            pbfUrl = pbfElement.GetString() ?? string.Empty;
        }

        var bounds = default(GeoBounds);
        var boundary = string.Empty;
        if (feature.TryGetProperty("geometry", out var geometry))
        {
            var imported = _boundaryImporter.Import(geometry, name);
            if (imported.Success)
            {
                bounds = imported.Bounds;
                boundary = imported.BoundaryGeoJson;
            }
        }

        return new RawGeofabrikRecord
        {
            Id = id,
            ParentId = GetString(properties, "parent"),
            Name = name,
            PbfUrl = pbfUrl,
            Bounds = bounds,
            BoundaryGeoJson = boundary,
        };
    }

    private bool IsFreshCache(string cachePath)
    {
        if (!File.Exists(cachePath))
            return false;

        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(cachePath);
        return age <= _cacheTtl;
    }

    private static int ScoreRegion(GeofabrikRegionRecord region, string query)
    {
        if (string.Equals(region.Name, query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(region.Id, query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (region.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (region.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 3;
        return 4;
    }

    private static bool HasUsableBounds(GeoBounds bounds)
    {
        var normalized = bounds.Normalize();
        return Math.Abs(normalized.East - normalized.West) > double.Epsilon &&
               Math.Abs(normalized.North - normalized.South) > double.Epsilon;
    }

    private static bool Contains(GeoBounds outerBounds, GeoBounds innerBounds)
    {
        const double Tolerance = 0.000001;
        var outer = outerBounds.Normalize();
        var inner = innerBounds.Normalize();
        return inner.West >= outer.West - Tolerance &&
               inner.East <= outer.East + Tolerance &&
               inner.South >= outer.South - Tolerance &&
               inner.North <= outer.North + Tolerance;
    }

    private static bool IsSuitableMatch(GeoBounds regionBounds, GeoBounds targetBounds)
    {
        return Contains(regionBounds, targetBounds) ||
               TargetCoverageRatio(regionBounds, targetBounds) >= MinimumTargetCoverageRatio;
    }

    private static double TargetCoverageRatio(GeoBounds regionBounds, GeoBounds targetBounds)
    {
        var region = regionBounds.Normalize();
        var target = targetBounds.Normalize();
        var targetArea = Area(target);
        if (targetArea <= double.Epsilon)
            return 0;

        var west = Math.Max(region.West, target.West);
        var east = Math.Min(region.East, target.East);
        var south = Math.Max(region.South, target.South);
        var north = Math.Min(region.North, target.North);
        var width = Math.Max(0, east - west);
        var height = Math.Max(0, north - south);
        return (width * height) / targetArea;
    }

    private static double Area(GeoBounds bounds)
    {
        var normalized = bounds.Normalize();
        return Math.Abs(normalized.East - normalized.West) * Math.Abs(normalized.North - normalized.South);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record RawGeofabrikRecord
    {
        public string Id { get; init; } = string.Empty;
        public string ParentId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string PbfUrl { get; init; } = string.Empty;
        public GeoBounds Bounds { get; init; }
        public string BoundaryGeoJson { get; init; } = string.Empty;
    }
}

public sealed record GeofabrikDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    string LocalPath,
    string Message)
{
    public double? Percent =>
        TotalBytes is > 0
            ? Math.Clamp((double)BytesReceived / TotalBytes.Value * 100.0, 0.0, 100.0)
            : null;
}

public sealed record GeofabrikPbfCacheEntry
{
    [JsonPropertyName("region_id")]
    public string RegionId { get; init; } = string.Empty;

    [JsonPropertyName("region_name")]
    public string RegionName { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("local_path")]
    public string LocalPath { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("downloaded_at")]
    public DateTimeOffset DownloadedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class GeofabrikPbfDownloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public GeofabrikPbfDownloadService(HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrailMateCenter", "osm-pbf-cache")
            : cacheDirectory;

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
    }

    public async Task<GeofabrikPbfCacheEntry> DownloadAsync(
        GeofabrikRegionRecord region,
        bool forceRefresh = false,
        IProgress<GeofabrikDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (region is null)
            throw new ArgumentNullException(nameof(region));
        if (string.IsNullOrWhiteSpace(region.PbfUrl))
            throw new InvalidOperationException("Selected Geofabrik region does not provide a PBF download URL.");

        Directory.CreateDirectory(_cacheDirectory);
        var localPath = Path.Combine(_cacheDirectory, BuildPbfFileName(region));
        var metadataPath = $"{localPath}.json";
        if (!forceRefresh && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
        {
            var cached = await TryReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
                return cached;
        }

        var tempPath = $"{localPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await _httpClient.GetAsync(
                    region.PbfUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            progress?.Report(new GeofabrikDownloadProgress(0, totalBytes, localPath, "Download started."));
            var totalRead = 0L;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(tempPath))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;
                    progress?.Report(new GeofabrikDownloadProgress(totalRead, totalBytes, localPath, "Downloading PBF..."));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(localPath))
                File.Delete(localPath);
            File.Move(tempPath, localPath);

            var entry = new GeofabrikPbfCacheEntry
            {
                RegionId = region.Id,
                RegionName = region.DisplayName,
                Url = region.PbfUrl,
                LocalPath = localPath,
                SizeBytes = new FileInfo(localPath).Length,
                DownloadedAt = DateTimeOffset.UtcNow,
            };

            await File.WriteAllTextAsync(
                    metadataPath,
                    JsonSerializer.Serialize(entry, JsonOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new GeofabrikDownloadProgress(entry.SizeBytes, entry.SizeBytes, localPath, "Download complete."));
            return entry;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task<GeofabrikPbfCacheEntry?> TryReadMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<GeofabrikPbfCacheEntry>(
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

    private static string BuildPbfFileName(GeofabrikRegionRecord region)
    {
        var fileName = Path.GetFileName(new Uri(region.PbfUrl).LocalPath);
        if (!string.IsNullOrWhiteSpace(fileName) &&
            fileName.EndsWith(".osm.pbf", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeFileName(fileName);
        }

        return $"{SanitizeFileName(region.Id)}-latest.osm.pbf";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '-' : ch);
        }

        return builder.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Stale temp files are harmless and can be cleaned by a later run.
        }
    }
}
