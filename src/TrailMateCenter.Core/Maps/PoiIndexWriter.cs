using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TrailMateCenter.Maps;

public sealed class PoiIndexWriter
{
    private const int MaxOpenBucketWriters = 256;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly PoiPriorityRules _priorityRules;

    public PoiIndexWriter(PoiPriorityRules? priorityRules = null)
    {
        _priorityRules = priorityRules ?? PoiPriorityRules.Default;
    }

    public async Task<PoiIndexWriteSummary> WriteAsync(
        string mapsRoot,
        IEnumerable<PoiRecord> pois,
        PoiManifestInput manifestInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapsRoot))
            throw new ArgumentException("Maps root is required.", nameof(mapsRoot));
        if (pois is null)
            throw new ArgumentNullException(nameof(pois));
        if (manifestInput is null)
            throw new ArgumentNullException(nameof(manifestInput));

        var options = manifestInput.Index.Normalize();
        var poiRoot = Path.Combine(mapsRoot, "poi");
        var parentRoot = Path.GetDirectoryName(poiRoot);
        if (string.IsNullOrWhiteSpace(parentRoot))
            throw new InvalidOperationException("Invalid maps root.");

        Directory.CreateDirectory(parentRoot);
        var tempRoot = Path.Combine(parentRoot, $".poi-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var summary = await WriteCoreAsync(tempRoot, pois, manifestInput with { Index = options }, cancellationToken)
                .ConfigureAwait(false);

            if (Directory.Exists(poiRoot))
                Directory.Delete(poiRoot, recursive: true);
            Directory.Move(tempRoot, poiRoot);
            return summary;
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    public string SerializePoi(PoiRecord poi, PoiIndexOptions options, bool forIndex)
    {
        var normalized = options.Normalize();
        return normalized.OutputFormat == PoiOutputFormat.Compact
            ? SerializeCompactPoi(poi, normalized, forIndex)
            : SerializeReadablePoi(poi, normalized, forIndex);
    }

    public async Task<PoiIndexWriteSummary> WriteStreamingAsync(
        string mapsRoot,
        PoiManifestInput manifestInput,
        Func<Func<PoiRecord, ValueTask>, Task> populatePoisAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapsRoot))
            throw new ArgumentException("Maps root is required.", nameof(mapsRoot));
        if (manifestInput is null)
            throw new ArgumentNullException(nameof(manifestInput));
        if (populatePoisAsync is null)
            throw new ArgumentNullException(nameof(populatePoisAsync));

        var options = manifestInput.Index.Normalize();
        var poiRoot = Path.Combine(mapsRoot, "poi");
        var parentRoot = Path.GetDirectoryName(poiRoot);
        if (string.IsNullOrWhiteSpace(parentRoot))
            throw new InvalidOperationException("Invalid maps root.");

        Directory.CreateDirectory(parentRoot);
        var tempRoot = Path.Combine(parentRoot, $".poi-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var builder = new StreamingPoiIndexBuild(
                this,
                tempRoot,
                manifestInput with { Index = options },
                _priorityRules);

            await populatePoisAsync(poi => builder.AddAsync(poi, cancellationToken)).ConfigureAwait(false);
            var summary = await builder.CompleteAsync(cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(poiRoot))
                Directory.Delete(poiRoot, recursive: true);
            Directory.Move(tempRoot, poiRoot);
            return summary;
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    public PoiManifest BuildManifest(PoiManifestInput input, PoiIndexWriteSummary summary, DateTimeOffset createdAt)
    {
        var options = input.Index.Normalize();
        var bounds = input.Area.Bounds.Normalize();
        return new PoiManifest
        {
            Source = new PoiManifestSource
            {
                Type = input.Source.Type,
                Name = input.Source.Name,
                Provider = input.Source.Provider,
                DownloadUrl = input.Source.DownloadUrl,
                License = input.Source.License,
            },
            Area = new PoiManifestArea
            {
                Name = input.Area.Name,
                AdminLevel = input.Area.AdminLevel,
                Bounds = new PoiManifestBounds
                {
                    West = bounds.West,
                    South = bounds.South,
                    East = bounds.East,
                    North = bounds.North,
                },
            },
            Index = new PoiManifestIndex
            {
                MinZoom = options.MinZoom,
                MaxZoom = options.MaxZoom,
                MaxPoiPerTile = options.MaxPoiPerTile,
                IncludeLabels = options.IncludeLabels,
                IncludeOriginalTags = options.IncludeOriginalTags,
                OutputFormat = options.OutputFormat == PoiOutputFormat.Compact ? "compact" : "readable",
                ClippedTiles = summary.WasAnyTileClipped,
                ClippedTileCountByZoom = summary.ClippedTileCountByZoom,
            },
            PoiTypes = input.PoiTypes
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            NameLanguage = string.IsNullOrWhiteSpace(input.NameLanguage) ? "default" : input.NameLanguage.Trim(),
            CreatedAt = createdAt,
        };
    }

    private async Task<PoiIndexWriteSummary> WriteCoreAsync(
        string poiRoot,
        IEnumerable<PoiRecord> pois,
        PoiManifestInput manifestInput,
        CancellationToken cancellationToken)
    {
        var options = manifestInput.Index.Normalize();
        var allPois = pois
            .Where(static p => !string.IsNullOrWhiteSpace(p.Id))
            .Select(NormalizePoi)
            .ToList();

        var fullPoiRows = 0L;
        if (options.GenerateFullPoisJsonl)
        {
            await using var fullWriter = new StreamWriter(
                Path.Combine(poiRoot, "pois.jsonl"),
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (var poi in allPois.OrderBy(static p => p.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await fullWriter.WriteLineAsync(SerializePoi(poi, options, forIndex: false)).ConfigureAwait(false);
                fullPoiRows++;
            }
        }

        var indexRows = 0L;
        var tileFilesWritten = 0;
        var clippedByZoom = new Dictionary<int, long>();
        if (options.GenerateTileIndex)
        {
            for (var zoom = options.MinZoom; zoom <= options.MaxZoom; zoom++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var byTile = allPois
                    .Where(p => _priorityRules.ShouldIncludeAtZoom(p, zoom))
                    .GroupBy(p => TileMath.LonLatToTile(p.Longitude, p.Latitude, zoom))
                    .OrderBy(static g => g.Key.Z)
                    .ThenBy(static g => g.Key.X)
                    .ThenBy(static g => g.Key.Y);

                foreach (var tileGroup in byTile)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var orderedPois = tileGroup
                        .OrderByDescending(static p => p.Priority)
                        .ThenBy(p => TileMath.SquaredDistanceToTileCenter(p.Latitude, p.Longitude, tileGroup.Key))
                        .ThenBy(static p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static p => p.Id, StringComparer.Ordinal)
                        .ToList();

                    var clipped = orderedPois.Count > options.MaxPoiPerTile;
                    if (clipped)
                    {
                        clippedByZoom[tileGroup.Key.Z] = clippedByZoom.GetValueOrDefault(tileGroup.Key.Z) + 1;
                        orderedPois = orderedPois.Take(options.MaxPoiPerTile).ToList();
                    }

                    if (orderedPois.Count == 0)
                        continue;

                    var tilePath = BuildIndexPath(poiRoot, tileGroup.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
                    await using var tileWriter = new StreamWriter(
                        tilePath,
                        append: false,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    foreach (var poi in orderedPois)
                    {
                        await tileWriter.WriteLineAsync(SerializePoi(poi, options, forIndex: true)).ConfigureAwait(false);
                        indexRows++;
                    }

                    tileFilesWritten++;
                }
            }
        }

        Directory.CreateDirectory(Path.Combine(poiRoot, "icons"));
        var summary = new PoiIndexWriteSummary
        {
            SourcePoiCount = allPois.Count,
            FullPoiRowsWritten = fullPoiRows,
            IndexRowsWritten = indexRows,
            TileFilesWritten = tileFilesWritten,
            WasAnyTileClipped = clippedByZoom.Count > 0,
            ClippedTileCountByZoom = clippedByZoom,
        };

        var manifest = BuildManifest(manifestInput, summary, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
                Path.Combine(poiRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);

        return summary;
    }

    private static string BuildIndexPath(string poiRoot, TileCoordinate tile)
    {
        return Path.Combine(
            poiRoot,
            "index",
            tile.Z.ToString(CultureInfo.InvariantCulture),
            tile.X.ToString(CultureInfo.InvariantCulture),
            $"{tile.Y.ToString(CultureInfo.InvariantCulture)}.jsonl");
    }

    private static string BuildBucketPath(string bucketRoot, TileCoordinate tile)
    {
        return Path.Combine(
            bucketRoot,
            tile.Z.ToString(CultureInfo.InvariantCulture),
            tile.X.ToString(CultureInfo.InvariantCulture),
            $"{tile.Y.ToString(CultureInfo.InvariantCulture)}.jsonl");
    }

    private static PoiRecord NormalizePoi(PoiRecord poi)
    {
        return poi with
        {
            Id = poi.Id.Trim(),
            Type = string.IsNullOrWhiteSpace(poi.Type) ? "generic" : poi.Type.Trim(),
            Name = string.IsNullOrWhiteSpace(poi.Name) ? null : poi.Name.Trim(),
            Source = string.IsNullOrWhiteSpace(poi.Source) ? "osm" : poi.Source.Trim(),
            Tags = new Dictionary<string, string>(poi.Tags ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        };
    }

    private static string SerializeReadablePoi(PoiRecord poi, PoiIndexOptions options, bool forIndex)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = poi.Id,
            ["type"] = poi.Type,
        };

        if (options.IncludeLabels && !string.IsNullOrWhiteSpace(poi.Name))
            values["name"] = poi.Name;

        values["lat"] = Math.Round(poi.Latitude, 7);
        values["lon"] = Math.Round(poi.Longitude, 7);
        values["priority"] = poi.Priority;

        if (!forIndex)
            values["source"] = poi.Source;
        if (options.IncludeOriginalTags && poi.Tags.Count > 0)
            values["tags"] = poi.Tags;

        return JsonSerializer.Serialize(values, JsonLineOptions);
    }

    private static string SerializeCompactPoi(PoiRecord poi, PoiIndexOptions options, bool forIndex)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = poi.Id,
            ["t"] = poi.Type,
        };

        if (options.IncludeLabels && !string.IsNullOrWhiteSpace(poi.Name))
            values["n"] = poi.Name;

        values["lat"] = Math.Round(poi.Latitude, 7);
        values["lon"] = Math.Round(poi.Longitude, 7);
        values["p"] = poi.Priority;

        if (!forIndex)
            values["s"] = poi.Source;
        if (options.IncludeOriginalTags && poi.Tags.Count > 0)
            values["tags"] = poi.Tags;

        return JsonSerializer.Serialize(values, JsonLineOptions);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A later export can clean up stale temp directories.
        }
    }

    private sealed class StreamingPoiIndexBuild : IAsyncDisposable
    {
        private readonly PoiIndexWriter _owner;
        private readonly string _poiRoot;
        private readonly string _bucketRoot;
        private readonly PoiManifestInput _manifestInput;
        private readonly PoiIndexOptions _options;
        private readonly PoiPriorityRules _priorityRules;
        private readonly Dictionary<TileCoordinate, StreamWriter> _bucketWriters = new();
        private StreamWriter? _fullWriter;
        private long _sourcePoiCount;
        private long _fullPoiRows;
        private bool _completed;

        public StreamingPoiIndexBuild(
            PoiIndexWriter owner,
            string poiRoot,
            PoiManifestInput manifestInput,
            PoiPriorityRules priorityRules)
        {
            _owner = owner;
            _poiRoot = poiRoot;
            _bucketRoot = Path.Combine(poiRoot, ".buckets");
            _manifestInput = manifestInput;
            _options = manifestInput.Index.Normalize();
            _priorityRules = priorityRules;

            if (_options.GenerateFullPoisJsonl)
            {
                _fullWriter = new StreamWriter(
                    Path.Combine(_poiRoot, "pois.jsonl"),
                    append: false,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (_options.GenerateTileIndex)
            {
                Directory.CreateDirectory(_bucketRoot);
            }
        }

        public async ValueTask AddAsync(PoiRecord poi, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completed)
                throw new InvalidOperationException("POI index build is already completed.");
            if (string.IsNullOrWhiteSpace(poi.Id))
                return;

            var normalized = NormalizePoi(poi);
            _sourcePoiCount++;

            if (_fullWriter is not null)
            {
                await _fullWriter.WriteLineAsync(_owner.SerializePoi(normalized, _options, forIndex: false))
                    .ConfigureAwait(false);
                _fullPoiRows++;
            }

            if (!_options.GenerateTileIndex)
                return;

            for (var zoom = _options.MinZoom; zoom <= _options.MaxZoom; zoom++)
            {
                if (!_priorityRules.ShouldIncludeAtZoom(normalized, zoom))
                    continue;

                var tile = TileMath.LonLatToTile(normalized.Longitude, normalized.Latitude, zoom);
                var writer = GetBucketWriter(tile);
                var line = JsonSerializer.Serialize(normalized, JsonLineOptions);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
        }

        public async Task<PoiIndexWriteSummary> CompleteAsync(CancellationToken cancellationToken)
        {
            if (_completed)
                throw new InvalidOperationException("POI index build is already completed.");

            _completed = true;
            if (_fullWriter is not null)
            {
                await _fullWriter.DisposeAsync().ConfigureAwait(false);
                _fullWriter = null;
            }

            await DisposeBucketWritersAsync().ConfigureAwait(false);

            var indexRows = 0L;
            var tileFilesWritten = 0;
            var clippedByZoom = new Dictionary<int, long>();

            if (_options.GenerateTileIndex && Directory.Exists(_bucketRoot))
            {
                foreach (var bucketFile in Directory.EnumerateFiles(_bucketRoot, "*.jsonl", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryParseTileFromBucketPath(bucketFile, out var tile))
                        continue;

                    var pois = await ReadBucketPoisAsync(bucketFile, cancellationToken).ConfigureAwait(false);
                    var orderedPois = pois
                        .OrderByDescending(static p => p.Priority)
                        .ThenBy(p => TileMath.SquaredDistanceToTileCenter(p.Latitude, p.Longitude, tile))
                        .ThenBy(static p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static p => p.Id, StringComparer.Ordinal)
                        .ToList();

                    var clipped = orderedPois.Count > _options.MaxPoiPerTile;
                    if (clipped)
                    {
                        clippedByZoom[tile.Z] = clippedByZoom.GetValueOrDefault(tile.Z) + 1;
                        orderedPois = orderedPois.Take(_options.MaxPoiPerTile).ToList();
                    }

                    if (orderedPois.Count == 0)
                        continue;

                    var indexPath = BuildIndexPath(_poiRoot, tile);
                    Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
                    await using var indexWriter = new StreamWriter(
                        indexPath,
                        append: false,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    foreach (var poi in orderedPois)
                    {
                        await indexWriter.WriteLineAsync(_owner.SerializePoi(poi, _options, forIndex: true))
                            .ConfigureAwait(false);
                        indexRows++;
                    }

                    tileFilesWritten++;
                }

                TryDeleteDirectory(_bucketRoot);
            }

            Directory.CreateDirectory(Path.Combine(_poiRoot, "icons"));
            var summary = new PoiIndexWriteSummary
            {
                SourcePoiCount = _sourcePoiCount,
                FullPoiRowsWritten = _fullPoiRows,
                IndexRowsWritten = indexRows,
                TileFilesWritten = tileFilesWritten,
                WasAnyTileClipped = clippedByZoom.Count > 0,
                ClippedTileCountByZoom = clippedByZoom,
            };

            var manifest = _owner.BuildManifest(_manifestInput, summary, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                    Path.Combine(_poiRoot, "manifest.json"),
                    JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            return summary;
        }

        public async ValueTask DisposeAsync()
        {
            if (_fullWriter is not null)
            {
                await _fullWriter.DisposeAsync().ConfigureAwait(false);
                _fullWriter = null;
            }

            await DisposeBucketWritersAsync().ConfigureAwait(false);
        }

        private StreamWriter GetBucketWriter(TileCoordinate tile)
        {
            if (_bucketWriters.TryGetValue(tile, out var existing))
                return existing;

            if (_bucketWriters.Count >= MaxOpenBucketWriters)
            {
                var firstKey = _bucketWriters.Keys.First();
                _bucketWriters[firstKey].Dispose();
                _bucketWriters.Remove(firstKey);
            }

            var path = BuildBucketPath(_bucketRoot, tile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = new StreamWriter(
                path,
                append: true,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _bucketWriters[tile] = writer;
            return writer;
        }

        private async ValueTask DisposeBucketWritersAsync()
        {
            foreach (var writer in _bucketWriters.Values)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            _bucketWriters.Clear();
        }

        private bool TryParseTileFromBucketPath(string path, out TileCoordinate tile)
        {
            tile = default;
            var relative = Path.GetRelativePath(_bucketRoot, path);
            var parts = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
                return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
                return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                return false;

            var yText = Path.GetFileNameWithoutExtension(parts[2]);
            if (!int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                return false;

            tile = new TileCoordinate(z, x, y);
            return true;
        }

        private static async Task<List<PoiRecord>> ReadBucketPoisAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var pois = new List<PoiRecord>();
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var poi = JsonSerializer.Deserialize<PoiRecord>(line, JsonLineOptions);
                if (poi is not null)
                    pois.Add(NormalizePoi(poi));
            }

            return pois;
        }
    }
}
