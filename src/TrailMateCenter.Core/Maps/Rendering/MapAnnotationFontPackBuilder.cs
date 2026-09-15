using System.Security.Cryptography;
using System.Text;

namespace TrailMateCenter.Maps.Rendering;

public sealed record MapAnnotationFontPack(string Id, string Directory, int GlyphCount, long FileBytes, long EstimatedRamBytes);

/// <summary>Builds the existing firmware content-font package layout in a staging SD root.</summary>
public static class MapAnnotationFontPackBuilder
{
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
