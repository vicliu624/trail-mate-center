using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

internal sealed class ContourTileService
{
    private const int TileSize = 256;
    private static readonly string ContourRoot = ContourPaths.Root;
    private static readonly string DemRoot = ContourPaths.DemRoot;
    private static readonly string WorkRoot = ContourPaths.WorkRoot;

    private readonly Action<ContourLogLevel, string>? _log;
    private readonly Channel<ContourTileRequest> _requests = Channel.CreateUnbounded<ContourTileRequest>();
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly EarthdataClient _earthdata;
    private readonly GdalRunner _gdal = new();
    private readonly Action _onTileReady;
    private ContourSettings _settings = new();
    private bool _earthdataPausedLogged;
    private bool _gdalPausedLogged;

    public ContourTileService(Action onTileReady, Action<ContourLogLevel, string>? log = null)
    {
        _onTileReady = onTileReady;
        _log = log;
        _earthdata = new EarthdataClient(Log);
        _ = Task.Run(ProcessQueueAsync);
    }

    public void UpdateSettings(ContourSettings settings)
    {
        _settings = settings ?? new ContourSettings();
        _earthdata.UpdateCredentials(_settings.Earthdata);
        if (_settings.Enabled && !_earthdata.HasCredentials)
        {
            Log(ContourLogLevel.Warning, "Contours enabled but Earthdata token is missing.");
        }
    }

    public async Task<EarthdataTestResult> TestCredentialsAsync(
        (double West, double South, double East, double North) bounds,
        CancellationToken cancellationToken)
    {
        if (!_earthdata.HasCredentials)
            return new EarthdataTestResult(EarthdataTestStatus.MissingCredentials);

        try
        {
            var detailLines = new List<string>();
            var tokenInfo = _earthdata.TryGetTokenInfo();
            if (tokenInfo.HasValue)
            {
                var info = tokenInfo.Value;
                if (!string.IsNullOrWhiteSpace(info.UserId))
                {
                    detailLines.Add($"Token user: {info.UserId}");
                }
                if (info.ExpiresAt.HasValue)
                {
                    var exp = info.ExpiresAt.Value;
                    var delta = exp - DateTimeOffset.UtcNow;
                    var relative = FormatRelativeTime(delta);
                    detailLines.Add($"Token expires: {exp:yyyy-MM-dd HH:mm zzz} ({relative})");
                }
                else
                {
                    detailLines.Add("Token expires: unknown");
                }
            }
            else
            {
                detailLines.Add("Token format: not JWT (cannot read uid/exp)");
            }

            var cmrCheck = await _earthdata.CheckCmrPermissionsAsync(tokenInfo?.UserId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cmrCheck.Detail))
                detailLines.Add(cmrCheck.Detail);
            if (cmrCheck.Status is EarthdataTestStatus.Unauthorized or EarthdataTestStatus.AccessDenied)
                return new EarthdataTestResult(cmrCheck.Status, string.Join('\n', detailLines));
            if (cmrCheck.Status == EarthdataTestStatus.Error)
                detailLines.Add("CMR auth: check failed (continuing with data access test)");

            var urls = await _earthdata.GetGranuleUrlsAsync(bounds, cancellationToken, 3);
            if (urls.Count == 0)
                return new EarthdataTestResult(EarthdataTestStatus.NoDataInView, string.Join('\n', detailLines));

            EarthdataTestResult? best = null;
            foreach (var url in urls)
            {
                var access = await _earthdata.CheckAccessAsync(url, cancellationToken);
                if (access.Status == EarthdataTestStatus.Success)
                {
                    if (!string.IsNullOrWhiteSpace(access.Detail))
                        detailLines.Add($"Data access: {access.Detail}");
                    return new EarthdataTestResult(EarthdataTestStatus.Success, string.Join('\n', detailLines));
                }

                best ??= access;
                if (access.Status is EarthdataTestStatus.Unauthorized or EarthdataTestStatus.AccessDenied)
                {
                    if (!string.IsNullOrWhiteSpace(access.Detail))
                        detailLines.Add($"Data access: {access.Detail}");
                    return new EarthdataTestResult(access.Status, string.Join('\n', detailLines));
                }
            }

            if (best is null)
                return new EarthdataTestResult(EarthdataTestStatus.Error, string.Join('\n', detailLines.Concat(new[] { "Data access: no usable link" })));

            if (!string.IsNullOrWhiteSpace(best.Value.Detail))
                detailLines.Add($"Data access: {best.Value.Detail}");
            return new EarthdataTestResult(best.Value.Status, string.Join('\n', detailLines));
        }
        catch (OperationCanceledException)
        {
            return new EarthdataTestResult(EarthdataTestStatus.Error, "Canceled");
        }
        catch (Exception ex)
        {
            return new EarthdataTestResult(EarthdataTestStatus.Error, ex.Message);
        }
    }

    public void QueueTiles(int zoom, int minX, int maxX, int minY, int maxY, IReadOnlyCollection<ContourKey> spec)
    {
        if (!_settings.Enabled)
            return;
        if (spec.Count == 0)
            return;

        var queued = 0;
        var requested = 0;
        var cached = 0;
        var pending = 0;
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                foreach (var key in spec)
                {
                    requested++;
                    var outputPath = GetTilePath(key, zoom, x, y);
                    if (File.Exists(outputPath))
                    {
                        cached++;
                        continue;
                    }

                    var pendingKey = $"{key.Kind}-{key.Interval}/{zoom}/{x}/{y}";
                    if (!_pending.TryAdd(pendingKey, 0))
                    {
                        pending++;
                        continue;
                    }

                    _requests.Writer.TryWrite(new ContourTileRequest(zoom, x, y, key, outputPath, pendingKey));
                    queued++;
                }
            }
        }

        if (requested > 0)
        {
            var mapTileCount = (maxX - minX + 1) * (maxY - minY + 1);
            var specText = string.Join(", ", spec.OrderBy(s => s.Kind).ThenBy(s => s.Interval)
                .Select(s => $"{s.Kind.ToString().ToLowerInvariant()}-{s.Interval}m"));
            Log(
                ContourLogLevel.Info,
                $"Queue request Z{zoom}: mapTiles={mapTileCount}, lineTiles={requested}, queued={queued}, cached={cached}, pending={pending} [{specText}]");
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var request in _requests.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                await GenerateTileAsync(request, _cts.Token);
            }
            catch
            {
                // Swallow exceptions to keep the background worker running.
            }
            finally
            {
                _pending.TryRemove(request.PendingKey, out _);
            }
        }
    }

    private async Task GenerateTileAsync(ContourTileRequest request, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
            return;
        if (!_earthdata.HasCredentials)
        {
            if (!_earthdataPausedLogged)
            {
                Log(ContourLogLevel.Warning, "Earthdata token missing; contour generation paused.");
                _earthdataPausedLogged = true;
            }
            return;
        }
        _earthdataPausedLogged = false;

        if (!await _gdal.EnsureAvailableAsync(cancellationToken))
        {
            if (!_gdalPausedLogged)
            {
                Log(ContourLogLevel.Error, "GDAL not available; contour generation paused.");
                _gdalPausedLogged = true;
            }
            return;
        }
        _gdalPausedLogged = false;

        Log(ContourLogLevel.Info, $"Tile Z{request.Zoom} {request.X}/{request.Y} {request.Key.Kind.ToString().ToLowerInvariant()}-{request.Key.Interval}m: start");

        var bounds = ContourTileMath.TileToBounds(request.X, request.Y, request.Zoom);
        var demFiles = await _earthdata.EnsureDemFilesAsync(bounds, cancellationToken);
        if (demFiles.Count == 0)
        {
            Log(ContourLogLevel.Warning, $"Tile Z{request.Zoom} {request.X}/{request.Y}: no DEM files available");
            return;
        }

        var workDir = Path.Combine(WorkRoot, $"{request.Zoom}_{request.X}_{request.Y}_{request.Key.Kind}_{request.Key.Interval}");
        Directory.CreateDirectory(workDir);

        var vrtPath = Path.Combine(workDir, "dem.vrt");
        var contourPath = Path.Combine(workDir, "contours.geojson");
        var rasterPath = Path.Combine(workDir, "contours.tif");

        var te = $"{Fmt(bounds.West)} {Fmt(bounds.South)} {Fmt(bounds.East)} {Fmt(bounds.North)}";
        var sources = string.Join(' ', demFiles.Select(Quote));

        if (await _gdal.RunAsync("gdalbuildvrt", $"-overwrite -te {te} {Quote(vrtPath)} {sources}", cancellationToken) != 0)
        {
            Log(ContourLogLevel.Error, $"Tile Z{request.Zoom} {request.X}/{request.Y}: gdalbuildvrt failed");
            return;
        }
        if (await _gdal.RunAsync("gdal_contour", $"-i {request.Key.Interval} -a elev -f GeoJSON {Quote(vrtPath)} {Quote(contourPath)}", cancellationToken) != 0)
        {
            Log(ContourLogLevel.Error, $"Tile Z{request.Zoom} {request.X}/{request.Y}: gdal_contour failed");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        var (r, g, b, a) = GetColor(request.Key.Kind);
        // gdal_rasterize requires drivers with Create() capability; PNG is often CreateCopy-only.
        // Rasterize to GeoTIFF first, then convert to PNG for map tiles.
        var rasterArgs = $"-burn {r} -burn {g} -burn {b} -burn {a} -init 0 0 0 0 -ts {TileSize} {TileSize} -te {te} -a_nodata 0 -ot Byte -of GTiff {Quote(contourPath)} {Quote(rasterPath)}";
        if (await _gdal.RunAsync("gdal_rasterize", rasterArgs, cancellationToken) != 0)
        {
            Log(ContourLogLevel.Error, $"Tile Z{request.Zoom} {request.X}/{request.Y}: gdal_rasterize failed");
            return;
        }
        if (await _gdal.RunAsync("gdal_translate", $"-of PNG {Quote(rasterPath)} {Quote(request.OutputPath)}", cancellationToken) != 0)
        {
            Log(ContourLogLevel.Error, $"Tile Z{request.Zoom} {request.X}/{request.Y}: gdal_translate failed");
            return;
        }

        _onTileReady?.Invoke();
        var size = -1L;
        try
        {
            if (File.Exists(request.OutputPath))
                size = new FileInfo(request.OutputPath).Length;
        }
        catch
        {
            // Ignore diagnostics failure; tile output already completed.
        }
        Log(ContourLogLevel.Info, $"Tile Z{request.Zoom} {request.X}/{request.Y}: done ({size} bytes)");
    }

    private void Log(ContourLogLevel level, string message)
    {
        _log?.Invoke(level, message);
    }

    private static string FormatRelativeTime(TimeSpan delta)
    {
        var abs = delta.Duration();
        var suffix = delta >= TimeSpan.Zero ? "from now" : "ago";
        if (abs.TotalDays >= 1)
            return $"{Math.Round(abs.TotalDays):0}d {suffix}";
        if (abs.TotalHours >= 1)
            return $"{Math.Round(abs.TotalHours):0}h {suffix}";
        if (abs.TotalMinutes >= 1)
            return $"{Math.Round(abs.TotalMinutes):0}m {suffix}";
        return $"{Math.Round(abs.TotalSeconds):0}s {suffix}";
    }

    private static (int R, int G, int B, int A) GetColor(ContourLineKind kind)
    {
        return kind == ContourLineKind.Major
            ? (214, 193, 145, 220)
            : (167, 149, 108, 190);
    }

    private static string GetTilePath(ContourKey key, int zoom, int x, int y)
    {
        return Path.Combine(
            ContourRoot,
            "tiles",
            $"{key.Kind.ToString().ToLowerInvariant()}-{key.Interval}",
            zoom.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture),
            $"{y}.png");
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}

internal readonly record struct ContourTileRequest(
    int Zoom,
    int X,
    int Y,
    ContourKey Key,
    string OutputPath,
    string PendingKey);

internal sealed class EarthdataClient
{
    private static readonly Uri CmrPermissions = new("https://cmr.earthdata.nasa.gov/access-control/permissions");
    private static readonly Uri CmrGranules = new("https://cmr.earthdata.nasa.gov/search/granules.json");
    private readonly HttpClient _searchClient = new();
    private HttpClient _downloadClient = new(new HttpClientHandler());
    private string _token = string.Empty;
    private readonly Action<ContourLogLevel, string>? _log;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_token);

    public EarthdataClient(Action<ContourLogLevel, string>? log)
    {
        _log = log;
        _searchClient.Timeout = TimeSpan.FromSeconds(30);
        _searchClient.DefaultRequestHeaders.UserAgent.Clear();
        _searchClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
        _searchClient.DefaultRequestHeaders.Accept.Clear();
        _searchClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        UpdateCredentials(new EarthdataSettings());
    }

    public void UpdateCredentials(EarthdataSettings settings)
    {
        _token = settings?.Token?.Trim() ?? string.Empty;

        _downloadClient.Dispose();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
        };

        _downloadClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        _downloadClient.DefaultRequestHeaders.UserAgent.Clear();
        _downloadClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
        _downloadClient.DefaultRequestHeaders.Authorization = HasCredentials
            ? new AuthenticationHeaderValue("Bearer", _token)
            : null;
    }

    public EarthdataTokenInfo? TryGetTokenInfo()
    {
        return EarthdataTokenInfo.TryParse(_token, out var info) ? info : null;
    }

    public async Task<EarthdataTestResult> CheckCmrPermissionsAsync(string? userId, CancellationToken cancellationToken)
    {
        if (!HasCredentials)
            return new EarthdataTestResult(EarthdataTestStatus.MissingCredentials, "CMR auth: missing token");

        var query = string.IsNullOrWhiteSpace(userId)
            ? "system_object=GROUP&user_type=registered"
            : $"system_object=GROUP&user_id={Uri.EscapeDataString(userId)}";
        var uri = new Uri($"{CmrPermissions}?{query}");

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new EarthdataTestResult(EarthdataTestStatus.Unauthorized, "CMR auth: HTTP 401 Unauthorized");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new EarthdataTestResult(EarthdataTestStatus.AccessDenied, "CMR auth: HTTP 403 Forbidden");

        if ((int)response.StatusCode is < 200 or >= 300)
            return new EarthdataTestResult(EarthdataTestStatus.Error, $"CMR auth: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        var permissions = await TryParsePermissionsAsync(response, cancellationToken);
        var summary = permissions.Count == 0
            ? "CMR auth: OK (no permissions returned)"
            : $"CMR auth: OK ({string.Join("; ", permissions.Select(p => $"{p.Key}={string.Join(',', p.Value)}"))})";
        return new EarthdataTestResult(EarthdataTestStatus.Success, summary);
    }

    private static async Task<Dictionary<string, List<string>>> TryParsePermissionsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return results;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var list = new List<string>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            list.Add(value);
                    }
                }
                results[prop.Name] = list;
            }
        }
        catch
        {
            return results;
        }
        return results;
    }

    public async Task<IReadOnlyList<string>> EnsureDemFilesAsync((double West, double South, double East, double North) bounds, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ContourPaths.DemRoot);
        var urls = await FindGranuleUrlsAsync(bounds, cancellationToken);
        if (urls.Count == 0)
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var url in urls)
        {
            var fileName = Path.GetFileName(url.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var localPath = Path.Combine(ContourPaths.DemRoot, fileName);
            if (!File.Exists(localPath))
            {
                Log(ContourLogLevel.Info, $"Downloading DEM {fileName}");
                await DownloadAsync(url, localPath, cancellationToken);
                Log(ContourLogLevel.Info, $"Downloaded DEM {fileName}");
            }

            if (localPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractedDir = Path.Combine(ContourPaths.DemRoot, "extracted", Path.GetFileNameWithoutExtension(fileName));
                Directory.CreateDirectory(extractedDir);
                if (!Directory.EnumerateFiles(extractedDir, "*.hgt", SearchOption.AllDirectories).Any())
                {
                    ZipFile.ExtractToDirectory(localPath, extractedDir, true);
                }
                results.AddRange(Directory.EnumerateFiles(extractedDir, "*.hgt", SearchOption.AllDirectories));
            }
            else if (localPath.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase) || localPath.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(localPath);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Uri?> GetAnyGranuleUrlAsync((double West, double South, double East, double North) bounds, CancellationToken cancellationToken)
    {
        var urls = await FindGranuleUrlsAsync(bounds, cancellationToken);
        return urls.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Uri>> GetGranuleUrlsAsync(
        (double West, double South, double East, double North) bounds,
        CancellationToken cancellationToken,
        int maxCount)
    {
        var urls = await FindGranuleUrlsAsync(bounds, cancellationToken);
        if (maxCount <= 0)
            return urls;
        return urls.Take(maxCount).ToList();
    }

    public async Task<EarthdataTestResult> CheckAccessAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!HasCredentials)
            return new EarthdataTestResult(EarthdataTestStatus.MissingCredentials);

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        var result = await TryRequestAsync(client, url, useRange: true, _token, cancellationToken);
        if (result.Status == EarthdataTestStatus.Error && result.Detail?.Contains("HTTP 400") == true)
        {
            result = await TryRequestAsync(client, url, useRange: false, _token, cancellationToken);
        }

        return result;
    }

    private static async Task<EarthdataTestResult> TryRequestAsync(HttpClient client, Uri url, bool useRange, string? bearerToken, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(client, url, useRange, bearerToken, cancellationToken, redirectRemaining: 1);
        if (response.Status == EarthdataTestStatus.Error &&
            !string.IsNullOrWhiteSpace(bearerToken))
        {
            if (response.Detail?.Contains("HTTP 400") == true ||
                response.Detail?.Contains("HTTP 301") == true ||
                response.Detail?.Contains("HTTP 302") == true ||
                response.Detail?.Contains("HTTP 303") == true)
            {
                var fallback = await SendRequestAsync(client, url, useRange, null, cancellationToken, redirectRemaining: 1);
                if (fallback.Status == EarthdataTestStatus.Success)
                {
                    return fallback with { Detail = $"{fallback.Detail} (no-auth fallback)" };
                }
                return fallback;
            }
        }
        return response;
    }

    private static async Task<EarthdataTestResult> SendRequestAsync(
        HttpClient client,
        Uri url,
        bool useRange,
        string? bearerToken,
        CancellationToken cancellationToken,
        int redirectRemaining)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (useRange)
        {
            request.Headers.Range = new RangeHeaderValue(0, 0);
        }
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new EarthdataTestResult(EarthdataTestStatus.Unauthorized, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({AuthLabel(bearerToken)})");

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new EarthdataTestResult(EarthdataTestStatus.AccessDenied, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({AuthLabel(bearerToken)})");

        if (IsRedirectToUrs(response))
            return new EarthdataTestResult(EarthdataTestStatus.Unauthorized, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} -> urs.earthdata.nasa.gov ({AuthLabel(bearerToken)})");

        if (IsRedirect(response.StatusCode))
        {
            var location = response.Headers.Location;
            if (location is null || redirectRemaining <= 0)
                return new EarthdataTestResult(EarthdataTestStatus.Error, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({AuthLabel(bearerToken)})");

            var next = location.IsAbsoluteUri ? location : new Uri(url, location);
            return await SendRequestAsync(client, next, useRange, bearerToken, cancellationToken, redirectRemaining - 1);
        }

        if (response.StatusCode == HttpStatusCode.PartialContent || (int)response.StatusCode is >= 200 and < 300)
            return new EarthdataTestResult(EarthdataTestStatus.Success, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({AuthLabel(bearerToken)})");

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
        var detail = response.Headers.Location is null
            ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({contentType}; {AuthLabel(bearerToken)})"
            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} ({contentType}; {AuthLabel(bearerToken)}) -> {response.Headers.Location}";

        return new EarthdataTestResult(EarthdataTestStatus.Error, detail);
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private async Task<List<Uri>> FindGranuleUrlsAsync((double West, double South, double East, double North) bounds, CancellationToken cancellationToken)
    {
        var bbox = $"{Fmt(bounds.West)},{Fmt(bounds.South)},{Fmt(bounds.East)},{Fmt(bounds.North)}";
        var query = $"short_name=NASADEM_HGT&version=001&bounding_box={Uri.EscapeDataString(bbox)}&page_size=2000";
        var uri = new Uri($"{CmrGranules}?{query}");

        using var response = await _searchClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var snippet = await ReadErrorSnippetAsync(response, cancellationToken);
            throw new HttpRequestException($"CMR search failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {snippet}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var urls = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("feed", out var feed))
            return urls.ToList();
        if (!feed.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return urls.ToList();

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var link in links.EnumerateArray())
            {
                if (!link.TryGetProperty("href", out var hrefElement))
                    continue;
                var href = hrefElement.GetString();
                if (string.IsNullOrWhiteSpace(href))
                    continue;
                if (!href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsDataLink(link, href))
                    continue;
                if (!IsLikelyDataFile(href))
                    continue;

                if (Uri.TryCreate(href, UriKind.Absolute, out var uriValue))
                {
                    if (seen.Add(uriValue.AbsoluteUri))
                    {
                        urls.Add(uriValue);
                    }
                }
            }
        }

        return urls.OrderBy(ScoreUrl).ToList();
    }

    private static async Task<string> ReadErrorSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
                return "empty response body";

            var trimmed = content.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (trimmed.Length > 800)
                trimmed = trimmed[..800] + "...";
            return trimmed;
        }
        catch
        {
            return "failed to read response body";
        }
    }

    private static int ScoreUrl(Uri uri)
    {
        var host = uri.Host ?? string.Empty;
        if (host.Contains("e4ftl", StringComparison.OrdinalIgnoreCase)
            || host.Contains("lpdaac", StringComparison.OrdinalIgnoreCase))
        {
            if (!host.Contains("earthdatacloud", StringComparison.OrdinalIgnoreCase))
                return 0;
        }
        if (host.Contains("earthdatacloud", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private static bool IsRedirectToUrs(HttpResponseMessage response)
    {
        if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400)
            return false;
        var location = response.Headers.Location;
        if (location is null)
            return false;
        var host = location.Host ?? string.Empty;
        return host.Contains("urs.earthdata.nasa.gov", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDataLink(JsonElement link, string href)
    {
        if (link.TryGetProperty("rel", out var relElement))
        {
            var rel = relElement.GetString();
            if (!string.IsNullOrWhiteSpace(rel) && rel.Contains("data#", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return href.Contains("data.lpdaac", StringComparison.OrdinalIgnoreCase)
               || href.Contains("lpdaac", StringComparison.OrdinalIgnoreCase)
               || href.Contains("e4ftl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyDataFile(string href)
    {
        return href.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
               || href.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase)
               || href.EndsWith(".tif", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadAsync(Uri uri, string localPath, CancellationToken cancellationToken)
    {
        var tempPath = localPath + ".tmp";
        var result = await TryDownloadAsync(_downloadClient, uri, tempPath, cancellationToken);
        if (result == DownloadResult.BadRequest && HasCredentials)
        {
            using var fallbackClient = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromMinutes(5),
            };
            fallbackClient.DefaultRequestHeaders.UserAgent.Clear();
            fallbackClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));
            result = await TryDownloadAsync(fallbackClient, uri, tempPath, cancellationToken);
        }

        if (result != DownloadResult.Success)
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            Log(ContourLogLevel.Error, $"Download failed: {uri} ({result})");
            throw new HttpRequestException($"Download failed: {result}");
        }

        if (File.Exists(localPath))
            File.Delete(localPath);
        File.Move(tempPath, localPath);
    }

    private static async Task<DownloadResult> TryDownloadAsync(HttpClient client, Uri uri, string tempPath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return DownloadResult.BadRequest;
            if (!response.IsSuccessStatusCode)
                return DownloadResult.Failed;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(file, cancellationToken);
            return DownloadResult.Success;
        }
        catch
        {
            return DownloadResult.Failed;
        }
    }

    private enum DownloadResult
    {
        Success,
        BadRequest,
        Failed,
    }

    private static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static string AuthLabel(string? bearerToken)
    {
        return string.IsNullOrWhiteSpace(bearerToken) ? "no-auth" : "bearer";
    }

    private void Log(ContourLogLevel level, string message)
    {
        _log?.Invoke(level, message);
    }
}

internal sealed class GdalRunner
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();
    private static readonly TimeSpan RetryProbeInterval = TimeSpan.FromSeconds(20);
    private bool? _available;
    private DateTimeOffset _nextAvailabilityProbeUtc = DateTimeOffset.MinValue;
    private string? _gdalBin;
    private bool _searched;

    public async Task<bool> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (_available == true)
            return _available.Value;

        if (_available == false && DateTimeOffset.UtcNow < _nextAvailabilityProbeUtc)
            return false;

        if (!_searched || _available == false)
        {
            _gdalBin = FindGdalBin();
            _searched = true;
        }

        try
        {
            var exitCode = await RunAsync("gdalinfo", "--version", cancellationToken);
            _available = exitCode == 0;
        }
        catch
        {
            _available = false;
        }

        _nextAvailabilityProbeUtc = _available == true
            ? DateTimeOffset.MaxValue
            : DateTimeOffset.UtcNow.Add(RetryProbeInterval);

        return _available.Value;
    }

    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        if (!_searched)
        {
            _gdalBin = FindGdalBin();
            _searched = true;
        }

        var exePath = ResolveExecutable(fileName);
        var envPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var gdalBin = _gdalBin;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(gdalBin) && !PathContainsDirectory(envPath, gdalBin))
        {
            startInfo.Environment["PATH"] = $"{gdalBin}{Path.PathSeparator}{envPath}";
        }

        var gdalRoot = GetGdalRoot(gdalBin);
        if (!string.IsNullOrWhiteSpace(gdalRoot))
        {
            var gdalData = Path.Combine(gdalRoot, "share", "gdal");
            if (Directory.Exists(gdalData))
            {
                startInfo.Environment["GDAL_DATA"] = gdalData;
            }

            var projLib = Path.Combine(gdalRoot, "share", "proj");
            if (Directory.Exists(projLib))
            {
                startInfo.Environment["PROJ_LIB"] = projLib;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string? FindGdalBin()
    {
        var executableName = ToExecutableName("gdalinfo");
        var fromPath = FindInPath(executableName);
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath;

        var gdalBinOverride = Environment.GetEnvironmentVariable("GDAL_BIN");
        if (!string.IsNullOrWhiteSpace(gdalBinOverride))
        {
            var overrideBin = gdalBinOverride.Trim();
            if (File.Exists(Path.Combine(overrideBin, executableName)))
                return overrideBin;
        }

        if (!IsWindows)
        {
            foreach (var bin in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/opt/local/bin" })
            {
                if (File.Exists(Path.Combine(bin, executableName)))
                    return bin;
            }
            return null;
        }

        var roots = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(programFiles);
        if (!string.IsNullOrWhiteSpace(programFilesX86) && !string.Equals(programFilesX86, programFiles, StringComparison.OrdinalIgnoreCase))
            roots.Add(programFilesX86);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            var qgisDirs = Directory.GetDirectories(root, "QGIS *", SearchOption.TopDirectoryOnly)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var qgisDir in qgisDirs)
            {
                var bin = Path.Combine(qgisDir, "bin");
                if (File.Exists(Path.Combine(bin, executableName)))
                    return bin;
            }
        }

        foreach (var osgeo in new[] { "C:\\OSGeo4W64", "C:\\OSGeo4W" })
        {
            var bin = Path.Combine(osgeo, "bin");
            if (File.Exists(Path.Combine(bin, executableName)))
                return bin;
        }

        return null;
    }

    private static string? FindInPath(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            var candidate = Path.Combine(trimmed, exeName);
            if (File.Exists(candidate))
                return trimmed;
        }

        return null;
    }

    private string ResolveExecutable(string fileName)
    {
        var exeName = ToExecutableName(fileName);
        if (!string.IsNullOrWhiteSpace(_gdalBin))
        {
            var candidate = Path.Combine(_gdalBin, exeName);
            if (File.Exists(candidate))
                return candidate;
        }
        return exeName;
    }

    private static bool PathContainsDirectory(string pathValue, string directory)
    {
        var target = NormalizePath(directory);
        var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(NormalizePath(entry), target, comparison))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ToExecutableName(string fileName)
    {
        if (IsWindows)
            return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.exe";

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static string? GetGdalRoot(string? gdalBin)
    {
        if (string.IsNullOrWhiteSpace(gdalBin))
            return null;

        try
        {
            var dir = new DirectoryInfo(gdalBin);
            if (dir.Exists && dir.Parent is not null)
                return dir.Parent.FullName;
        }
        catch
        {
            return null;
        }

        return null;
    }
}

internal static class ContourPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "TrailMateCenter",
        "contours");

    public static readonly string DemRoot = Path.Combine(Root, "dem");

    public static readonly string WorkRoot = Path.Combine(Root, "work");
}

internal enum EarthdataTestStatus
{
    Success,
    MissingCredentials,
    NoDataInView,
    Unauthorized,
    AccessDenied,
    NoViewport,
    Error,
}

internal readonly record struct EarthdataTestResult(EarthdataTestStatus Status, string? Detail = null);

internal readonly record struct EarthdataTokenInfo(string? UserId, DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt, string? Issuer)
{
    public static bool TryParse(string token, out EarthdataTokenInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length < 2)
            return false;

        if (!TryBase64UrlDecode(parts[1], out var payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var userId = root.TryGetProperty("uid", out var uidElement) ? uidElement.GetString() : null;
            var issuer = root.TryGetProperty("iss", out var issElement) ? issElement.GetString() : null;
            var issuedAt = TryGetUnixTime(root, "iat");
            var expiresAt = TryGetUnixTime(root, "exp");
            info = new EarthdataTokenInfo(userId, issuedAt, expiresAt, issuer);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DateTimeOffset? TryGetUnixTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
            return null;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
            return DateTimeOffset.FromUnixTimeSeconds(value);
        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var textValue))
            return DateTimeOffset.FromUnixTimeSeconds(textValue);
        return null;
    }

    private static bool TryBase64UrlDecode(string input, out byte[] output)
    {
        output = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = input.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding == 2)
            normalized += "==";
        else if (padding == 3)
            normalized += "=";
        else if (padding != 0)
            return false;

        try
        {
            output = Convert.FromBase64String(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal enum ContourLogLevel
{
    Info,
    Warning,
    Error,
}
