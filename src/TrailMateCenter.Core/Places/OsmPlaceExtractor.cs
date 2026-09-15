using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.Places;

public sealed class OsmPlaceExtractor
{
    private readonly OsmPlaceTagMapper _tagMapper;

    public OsmPlaceExtractor(OsmPlaceTagMapper? tagMapper = null)
    {
        _tagMapper = tagMapper ?? new OsmPlaceTagMapper();
    }

    public async Task<IReadOnlyList<PlaceRecord>> ExtractAsync(
        PlaceExtractionOptions options,
        IProgress<PlaceExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var places = new List<PlaceRecord>();
        await ExtractToAsync(
                options,
                place =>
                {
                    places.Add(place);
                    return ValueTask.CompletedTask;
                },
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return places;
    }

    public async Task ExtractToAsync(
        PlaceExtractionOptions options,
        Func<PlaceRecord, ValueTask> onPlace,
        IProgress<PlaceExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (onPlace is null)
            throw new ArgumentNullException(nameof(onPlace));
        if (string.IsNullOrWhiteSpace(options.PbfPath))
            throw new ArgumentException("PBF file path is required.", nameof(options));
        if (!File.Exists(options.PbfPath))
            throw new FileNotFoundException("PBF file missing.", options.PbfPath);

        await Task.Run(
                () => ExtractCore(options, onPlace, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void ExtractCore(
        PlaceExtractionOptions options,
        Func<PlaceRecord, ValueTask> onPlace,
        IProgress<PlaceExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bounds = options.Bounds.Normalize();
        var polygonFilter = GeoJsonPointInPolygonFilter.TryCreate(options.BoundaryGeoJson);
        var processed = 0L;
        var extracted = 0L;
        var progressInterval = Math.Max(1000, options.ProgressInterval);

        using var stream = File.OpenRead(options.PbfPath);
        using var source = new PBFOsmStreamSource(stream);
        foreach (var osmGeo in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            if (osmGeo is Node node)
                EmitIfPresent(ExtractNode(node, bounds, polygonFilter, options), onPlace, ref extracted);

            if (options.MaxPlaces is > 0 && extracted >= options.MaxPlaces.Value)
                break;

            if (processed % progressInterval == 0)
                progress?.Report(new PlaceExtractionProgress(processed, extracted));
        }

        progress?.Report(new PlaceExtractionProgress(processed, extracted));
    }

    private static void EmitIfPresent(
        PlaceRecord? place,
        Func<PlaceRecord, ValueTask> onPlace,
        ref long extracted)
    {
        if (place is null)
            return;

        onPlace(place).AsTask().GetAwaiter().GetResult();
        extracted++;
    }

    private PlaceRecord? ExtractNode(
        Node node,
        GeoBounds bounds,
        GeoJsonPointInPolygonFilter? polygonFilter,
        PlaceExtractionOptions options)
    {
        if (!node.Id.HasValue || !node.Latitude.HasValue || !node.Longitude.HasValue)
            return null;

        var lat = node.Latitude.Value;
        var lon = node.Longitude.Value;
        if (!bounds.Contains(lat, lon))
            return null;
        if (polygonFilter is not null && !polygonFilter.Contains(lat, lon))
            return null;

        var tags = ToDictionary(node.Tags);
        var mapping = _tagMapper.Map(tags);
        if (mapping is null)
            return null;

        var names = PlaceNameNormalizer.CollectNames(tags, options.NameLanguage);
        if (names.Count == 0)
            return null;

        return new PlaceRecord
        {
            Id = $"osm-node-{node.Id.Value}",
            Category = mapping.Category,
            PrimaryName = names[0],
            Names = names,
            Latitude = lat,
            Longitude = lon,
            Rank = mapping.Rank,
            Source = "osm",
            OsmType = "node",
            OsmId = node.Id.Value,
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
}
