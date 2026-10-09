using System.Text.Json;

namespace TrailMateCenter.Maps.Tmap;

public static class TmapAdministrativeSections
{
    public sealed record Result(string Path, ulong Places, ulong Located, ulong Incomplete, ulong Ambiguous, long AddedBytes);

    public static Result Upgrade(string path, string boundaryManifest, CancellationToken token = default,
        long maximumBytes = long.MaxValue, bool updateIdentity = true)
    {
        path = System.IO.Path.GetFullPath(path);
        var areas = new TmapAdministrativeAreas(boundaryManifest, token);
        var work = path + ".admin-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(work);
        var references = System.IO.Path.Combine(work, "references");
        var stringsPath = System.IO.Path.Combine(work, "strings");
        var provenance = System.IO.Path.Combine(work, "provenance.json");
        var oldBytes = new FileInfo(path).Length;
        ulong places = 0, located = 0, incomplete = 0, ambiguous = 0, paths = 0;
        try
        {
            using (var reader = new TmapReader(path))
            using (var rows = new TmapTable(references, 9, 16))
            using (var strings = new TmapTable(stringsPath, 10))
            {
                // Only distinct administrative paths are retained in memory.
                // Row references are streamed in original POI row order.
                var dictionary = new Dictionary<string, ulong>(StringComparer.Ordinal);
                for (ulong row = 1; row <= reader.PoiCount; row++)
                {
                    token.ThrowIfCancellationRequested();
                    var poi = reader.ReadPoi(row); var location = areas.Resolve(poi.Latitude, poi.Longitude);
                    var record = new byte[16];
                    if (location.Path.Length != 0)
                    {
                        if (!dictionary.TryGetValue(location.Path, out var reference))
                        { reference = strings.String(location.Path); dictionary.Add(location.Path, reference); paths++; }
                        TmapFormat.Put64(record, 0, reference); located++;
                    }
                    record[8] = location.Levels; record[9] = location.Flags;
                    var country = TmapFormat.Utf8.GetBytes(location.CountryCode);
                    if (country.Length > 3) throw new InvalidDataException("Administrative country code must fit three bytes.");
                    country.CopyTo(record, 10); rows.Add(record); places++;
                    if ((location.Flags & 1) != 0) incomplete++;
                    if ((location.Flags & 2) != 0) ambiguous++;
                    if (row % 4096 == 0) Console.WriteLine($"tmap-admin places={row}/{reader.PoiCount} located={located} incomplete={incomplete} ambiguous={ambiguous}");
                }
            }
            File.WriteAllText(provenance, JsonSerializer.Serialize(new
            {
                schema = 1, method = "WGS84 polygon containment; no nearest-city substitution",
                manifest = JsonSerializer.Deserialize<JsonElement>(areas.ManifestJson), places, located, incomplete, ambiguous
            }));
            TmapExtensionPublisher.Publish(path,
                [new(50, references, places), new(51, stringsPath, paths), new(54, provenance, 1, false)], token, maximumBytes, updateIdentity);
            return new(path, places, located, incomplete, ambiguous, new FileInfo(path).Length - oldBytes);
        }
        finally
        {
            File.Delete(references); File.Delete(stringsPath); File.Delete(provenance); Directory.Delete(work);
        }
    }
}
