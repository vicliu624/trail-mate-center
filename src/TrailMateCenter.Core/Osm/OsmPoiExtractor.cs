using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;

namespace TrailMateCenter.Osm;

public sealed record OsmPoiExtractionOptions
{
    public string PbfPath { get; init; } = string.Empty;
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public IReadOnlyCollection<string> SelectedPoiTypes { get; init; } = Array.Empty<string>();
    public bool IncludeOriginalTags { get; init; }
    public bool IncludeWays { get; init; } = true;
    public int? MaxPois { get; init; }
    public string NameLanguage { get; init; } = "default";
    public int ProgressInterval { get; init; } = 25_000;
}

public sealed record OsmPoiExtractionProgress(long ProcessedElements, long ExtractedPoiCount);

public sealed class OsmPoiExtractor
{
    private readonly OsmTagMapper _tagMapper;

    public OsmPoiExtractor(OsmTagMapper? tagMapper = null)
    {
        _tagMapper = tagMapper ?? new OsmTagMapper();
    }

    public async Task<IReadOnlyList<PoiRecord>> ExtractAsync(
        OsmPoiExtractionOptions options,
        IProgress<OsmPoiExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pois = new List<PoiRecord>();
        await ExtractToAsync(
                options,
                poi =>
                {
                    pois.Add(poi);
                    return ValueTask.CompletedTask;
                },
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return pois;
    }

    public async Task ExtractToAsync(
        OsmPoiExtractionOptions options,
        Func<PoiRecord, ValueTask> onPoi,
        IProgress<OsmPoiExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (onPoi is null)
            throw new ArgumentNullException(nameof(onPoi));
        if (string.IsNullOrWhiteSpace(options.PbfPath))
            throw new ArgumentException("PBF file path is required.", nameof(options));
        if (!File.Exists(options.PbfPath))
            throw new FileNotFoundException("PBF file missing.", options.PbfPath);

        await Task.Run(
                () => ExtractCore(options, onPoi, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void ExtractCore(
        OsmPoiExtractionOptions options,
        Func<PoiRecord, ValueTask> onPoi,
        IProgress<OsmPoiExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bounds = options.Bounds.Normalize();
        var polygonFilter = GeoJsonPointInPolygonFilter.TryCreate(options.BoundaryGeoJson);
        var selectedTypes = new HashSet<string>(
            options.SelectedPoiTypes
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (selectedTypes.Count == 0)
            return;

        var nodeLocations = new Dictionary<long, (double Lat, double Lon)>();
        var processed = 0L;
        var extracted = 0L;
        var progressInterval = Math.Max(1000, options.ProgressInterval);

        using var stream = File.OpenRead(options.PbfPath);
        using var source = new PBFOsmStreamSource(stream);
        foreach (var osmGeo in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            switch (osmGeo)
            {
                case Node node:
                    EmitIfPresent(ExtractNode(node, bounds, polygonFilter, selectedTypes, options, nodeLocations), onPoi, ref extracted);
                    break;
                case Way way when options.IncludeWays:
                    EmitIfPresent(ExtractWay(way, bounds, polygonFilter, selectedTypes, options, nodeLocations), onPoi, ref extracted);
                    break;
            }

            if (options.MaxPois is > 0 && extracted >= options.MaxPois.Value)
                break;

            if (processed % progressInterval == 0)
            {
                progress?.Report(new OsmPoiExtractionProgress(processed, extracted));
            }
        }

        progress?.Report(new OsmPoiExtractionProgress(processed, extracted));
    }

    private static void EmitIfPresent(
        PoiRecord? poi,
        Func<PoiRecord, ValueTask> onPoi,
        ref long extracted)
    {
        if (poi is null)
            return;

        onPoi(poi).AsTask().GetAwaiter().GetResult();
        extracted++;
    }

    private PoiRecord? ExtractNode(
        Node node,
        GeoBounds bounds,
        GeoJsonPointInPolygonFilter? polygonFilter,
        ISet<string> selectedTypes,
        OsmPoiExtractionOptions options,
        Dictionary<long, (double Lat, double Lon)> nodeLocations)
    {
        if (!node.Id.HasValue || !node.Latitude.HasValue || !node.Longitude.HasValue)
            return null;

        var lat = node.Latitude.Value;
        var lon = node.Longitude.Value;
        if (!bounds.Contains(lat, lon))
            return null;
        if (polygonFilter is not null && !polygonFilter.Contains(lat, lon))
            return null;

        nodeLocations[node.Id.Value] = (lat, lon);
        var tags = ToDictionary(node.Tags);
        var mapping = _tagMapper.Map(tags, selectedTypes);
        if (mapping is null)
            return null;

        return new PoiRecord
        {
            Id = $"osm-node-{node.Id.Value}",
            Type = mapping.Type,
            Name = SelectName(tags, options.NameLanguage),
            Latitude = lat,
            Longitude = lon,
            Priority = mapping.Priority,
            Source = "osm",
            Tags = options.IncludeOriginalTags ? tags : new Dictionary<string, string>(),
        };
    }

    private PoiRecord? ExtractWay(
        Way way,
        GeoBounds bounds,
        GeoJsonPointInPolygonFilter? polygonFilter,
        ISet<string> selectedTypes,
        OsmPoiExtractionOptions options,
        IReadOnlyDictionary<long, (double Lat, double Lon)> nodeLocations)
    {
        if (!way.Id.HasValue || way.Nodes is null || way.Nodes.Length == 0)
            return null;

        var tags = ToDictionary(way.Tags);
        var mapping = _tagMapper.Map(tags, selectedTypes);
        if (mapping is null)
            return null;

        var points = new List<(double Lat, double Lon)>();
        foreach (var nodeId in way.Nodes)
        {
            if (nodeLocations.TryGetValue(nodeId, out var point))
                points.Add(point);
        }

        if (points.Count == 0)
            return null;

        var lat = points.Average(static p => p.Lat);
        var lon = points.Average(static p => p.Lon);
        if (!bounds.Contains(lat, lon))
            return null;
        if (polygonFilter is not null && !polygonFilter.Contains(lat, lon))
            return null;

        return new PoiRecord
        {
            Id = $"osm-way-{way.Id.Value}",
            Type = mapping.Type,
            Name = SelectName(tags, options.NameLanguage),
            Latitude = lat,
            Longitude = lon,
            Priority = mapping.Priority,
            Source = "osm",
            Tags = options.IncludeOriginalTags ? tags : new Dictionary<string, string>(),
        };
    }

    private static Dictionary<string, string> ToDictionary(TagsCollectionBase? tags)
    {
        if (tags is null || tags.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Key))
                continue;
            result[tag.Key] = tag.Value ?? string.Empty;
        }

        return result;
    }

    private static string? SelectName(IReadOnlyDictionary<string, string> tags, string? preferredLanguage)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguage) &&
            !string.Equals(preferredLanguage, "default", StringComparison.OrdinalIgnoreCase) &&
            tags.TryGetValue($"name:{preferredLanguage.Trim()}", out var localizedName) &&
            !string.IsNullOrWhiteSpace(localizedName))
        {
            return localizedName;
        }

        if (tags.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return null;
    }
}
