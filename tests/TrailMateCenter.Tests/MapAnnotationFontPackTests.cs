using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationFontPackTests
{
    [Fact]
    public void PackageIncludesGlyphsLicenseAndExistingFirmwareManifestWithoutExternalTools()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var codes = "人民路昆明".Select(c => (int)c).ToArray();
            var first = MapAnnotationFontPackBuilder.Build(root, codes)!;
            Assert.Equal(6, first.GlyphCount);
            Assert.True(first.FileBytes > 0 && first.EstimatedRamBytes > first.FileBytes);
            Assert.Contains("usage=content", File.ReadAllText(Path.Combine(first.Directory, "manifest.ini")));
            Assert.Contains("0x2026", File.ReadAllText(Path.Combine(first.Directory, "ranges.txt")));
            Assert.Contains("OPEN FONT LICENSE", File.ReadAllText(Path.Combine(first.Directory, "OFL.txt")));
            var second = MapAnnotationFontPackBuilder.Build(root, codes.Reverse())!;
            Assert.Equal(first.Id, second.Id);
            Assert.Null(MapAnnotationFontPackBuilder.Build(root, "Street".Select(c => (int)c)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
