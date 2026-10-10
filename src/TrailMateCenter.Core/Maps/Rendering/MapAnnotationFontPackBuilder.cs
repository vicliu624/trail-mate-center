using System.Security.Cryptography;
using System.Text;

namespace TrailMateCenter.Maps.Rendering;

public sealed record MapAnnotationFontPack(string Id, string Directory, int GlyphCount, long FileBytes, long EstimatedRamBytes);

/// <summary>Builds the existing firmware content-font package layout in a staging SD root.</summary>
public static class MapAnnotationFontPackBuilder
{
    public static IReadOnlyList<MapAnnotationFontPack> BuildMany(string stagedSdRoot, IEnumerable<int> requiredCodepoints,
        CancellationToken token = default)
    {
        var codes = requiredCodepoints.Where(c => c > 127).Distinct().Order().ToArray();
        if (codes.Length == 0) return [];
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources", "AnnotationFonts");
        var primary = Path.Combine(resources, "NotoSansCJKsc-Regular.otf");
        var license = Path.Combine(resources, "OFL.txt");
        if (!File.Exists(primary) || !File.Exists(license)) throw new FileNotFoundException("Bundled annotation font resources are missing.");
        var sources = new[] { primary }.Concat(Directory.EnumerateFiles(resources)
            .Where(p => p != primary && (p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)).ToArray();
        var sourceIdentity = string.Join(":", sources.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
        var missing = LvglAnnotationFontWriter.MissingGlyphs(sources, codes);
        if (missing.Count != 0)
        {
            Directory.CreateDirectory(stagedSdRoot);
            File.WriteAllText(Path.Combine(stagedSdRoot, "missing-map-glyphs.json"), System.Text.Json.JsonSerializer.Serialize(missing));
            throw new InvalidDataException($"Map font sources lack {missing.Count} characters: " +
                string.Join(",", missing.Take(64).Select(c => $"U+{c:X}")) + "; see missing-map-glyphs.json.");
        }
        var result = new List<MapAnnotationFontPack>();
        // Keep common non-CJK scripts together: a Russian label should not
        // require loading a large Han subset just to render a Cyrillic glyph.
        foreach (var group in codes.GroupBy(c => c is >= 0x3400 and <= 0x9fff or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x3347f ? 1 : 0)
                     .OrderBy(g => g.Key).SelectMany(g => g.Chunk(2048)))
        {
            token.ThrowIfCancellationRequested();
            var subset = group.Append(0x2026).Distinct().Order().ToArray();
            var identity = "map-multiscript-16px-2bpp-v2:" + sourceIdentity + ":" + string.Join(",", subset);
            var id = "map-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 8)).ToLowerInvariant();
            var root = Path.Combine(stagedSdRoot, "trailmate", "packs", "fonts"); Directory.CreateDirectory(root);
            var directory = Path.Combine(root, id);
            var pending = Path.Combine(root, ".build-" + id + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pending);
            try
            {
                LvglAnnotationFontWriter.Write(sources, subset, Path.Combine(pending, "font.bin"), token);
                var size = new FileInfo(Path.Combine(pending, "font.bin")).Length;
                // The emitted format includes 4-byte glyph headers, 4-byte
                // loca offsets and 2-byte sparse cmap entries. LVGL's loader
                // replaces the first two with <=16-byte glyph descriptors;
                // its transient loca array costs another 4 bytes per glyph.
                // File size + 12 bytes/glyph + 4 KiB covers that peak for this
                // uncompressed format, including the LARGE descriptor layout.
                var estimate = size + subset.Length * 12L + 4096;
                if (estimate > 640 * 1024) throw new InvalidDataException("A map font subset exceeds the device content-font budget.");
                File.WriteAllText(Path.Combine(pending, "manifest.ini"),
                    $"kind=font\nid={id}\ndisplay_name=TMAP place and administrative names\nusage=content\nestimated_ram_bytes={estimate}\nsource=binfont\nfile=font.bin\nranges=ranges.txt\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(pending, "ranges.txt"), string.Join(",", subset.Select(c => $"0x{c:X}")), new UTF8Encoding(false));
                File.Copy(license, Path.Combine(pending, "OFL.txt"), true);
                foreach (var extra in Directory.EnumerateFiles(resources).Where(p => p.EndsWith("-OFL.txt", StringComparison.Ordinal) || p.EndsWith("-LICENSE.txt", StringComparison.Ordinal)))
                    File.Copy(extra, Path.Combine(pending, Path.GetFileName(extra)), true);
                File.WriteAllText(Path.Combine(pending, "sources.json"), System.Text.Json.JsonSerializer.Serialize(sources.Select(p => new
                { file = Path.GetFileName(p), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) })));
                token.ThrowIfCancellationRequested();
                using var lease = new FileStream(Path.Combine(root, "." + id + ".lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                if (!Directory.Exists(directory)) Directory.Move(pending, directory);
                else
                {
                    foreach (var path in Directory.EnumerateFiles(pending))
                    {
                        token.ThrowIfCancellationRequested();
                        var existing = Path.Combine(directory, Path.GetFileName(path));
                        if (!File.Exists(existing)) throw new InvalidDataException($"Incomplete existing font resource {id}.");
                        using var old = File.OpenRead(existing); using var current = File.OpenRead(path);
                        if (old.Length != current.Length || !SHA256.HashData(old).AsSpan().SequenceEqual(SHA256.HashData(current)))
                            throw new InvalidDataException($"Existing font resource {id} differs from its content identity.");
                    }
                }
                result.Add(new(id, directory, subset.Length, size, estimate));
            }
            finally
            {
                if (Directory.Exists(pending))
                {
                    foreach (var path in Directory.EnumerateFiles(pending)) File.Delete(path);
                    Directory.Delete(pending);
                }
            }
        }
        return result;
    }

    public static MapAnnotationFontPack? Build(string stagedSdRoot, IEnumerable<int> requiredCodepoints, CancellationToken token = default)
    {
        var codes = requiredCodepoints.Where(c => c > 127).Append(0x2026).Distinct().Order().ToArray();
        if (codes.Length == 1) return null; // Latin names use the firmware's built-in font.
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources", "AnnotationFonts");
        var font = Path.Combine(resources, "NotoSansCJKsc-Regular.otf");
        var license = Path.Combine(resources, "OFL.txt");
        if (!File.Exists(font) || !File.Exists(license)) throw new FileNotFoundException("The bundled annotation font resources are missing. Reinstall Center.");
        var identity = "noto-cjk-sc-16px-2bpp-v1:" + string.Join(",", codes);
        var id = "map-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 8)).ToLowerInvariant();
        var directory = Path.Combine(stagedSdRoot, "trailmate", "packs", "fonts", id);
        System.IO.Directory.CreateDirectory(directory);
        LvglAnnotationFontWriter.Write(font, codes, Path.Combine(directory, "font.bin"), token);
        var size = new FileInfo(Path.Combine(directory, "font.bin")).Length;
        // Conservative cost of decoded glyph descriptors, cmap lists and bitmap.
        var estimate = size + codes.Length * 24L + 4096;
        if (estimate > 640 * 1024) throw new InvalidDataException("Annotation font exceeds the compact device's content-font budget. Reduce the map area or selected annotation classes.");
        File.WriteAllText(Path.Combine(directory, "manifest.ini"),
            $"kind=font\nid={id}\ndisplay_name=Map annotation names\nusage=content\nestimated_ram_bytes={estimate}\nsource=binfont\nfile=font.bin\nranges=ranges.txt\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "ranges.txt"), string.Join(",", codes.Select(c => $"0x{c:X}")), new UTF8Encoding(false));
        File.Copy(license, Path.Combine(directory, "OFL.txt"), true);
        return new(id, directory, codes.Length, size, estimate);
    }
}
