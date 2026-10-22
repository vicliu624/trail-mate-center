using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class RasterAnnotationFontFallbackTests
{
    [Fact]
    public void RasterMapFont_UsesBundledFallbackForExtendedLatinAndOtherScripts()
    {
        var root = Path.Combine(Path.GetTempPath(), "Center.RasterFontFallback", Guid.NewGuid().ToString("N"));
        try
        {
            var codes = "śЖع中…".EnumerateRunes().Select(rune => rune.Value).ToArray();
            var result = MapAnnotationFontPackBuilder.Build(root, codes)!;
            var ranges = File.ReadAllText(Path.Combine(result.Directory, "ranges.txt"));
            Assert.Contains("0x15B", ranges);
            Assert.Contains("0x416", ranges);
            Assert.Contains("0x639", ranges);
            Assert.Equal(codes.Length, result.GlyphCount);
            Assert.True(result.EstimatedRamBytes < 640 * 1024);
            Assert.True(File.Exists(Path.Combine(result.Directory, "sources.json")));
            Assert.True(File.Exists(Path.Combine(result.Directory, "NotoSans-OFL.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
