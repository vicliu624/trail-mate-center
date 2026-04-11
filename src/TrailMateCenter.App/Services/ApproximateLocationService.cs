using System.Text.Json;
using Microsoft.Extensions.Logging;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public sealed class ApproximateLocationService
{
    private readonly SessionStore _sessionStore;
    private readonly ILogger<ApproximateLocationService> _logger;

    public ApproximateLocationService(SessionStore sessionStore, ILogger<ApproximateLocationService> logger)
    {
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<ApproximateLocationResult?> ResolveAsync(CancellationToken cancellationToken)
    {
        var latestPosition = _sessionStore.SnapshotPositions()
            .OrderByDescending(position => position.Timestamp)
            .FirstOrDefault();
        if (latestPosition is not null)
        {
            return new ApproximateLocationResult(
                latestPosition.Latitude,
                latestPosition.Longitude,
                "session-position",
                latestPosition.Label ?? latestPosition.Source.ToString());
        }

        var latestNodeLocation = _sessionStore.SnapshotNodeInfos()
            .Where(node => node.Latitude.HasValue && node.Longitude.HasValue)
            .OrderByDescending(node => node.LastHeard)
            .FirstOrDefault();
        if (latestNodeLocation is not null)
        {
            return new ApproximateLocationResult(
                latestNodeLocation.Latitude!.Value,
                latestNodeLocation.Longitude!.Value,
                "node-info",
                latestNodeLocation.LongName ?? latestNodeLocation.ShortName ?? $"0x{latestNodeLocation.NodeId:X8}");
        }

        return await ResolveViaIpAsync(cancellationToken);
    }

    private async Task<ApproximateLocationResult?> ResolveViaIpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(4),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TrailMateCenter/1.0");

            using var response = await client.GetAsync("https://ipwho.is/", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Approximate IP location lookup failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                return null;
            if (!root.TryGetProperty("latitude", out var latitude) || !latitude.TryGetDouble(out var lat))
                return null;
            if (!root.TryGetProperty("longitude", out var longitude) || !longitude.TryGetDouble(out var lon))
                return null;

            var city = ReadString(root, "city");
            var region = ReadString(root, "region");
            var country = ReadString(root, "country");
            var label = string.Join(", ", new[] { city, region, country }.Where(static part => !string.IsNullOrWhiteSpace(part)));
            return new ApproximateLocationResult(lat, lon, "ip-geolocation", label);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation(ex, "Approximate IP location lookup failed.");
            return null;
        }
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}

public sealed record ApproximateLocationResult(
    double Latitude,
    double Longitude,
    string Source,
    string Label);
