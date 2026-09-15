using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TrailMateCenter.Places;

public sealed class PlaceSearchPackWriter
{
    private const string ManifestFileName = "manifest.json";
    private const string LicensesFileName = "licenses.json";
    private const string PlacesDirectoryName = "places";
    private const string PacksDirectoryName = "packs";
    private const string PlacesBinaryFileName = "places.bin";
    private const string NamesBinaryFileName = "names.bin";
    private const string PlacesBinaryMagic = "TMPLREC1";
    private const string NamesBinaryMagic = "TMPLNAM1";
    private const int BinaryVersion = 1;
    private const int MaxStringBytes = 512;
    private const long BinaryHeaderCountOffset = 12;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] CategoryTable =
    [
        PlaceCategories.Generic,
        PlaceCategories.Admin,
        PlaceCategories.Emergency,
        PlaceCategories.Food,
        PlaceCategories.Landmark,
        PlaceCategories.Lodging,
        PlaceCategories.Medical,
        PlaceCategories.Natural,
        PlaceCategories.Outdoor,
        PlaceCategories.Settlement,
        PlaceCategories.Shop,
        PlaceCategories.Transport,
        PlaceCategories.Water,
    ];

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public async Task<PlaceSearchPackWriteSummary> WriteStreamingAsync(
        string outputRoot,
        PlaceSearchPackManifestInput manifestInput,
        Func<Func<PlaceRecord, ValueTask>, Task> populatePlacesAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        if (manifestInput is null)
            throw new ArgumentNullException(nameof(manifestInput));
        if (populatePlacesAsync is null)
            throw new ArgumentNullException(nameof(populatePlacesAsync));

        var packId = BuildPackId(manifestInput.PackId, manifestInput.Area.Name);
        var placesRoot = Path.Combine(outputRoot, PlacesDirectoryName);
        var packsRoot = Path.Combine(placesRoot, PacksDirectoryName);
        var finalRoot = Path.Combine(packsRoot, packId);
        var tempRoot = Path.Combine(packsRoot, $".{packId}-{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(packsRoot);
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var builder = new StreamingPlaceSearchPackBuild(
                this,
                tempRoot,
                manifestInput with { PackId = packId });

            await populatePlacesAsync(place => builder.AddAsync(place, cancellationToken)).ConfigureAwait(false);
            var summary = await builder.CompleteAsync(cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(finalRoot))
                Directory.Delete(finalRoot, recursive: true);
            Directory.Move(tempRoot, finalRoot);

            await WriteCatalogAsync(placesRoot, cancellationToken).ConfigureAwait(false);
            return summary;
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    public PlaceSearchPackManifest BuildManifest(
        PlaceSearchPackManifestInput input,
        PlaceSearchPackWriteSummary summary,
        DateTimeOffset createdAt)
    {
        var bounds = input.Area.Bounds.Normalize();
        return new PlaceSearchPackManifest
        {
            PackId = summary.PackId,
            Files = new PlaceSearchPackManifestFiles
            {
                Places = PlacesBinaryFileName,
                Names = NamesBinaryFileName,
            },
            Binary = new PlaceSearchPackManifestBinary
            {
                MaxStringBytes = MaxStringBytes,
            },
            Categories = BuildCategoryManifest(),
            Source = new PlaceSearchPackManifestSource
            {
                Type = input.Source.Type,
                Name = input.Source.Name,
                Provider = input.Source.Provider,
                DownloadUrl = input.Source.DownloadUrl,
                License = input.Source.License,
            },
            Area = new PlaceSearchPackManifestArea
            {
                Name = input.Area.Name,
                AdminLevel = input.Area.AdminLevel,
                Bounds = new PlaceSearchPackManifestBounds
                {
                    West = bounds.West,
                    South = bounds.South,
                    East = bounds.East,
                    North = bounds.North,
                },
            },
            Extraction = new PlaceSearchPackManifestExtraction
            {
                NameLanguage = string.IsNullOrWhiteSpace(input.NameLanguage) ? "default" : input.NameLanguage.Trim(),
            },
            Records = new PlaceSearchPackManifestRecords
            {
                PlaceCount = summary.PlaceCount,
                NameRows = summary.NameRowsWritten,
                CategoryCounts = summary.CategoryCounts,
            },
            CreatedAt = createdAt,
        };
    }

    public static string BuildPackId(string? preferredPackId, string? areaName)
    {
        var hashSource = !string.IsNullOrWhiteSpace(preferredPackId)
            ? preferredPackId.Trim()
            : !string.IsNullOrWhiteSpace(areaName) ? areaName.Trim() : "selection";
        var slugSource = !string.IsNullOrWhiteSpace(areaName) ? areaName.Trim() : hashSource;
        var slug = BuildAsciiSlug(slugSource);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "pack";

        if (slug.Length > 48)
            slug = slug[..48].Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "pack";

        return $"{slug}-{ComputeShortHexHash(hashSource)}";
    }

    private async Task WriteCatalogAsync(string placesRoot, CancellationToken cancellationToken)
    {
        var packsRoot = Path.Combine(placesRoot, PacksDirectoryName);
        var packs = new List<PlaceSearchCatalogPack>();

        if (Directory.Exists(packsRoot))
        {
            foreach (var packRoot in Directory.EnumerateDirectories(packsRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directoryName = Path.GetFileName(packRoot);
                if (string.IsNullOrWhiteSpace(directoryName) || directoryName.StartsWith(".", StringComparison.Ordinal))
                    continue;

                var manifestPath = Path.Combine(packRoot, ManifestFileName);
                var placesPath = Path.Combine(packRoot, PlacesBinaryFileName);
                var namesPath = Path.Combine(packRoot, NamesBinaryFileName);
                var licensesPath = Path.Combine(packRoot, LicensesFileName);
                if (!File.Exists(manifestPath) ||
                    !File.Exists(placesPath) ||
                    !File.Exists(namesPath) ||
                    !File.Exists(licensesPath))
                {
                    continue;
                }

                PlaceSearchPackManifest? manifest;
                await using (var stream = File.OpenRead(manifestPath))
                {
                    manifest = await JsonSerializer.DeserializeAsync<PlaceSearchPackManifest>(
                            stream,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.PackId))
                    continue;

                packs.Add(new PlaceSearchCatalogPack
                {
                    PackId = manifest.PackId,
                    Name = manifest.Area.Name,
                    AdminLevel = manifest.Area.AdminLevel,
                    Path = $"{PacksDirectoryName}/{directoryName}",
                    Bounds = manifest.Area.Bounds,
                    Source = manifest.Source,
                    Records = manifest.Records,
                    Files = manifest.Files,
                });
            }
        }

        var catalog = new PlaceSearchCatalog
        {
            Packs = packs
                .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static p => p.AdminLevel)
                .ThenBy(static p => p.PackId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var catalogPath = Path.Combine(placesRoot, "catalog.json");
        var tempCatalogPath = $"{catalogPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
                tempCatalogPath,
                JsonSerializer.Serialize(catalog, ManifestJsonOptions),
                Utf8NoBom,
                cancellationToken)
            .ConfigureAwait(false);
        File.Move(tempCatalogPath, catalogPath, overwrite: true);
    }

    private static IReadOnlyList<PlaceSearchPackCategory> BuildCategoryManifest()
    {
        return CategoryTable
            .Select(static (category, index) => new PlaceSearchPackCategory
            {
                Id = index,
                Name = category,
            })
            .ToArray();
    }

    private static PlaceRecord NormalizePlace(PlaceRecord place)
    {
        var inputNames = place.Names ?? Array.Empty<string>();
        var names = inputNames
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Select(static n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primaryName = string.IsNullOrWhiteSpace(place.PrimaryName)
            ? names.FirstOrDefault() ?? string.Empty
            : place.PrimaryName.Trim();
        if (!string.IsNullOrWhiteSpace(primaryName) &&
            !names.Contains(primaryName, StringComparer.OrdinalIgnoreCase))
        {
            names.Insert(0, primaryName);
        }

        return place with
        {
            Id = place.Id.Trim(),
            Category = NormalizeCategory(place.Category),
            PrimaryName = primaryName,
            Names = names,
            Source = string.IsNullOrWhiteSpace(place.Source) ? "osm" : place.Source.Trim(),
            OsmType = string.IsNullOrWhiteSpace(place.OsmType) ? string.Empty : place.OsmType.Trim(),
            Tags = place.Tags is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(place.Tags, StringComparer.Ordinal),
        };
    }

    private static IReadOnlyList<NameRow> BuildNameRows(PlaceRecord place)
    {
        var rows = new List<NameRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in place.Names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var displayName = name.Trim();
            var normalized = PlaceNameNormalizer.NormalizeForSearch(displayName);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                continue;

            rows.Add(new NameRow(displayName, normalized));
        }

        return rows;
    }

    private static string NormalizeCategory(string? category)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            foreach (var known in CategoryTable)
            {
                if (string.Equals(known, category.Trim(), StringComparison.OrdinalIgnoreCase))
                    return known;
            }
        }

        return PlaceCategories.Generic;
    }

    private static ushort CategoryIdFor(string category)
    {
        for (var i = 0; i < CategoryTable.Length; i++)
        {
            if (string.Equals(CategoryTable[i], category, StringComparison.OrdinalIgnoreCase))
                return (ushort)i;
        }

        return 0;
    }

    private static ulong ComputePlaceKeyHash(PlaceRecord place)
    {
        var source = $"{place.Source}|{place.OsmType}|{place.OsmId?.ToString() ?? string.Empty}|{place.Id}";
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(source))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private static void WriteHeader(BinaryWriter writer, string magic)
    {
        var magicBytes = Encoding.ASCII.GetBytes(magic);
        if (magicBytes.Length != 8)
            throw new InvalidOperationException("Place search binary magic must be 8 bytes.");

        writer.Write(magicBytes);
        writer.Write(BinaryVersion);
        writer.Write(0UL);
    }

    private static void WriteRecordCount(FileStream stream, ulong count)
    {
        stream.Position = BinaryHeaderCountOffset;
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(count);
        writer.Flush();
        stream.Position = stream.Length;
    }

    private static void WritePlaceRecord(BinaryWriter writer, PlaceRecord place, ulong keyHash)
    {
        writer.Write(keyHash);
        writer.Write(ToE7(place.Latitude));
        writer.Write(ToE7(place.Longitude));
        writer.Write(ClampToUInt16(place.Rank));
        writer.Write(CategoryIdFor(place.Category));
        writer.Write(OsmTypeIdFor(place.OsmType));
        writer.Write(place.OsmId ?? -1L);
        WriteBoundedUtf8String(writer, place.Id);
        WriteBoundedUtf8String(writer, place.PrimaryName);
    }

    private static void WriteNameRecord(
        BinaryWriter writer,
        PlaceRecord place,
        NameRow name,
        ulong placeOffset,
        ulong keyHash)
    {
        writer.Write(placeOffset);
        writer.Write(keyHash);
        writer.Write(ToE7(place.Latitude));
        writer.Write(ToE7(place.Longitude));
        writer.Write(ClampToUInt16(place.Rank));
        writer.Write(CategoryIdFor(place.Category));
        WriteBoundedUtf8String(writer, name.DisplayName);
        WriteBoundedUtf8String(writer, name.Normalized);
    }

    private static int ToE7(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        var scaled = Math.Round(value * 10_000_000d, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }

    private static ushort ClampToUInt16(int value)
    {
        return (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }

    private static byte OsmTypeIdFor(string? osmType)
    {
        return osmType?.Trim().ToLowerInvariant() switch
        {
            "node" => 1,
            "way" => 2,
            "relation" => 3,
            _ => 0,
        };
    }

    private static void WriteBoundedUtf8String(BinaryWriter writer, string? value)
    {
        var text = value ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxStringBytes)
        {
            text = TruncateUtf8(text, MaxStringBytes);
            bytes = Encoding.UTF8.GetBytes(text);
        }

        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        var end = value.Length;
        while (end > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, end)) > maxBytes)
            end--;

        if (end > 0 && char.IsHighSurrogate(value[end - 1]))
            end--;

        return end <= 0 ? string.Empty : value[..end];
    }

    private static string BuildAsciiSlug(string source)
    {
        var builder = new StringBuilder(source.Length);
        var lastWasDash = false;

        foreach (var ch in source.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ComputeShortHexHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(12);
        for (var i = 0; i < 6; i++)
            builder.Append(bytes[i].ToString("x2"));
        return builder.ToString();
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

    private sealed record NameRow(string DisplayName, string Normalized);

    private sealed class StreamingPlaceSearchPackBuild : IAsyncDisposable
    {
        private readonly PlaceSearchPackWriter _owner;
        private readonly string _placeRoot;
        private readonly PlaceSearchPackManifestInput _manifestInput;
        private readonly FileStream _placesStream;
        private readonly FileStream _namesStream;
        private readonly BinaryWriter _placesWriter;
        private readonly BinaryWriter _namesWriter;
        private readonly Dictionary<string, long> _categoryCounts = new(StringComparer.OrdinalIgnoreCase);
        private long _placeCount;
        private long _nameRows;
        private bool _completed;
        private bool _disposed;

        public StreamingPlaceSearchPackBuild(
            PlaceSearchPackWriter owner,
            string placeRoot,
            PlaceSearchPackManifestInput manifestInput)
        {
            _owner = owner;
            _placeRoot = placeRoot;
            _manifestInput = manifestInput;
            Directory.CreateDirectory(_placeRoot);

            _placesStream = new FileStream(
                Path.Combine(_placeRoot, PlacesBinaryFileName),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            _namesStream = new FileStream(
                Path.Combine(_placeRoot, NamesBinaryFileName),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            _placesWriter = new BinaryWriter(_placesStream, Encoding.UTF8, leaveOpen: true);
            _namesWriter = new BinaryWriter(_namesStream, Encoding.UTF8, leaveOpen: true);

            WriteHeader(_placesWriter, PlacesBinaryMagic);
            WriteHeader(_namesWriter, NamesBinaryMagic);
        }

        public ValueTask AddAsync(PlaceRecord place, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completed)
                throw new InvalidOperationException("Place search pack build is already completed.");
            if (string.IsNullOrWhiteSpace(place.Id))
                return ValueTask.CompletedTask;

            var normalized = NormalizePlace(place);
            if (string.IsNullOrWhiteSpace(normalized.PrimaryName) || normalized.Names.Count == 0)
                return ValueTask.CompletedTask;

            var nameRows = BuildNameRows(normalized);
            if (nameRows.Count == 0)
                return ValueTask.CompletedTask;

            var keyHash = ComputePlaceKeyHash(normalized);
            var placeOffset = (ulong)_placesStream.Position;
            WritePlaceRecord(_placesWriter, normalized, keyHash);
            _placeCount++;
            _categoryCounts[normalized.Category] = _categoryCounts.GetValueOrDefault(normalized.Category) + 1;

            foreach (var nameRow in nameRows)
            {
                WriteNameRecord(_namesWriter, normalized, nameRow, placeOffset, keyHash);
                _nameRows++;
            }

            return ValueTask.CompletedTask;
        }

        public async Task<PlaceSearchPackWriteSummary> CompleteAsync(CancellationToken cancellationToken)
        {
            if (_completed)
                throw new InvalidOperationException("Place search pack build is already completed.");

            _completed = true;
            CloseWriters();

            var summary = new PlaceSearchPackWriteSummary
            {
                PackId = _manifestInput.PackId,
                PlaceCount = _placeCount,
                NameRowsWritten = _nameRows,
                CategoryCounts = _categoryCounts
                    .OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static p => p.Key, static p => p.Value, StringComparer.OrdinalIgnoreCase),
            };

            var manifest = _owner.BuildManifest(_manifestInput, summary, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                    Path.Combine(_placeRoot, ManifestFileName),
                    JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                    Utf8NoBom,
                    cancellationToken)
                .ConfigureAwait(false);

            await File.WriteAllTextAsync(
                    Path.Combine(_placeRoot, LicensesFileName),
                    JsonSerializer.Serialize(new PlaceSearchPackLicenseFile(), ManifestJsonOptions),
                    Utf8NoBom,
                    cancellationToken)
                .ConfigureAwait(false);

            return summary;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
                CloseWriters();

            return ValueTask.CompletedTask;
        }

        private void CloseWriters()
        {
            if (_disposed)
                return;

            _placesWriter.Flush();
            _namesWriter.Flush();
            WriteRecordCount(_placesStream, (ulong)_placeCount);
            WriteRecordCount(_namesStream, (ulong)_nameRows);
            _placesWriter.Dispose();
            _namesWriter.Dispose();
            _placesStream.Dispose();
            _namesStream.Dispose();
            _disposed = true;
        }
    }
}
