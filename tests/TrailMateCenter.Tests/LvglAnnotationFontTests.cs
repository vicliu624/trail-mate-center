using System.Text;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class LvglAnnotationFontTests
{
    [Fact]
    public void SubsetWritesBoundedUncompressedTablesAndRejectsInvalidUnicode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        var output = Path.Combine(directory, "font.bin");
        try
        {
            LvglAnnotationFontWriter.Write(source, "人民路昆明…".EnumerateRunes().Select(r => r.Value), output);
            using var reader = new BinaryReader(File.OpenRead(output));
            foreach (var tag in new[] { "head", "cmap", "loca", "glyf" })
            {
                var start = reader.BaseStream.Position;
                var size = reader.ReadUInt32();
                Assert.Equal(tag, Encoding.ASCII.GetString(reader.ReadBytes(4)));
                Assert.InRange(size, 8U, 65536U);
                reader.BaseStream.Position = start + size;
            }
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
            Assert.Throws<ArgumentException>(() => LvglAnnotationFontWriter.Write(source, [0x110000], output));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
