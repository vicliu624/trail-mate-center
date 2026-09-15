using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrailMateCenter.Maps.Rendering;

public sealed record MapAnnotationIndexResult(long Rows, int TileFiles, int ClippedTiles, IReadOnlyList<int> RequiredCodepoints)
{
    public int PoiFeatures { get; init; }
    public int RoadFeatures { get; init; }
    public int PlaceFeatures { get; init; }
}

/// <summary>Writes one typed spatial layer: places, roads and POIs share its manifest and index.</summary>
internal static class MapAnnotationIndexWriter
{
    public const int MaximumRecordsPerTile = 200;
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<MapAnnotationIndexResult> WriteAsync(PbfBasemapStore store, string mapsRoot, GeoBounds bounds,
        int minimumZoom, int maximumZoom, MapAnnotationOptions options, CancellationToken token)
    {
        if (minimumZoom < 1 || maximumZoom > 18 || minimumZoom > maximumZoom) throw new ArgumentOutOfRangeException(nameof(minimumZoom));
        var poiRoot = Path.Combine(mapsRoot, "poi");
        Directory.CreateDirectory(poiRoot);
        var glyphs = new SortedSet<int>();
        var features = new[] { new HashSet<string>(), new HashSet<string>(), new HashSet<string>() };
        long rows = 0;
        var files = 0;
        var clipped = 0;
        var perZoom = new Dictionary<int, long>();
        for (var z = minimumZoom; z <= maximumZoom; z++)
        {
            var range = TileMath.BoundsToTileRange(bounds, z);
            for (var x = range.MinX; x <= range.MaxX; x++)
            for (var y = range.MinY; y <= range.MaxY; y++)
            {
                token.ThrowIfCancellationRequested();
                var tile = new TileCoordinate(z, x, y);
                // Separate bounded candidate lists stop a single class from
                // consuming every export slot. Unused class slots are shared.
                var groups = new[] { new List<MapAnnotationCandidate>(), new List<MapAnnotationCandidate>(), new List<MapAnnotationCandidate>() };
                var total = 0;
                foreach (var feature in store.QueryAnnotations(MapAnnotationCandidateBuilder.TileEnvelope(tile), z))
                foreach (var candidate in MapAnnotationCandidateBuilder.Build(feature, tile, options))
                {
                    token.ThrowIfCancellationRequested();
                    total++;
                    var group = groups[(int)candidate.Kind];
                    if (group.Count < MaximumRecordsPerTile) group.Add(candidate);
                }
                if (total == 0) continue;
                if (total > MaximumRecordsPerTile) clipped++;
                var selected = new List<MapAnnotationCandidate>(MaximumRecordsPerTile);
                var reservation = new[] { 40, 72, 88 }; // place, road, facility
                for (var i = 0; i < groups.Length; i++) selected.AddRange(groups[i].Take(reservation[i]));
                selected.AddRange(groups.SelectMany((group, i) => group.Skip(reservation[i]))
                    .OrderByDescending(c => c.Priority).ThenBy(c => c.Id, StringComparer.Ordinal)
                    .Take(MaximumRecordsPerTile - selected.Count));
                var path = Path.Combine(poiRoot, "index", z.ToString(), x.ToString(), $"{y}.jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
                foreach (var candidate in selected)
                {
                    features[(int)candidate.Kind].Add(candidate.FeatureId);
                    foreach (var rune in candidate.Name.EnumerateRunes()) if (rune.Value > 127) glyphs.Add(rune.Value);
                    var payload = new
                    {
                        id = candidate.Id, feature_id = candidate.FeatureId,
                        kind = candidate.Kind.ToString().ToLowerInvariant(), type = candidate.Category,
                        name = candidate.Name, lat = candidate.Latitude, lon = candidate.Longitude,
                        priority = candidate.Priority, path = candidate.Path,
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(payload, Json).AsMemory(), token);
                    rows++;
                    perZoom[z] = perZoom.GetValueOrDefault(z) + 1;
                }
                files++;
            }
        }
        var manifest = new
        {
            version = 3, generator = "TrailMateCenter", policy_version = MapAnnotationPolicy.Version,
            text_background = "transparent", text_halo = false,
            annotation_options = options,
            index = new
            {
                enabled_zoom_levels = Enumerable.Range(minimumZoom, maximumZoom - minimumZoom + 1),
                scheme = "web-mercator-xyz", format = "jsonl", include_labels = true,
                max_poi_per_tile = MaximumRecordsPerTile, kinds = new[] { "place", "road", "poi" },
                geometry = "tile-local-pixels", max_path_points = MapAnnotationCandidateBuilder.MaximumPathPoints,
                tile_margin = MapAnnotationCandidateBuilder.TileMargin,
                clipped_tiles = clipped > 0, clipped_tile_count = clipped, rows_by_zoom = perZoom,
            },
        };
        await File.WriteAllTextAsync(Path.Combine(poiRoot, "manifest.json"), JsonSerializer.Serialize(manifest, Json), token);
        return new(rows, files, clipped, glyphs.ToArray())
        {
            PlaceFeatures = features[(int)MapAnnotationKind.Place].Count,
            RoadFeatures = features[(int)MapAnnotationKind.Road].Count,
            PoiFeatures = features[(int)MapAnnotationKind.Poi].Count,
        };
    }
}
