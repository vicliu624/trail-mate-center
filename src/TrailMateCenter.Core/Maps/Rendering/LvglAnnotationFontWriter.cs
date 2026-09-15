using System.Text;
using SkiaSharp;

namespace TrailMateCenter.Maps.Rendering;

/// <summary>Uncompressed 2-bpp LVGL binfont subset, written without an external runtime.</summary>
public static class LvglAnnotationFontWriter
{
    public static void Write(string fontSource, IEnumerable<int> codepoints, string output, CancellationToken token = default)
    {
        var codes = codepoints.Distinct().Order().ToArray();
        if (codes.Length == 0 || codes.Length > 60000 || codes.Any(c => !Rune.IsValid(c)))
            throw new ArgumentException("A nonempty valid Unicode subset is required.");
        using var typeface = SKTypeface.FromFile(fontSource) ?? throw new InvalidDataException("Cannot load annotation font.");
        using var font = new SKFont(typeface, 16) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var glyphs = new List<byte[]> { new byte[4] };
        var ascent = 0;
        var descent = 0;
        foreach (var code in codes)
        {
            token.ThrowIfCancellationRequested();
            var text = new Rune(code).ToString();
            if (font.GetGlyphs(text).Any(g => g == 0)) throw new InvalidDataException($"Annotation font lacks U+{code:X}.");
            var bounds = new SKRect();
            var advance = (int)Math.Round(font.MeasureText(text, out bounds));
            var left = (int)Math.Floor(bounds.Left); var top = (int)Math.Floor(bounds.Top);
            var width = Math.Max(0, (int)Math.Ceiling(bounds.Right) - left);
            var height = Math.Max(0, (int)Math.Ceiling(bounds.Bottom) - top);
            var bottomOffset = -top - height;
            if (left is < -32 or > 31 || bottomOffset is < -32 or > 31 || width > 63 || height > 63 || advance is < 0 or > 255)
                throw new InvalidDataException($"Glyph U+{code:X} exceeds the bounded font format.");
            ascent = Math.Max(ascent, -top); descent = Math.Max(descent, top + height);
            var bytes = new byte[4 + (width * height + 3) / 4];
            var header = ((uint)advance << 24) | ((uint)(left & 63) << 18) | ((uint)(bottomOffset & 63) << 12) | ((uint)width << 6) | (uint)height;
            for (var i = 0; i < 4; i++) bytes[i] = (byte)(header >> (24 - 8 * i));
            if (width > 0 && height > 0)
            {
                using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawText(text, -left, -top, SKTextAlign.Left, font, paint);
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;
                        var alpha = (bitmap.GetPixel(x, y).Alpha * 3 + 127) / 255;
                        bytes[4 + index / 4] |= (byte)(alpha << (6 - (index % 4) * 2));
                    }
            }
            glyphs.Add(bytes);
        }
        using var head = new MemoryStream();
        using (var writer = new BinaryWriter(head, Encoding.ASCII, true))
        {
            writer.Write(1U); writer.Write((ushort)3); writer.Write((ushort)16);
            writer.Write((ushort)Math.Max(1, ascent)); writer.Write((short)-descent);
            writer.Write((ushort)Math.Max(1, ascent)); writer.Write((short)-descent);
            writer.Write((ushort)0); writer.Write((short)-descent); writer.Write((short)ascent);
            writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(new byte[] { 1, 0, 0, 2, 6, 6, 8, 0, 0, 0 });
            writer.Write((short)-1); writer.Write((ushort)1);
        }
        // One sparse map per <=65535-codepoint interval, including supplementary planes.
        var groups = new List<int[]>();
        for (var i = 0; i < codes.Length;)
        {
            var start = i++;
            while (i < codes.Length && codes[i] - codes[start] < 65535) i++;
            groups.Add(codes[start..i]);
        }
        using var cmap = new MemoryStream();
        using (var writer = new BinaryWriter(cmap, Encoding.ASCII, true))
        {
            writer.Write((uint)groups.Count);
            var offset = 12 + groups.Count * 16;
            ushort glyph = 1;
            foreach (var group in groups)
            {
                writer.Write((uint)offset); writer.Write((uint)group[0]);
                writer.Write((ushort)(group[^1] - group[0] + 1)); writer.Write(glyph);
                writer.Write((ushort)group.Length); writer.Write((byte)3); writer.Write((byte)0);
                offset += group.Length * 2; glyph += (ushort)group.Length;
            }
            foreach (var group in groups) foreach (var code in group) writer.Write((ushort)(code - group[0]));
        }
        using var loca = new MemoryStream();
        using (var writer = new BinaryWriter(loca, Encoding.ASCII, true))
        {
            writer.Write((uint)glyphs.Count);
            uint offset = 8;
            foreach (var glyph in glyphs) { writer.Write(offset); offset += (uint)glyph.Length; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var file = File.Create(output);
        using var final = new BinaryWriter(file);
        void Table(string tag, byte[] data) { final.Write((uint)(data.Length + 8)); final.Write(Encoding.ASCII.GetBytes(tag)); final.Write(data); }
        Table("head", head.ToArray()); Table("cmap", cmap.ToArray()); Table("loca", loca.ToArray());
        Table("glyf", glyphs.SelectMany(g => g).ToArray());
    }
}
