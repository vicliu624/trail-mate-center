using System.Security.Cryptography;
using System.Text.Json;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Maps.Tmap;

public static class TmapFontSections
{
    public sealed record Result(string Path, int RequiredCharacters, IReadOnlyList<MapAnnotationFontPack> Fonts);

    public static Result Upgrade(string path, string sdRoot, CancellationToken token = default,
        long maximumBytes = long.MaxValue, bool updateIdentity = true)
    {
        path = System.IO.Path.GetFullPath(path); sdRoot = System.IO.Path.GetFullPath(sdRoot);
        var codes = new HashSet<int>();
        void Collect(string text) { foreach (var rune in text.EnumerateRunes()) if (rune.Value > 127) codes.Add(rune.Value); }
        using (var reader = new TmapReader(path))
        {
            foreach (var name in reader.SearchableNames(token)) Collect(name);
            if (reader.HasAdministrativeAreas)
                for (ulong row = 1; row <= reader.PoiCount; row++)
                { token.ThrowIfCancellationRequested(); Collect(reader.ReadAdministrativeLocation(row).Path); }
        }
        var fonts = MapAnnotationFontPackBuilder.BuildMany(sdRoot, codes, token);
        var work = path + ".fonts-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(work);
        var records = System.IO.Path.Combine(work, "records"); var stringsPath = System.IO.Path.Combine(work, "strings");
        try
        {
            using (var rows = new TmapTable(records, 9, 64))
            using (var strings = new TmapTable(stringsPath, 10))
                foreach (var font in fonts)
                {
                    var record = new byte[64]; TmapFormat.Put64(record, 0, strings.String(font.Id));
                    using var input = File.OpenRead(System.IO.Path.Combine(font.Directory, "font.bin"));
                    SHA256.HashData(input).CopyTo(record, 8);
                    TmapFormat.Put32(record, 40, (uint)font.GlyphCount); TmapFormat.Put32(record, 44, checked((uint)font.EstimatedRamBytes));
                    TmapFormat.Put64(record, 48, (ulong)font.FileBytes); TmapFormat.Put16(record, 56, 16); record[58] = 2;
                    rows.Add(record);
                }
            TmapExtensionPublisher.Publish(path, [new(52, records, (ulong)fonts.Count), new(53, stringsPath, (ulong)fonts.Count)],
                token, maximumBytes, updateIdentity);
            File.WriteAllText(path + ".font-resources.json", JsonSerializer.Serialize(new
            {
                schema = 1, required_characters = codes.Count, install_sd_root = "trailmate/packs/fonts",
                fonts = fonts.Select(f => new { id = f.Id, glyph_count = f.GlyphCount, bytes = f.FileBytes, estimated_ram_bytes = f.EstimatedRamBytes })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return new(path, codes.Count, fonts);
        }
        finally { File.Delete(records); File.Delete(stringsPath); Directory.Delete(work); }
    }
}
