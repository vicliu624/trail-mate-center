using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace TrailMateCenter.Maps.Tmap;

public sealed record TmapAdministrativeSource(string Path, int Level, string CountryCode, string CountryName,
    string Source, string License, string Sha256, string NameProperty = "shapeName", string? EnglishNameProperty = null);
public sealed record TmapAdministrativeManifest(int Version, IReadOnlyList<TmapAdministrativeSource> Sources);
public sealed record TmapAdministrativeLocation(string Path, byte Levels, byte Flags, string CountryCode);

/// <summary>Build-time boundary containment; never used on a device.</summary>
public sealed class TmapAdministrativeAreas
{
    private sealed record Area(int Level, string Country, string CountryName, string Name,
        Geometry Geometry, IPreparedGeometry Prepared);
    private readonly STRtree<Area>[] _trees = [new(), new(), new(), new(), new(), new()];
    private readonly GeometryFactory _factory = new(new PrecisionModel(), 4326);
    public string ManifestJson { get; }

    public TmapAdministrativeAreas(string manifestPath, CancellationToken token = default)
    {
        manifestPath = System.IO.Path.GetFullPath(manifestPath);
        ManifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<TmapAdministrativeManifest>(ManifestJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing administrative manifest.");
        if (manifest.Version != 1 || manifest.Sources.Count == 0) throw new InvalidDataException("Unsupported or empty administrative manifest.");
        var root = System.IO.Path.GetDirectoryName(manifestPath)!;
        foreach (var source in manifest.Sources)
        {
            token.ThrowIfCancellationRequested();
            if (source.Level is < 0 or > 5 || string.IsNullOrWhiteSpace(source.CountryCode) ||
                string.IsNullOrWhiteSpace(source.Source) || string.IsNullOrWhiteSpace(source.License))
                throw new InvalidDataException("Administrative sources need level, country, provenance and license.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, source.Path));
            if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Administrative source must be inside the dataset directory.");
            using (var input = File.OpenRead(path))
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Administrative source hash mismatch: {source.Path}");
            using var stream = File.OpenRead(path);
            if (stream.Length > 256L * 1024 * 1024) throw new InvalidDataException("Split administrative GeoJSON files larger than 256 MiB.");
            using var document = JsonDocument.Parse(stream);
            foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var properties = feature.GetProperty("properties");
                var name = source.Level == 0 ? source.CountryName : Text(properties, source.EnglishNameProperty) ?? Text(properties, source.NameProperty);
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("An administrative polygon lacks a name.");
                var geometry = ReadGeometry(feature.GetProperty("geometry"));
                if (geometry.IsEmpty || !geometry.IsValid) throw new InvalidDataException($"Invalid administrative polygon: {name}");
                var area = new Area(source.Level, source.CountryCode, source.CountryName, name, geometry, PreparedGeometryFactory.Prepare(geometry));
                _trees[source.Level].Insert(geometry.EnvelopeInternal, area);
            }
        }
        foreach (var tree in _trees) tree.Build();
    }

    public TmapAdministrativeLocation Resolve(double latitude, double longitude)
    {
        var point = _factory.CreatePoint(new Coordinate(longitude, latitude));
        var names = new List<string>(6);
        string country = "";
        byte levels = 0, flags = 0;
        for (var level = 0; level < _trees.Length; level++)
        {
            var candidates = _trees[level].Query(point.EnvelopeInternal)
                .Where(a => (country.Length == 0 || a.Country == country) && a.Prepared.Covers(point)).ToArray();
            // Overlapping boundary sources are uncertain, not a licence to
            // guess the closest place or silently pick the smallest polygon.
            if (candidates.Length > 1) { flags |= 2; break; }
            if (candidates.Length == 0) { if (level <= 2) flags |= 1; continue; }
            var area = candidates[0];
            if (country.Length == 0)
            {
                country = area.Country;
                if (level != 0) { names.Add(area.CountryName); levels |= 1; flags |= 1; }
            }
            if (names.Count == 0 || names[^1] != area.Name) names.Add(area.Name);
            levels |= (byte)(1 << level);
        }
        var path = string.Join(" › ", names);
        if (TmapFormat.Utf8.GetByteCount(path) > 512) throw new InvalidDataException("Administrative path exceeds 512 UTF-8 bytes.");
        return new(path, levels, flags, country);
    }

    private static string? Text(JsonElement properties, string? key) => key is not null && properties.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private Geometry ReadGeometry(JsonElement value)
    {
        var coordinates = value.GetProperty("coordinates");
        return value.GetProperty("type").GetString() switch
        {
            "Polygon" => Polygon(coordinates),
            "MultiPolygon" => _factory.CreateMultiPolygon(coordinates.EnumerateArray().Select(Polygon).ToArray()),
            _ => throw new InvalidDataException("Administrative GeoJSON must contain polygons.")
        };
    }
    private Polygon Polygon(JsonElement value)
    {
        var rings = value.EnumerateArray().Select(r => _factory.CreateLinearRing(r.EnumerateArray().Select(p =>
        {
            var longitude = p[0].GetDouble(); var latitude = p[1].GetDouble();
            if (!double.IsFinite(longitude) || !double.IsFinite(latitude) || longitude is < -180 or > 180 || latitude is < -90 or > 90)
                throw new InvalidDataException("Administrative geometry must use WGS84 coordinates.");
            return new Coordinate(longitude, latitude);
        }).ToArray())).ToArray();
        if (rings.Length == 0) throw new InvalidDataException("Missing polygon ring.");
        return _factory.CreatePolygon(rings[0], rings.Skip(1).ToArray());
    }
}
