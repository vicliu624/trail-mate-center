using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Tests;

public sealed class TmapOutdoorPreviewTests
{
    [Fact]
    public void ReadsActualPagedRoadAndContourVectorsIncludingReverseShapes()
    {
        WithMap(path =>
        {
            var preview = TmapOutdoorPreviewReader.Read(path);
            Assert.Equal(3UL, preview.Nodes);
            Assert.Equal(2UL, preview.DirectedEdges);
            Assert.Equal(2, preview.Roads.Count);
            Assert.Equal(preview.Roads[0].Points.Reverse(), preview.Roads[1].Points);
            Assert.Single(preview.Contours);
            Assert.Equal(100, preview.Contours[0].ElevationMeters);
            Assert.Equal(2, preview.Contours[0].Points.Count);
            Assert.Equal(9.5, preview.Contours[0].Points[0].Longitude, 7);
            Assert.Equal(47.1, preview.Contours[0].Points[0].Latitude, 7);
            Assert.False(preview.Sampled);
        });
    }
    [Theory]
    [InlineData(160)]
    [InlineData(1024)]
    [InlineData(3 * 1024 + 40)]
    public void RejectsDamagedHeaderDirectoryAndReadDataPages(int offset)
    {
        WithMap(path =>
        {
            var raw = File.ReadAllBytes(path); raw[offset] ^= 1; File.WriteAllBytes(path, raw);
            Assert.Throws<InvalidDataException>(() => TmapOutdoorPreviewReader.Read(path));
        });
    }
    [Fact]
    public void EnforcesReadBudgetAndCancellationAndReleasesFile()
    {
        WithMap(path =>
        {
            Assert.Throws<InvalidDataException>(() => TmapOutdoorPreviewReader.Read(path, maximumReadBytes: 1024));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => TmapOutdoorPreviewReader.Read(path, cancel.Token));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
    }
    internal static byte[] Fixture()
    {
        const int pageSize = 1024;
        var nodes = new byte[3 * 32];
        var shapes = new byte[24];
        for (var i = 0; i < 3; i++)
        {
            TmapFormat.PutI32(shapes, i * 8, 471000000 + i * 1000);
            TmapFormat.PutI32(shapes, i * 8 + 4, 95000000 + i * 1000);
        }
        var edges = new byte[96];
        for (var i = 0; i < 2; i++) TmapFormat.Put32(edges, i * 48 + 32, 3);
        TmapFormat.Put16(edges, 48 + 10, 1);
        using var coordinates = new MemoryStream();
        foreach (var value in new[] { 471000000, 95000000, 1000, 1000 })
        {
            var n = unchecked((uint)((value << 1) ^ (value >> 31)));
            while (n >= 128) { coordinates.WriteByte((byte)((n & 127) | 128)); n >>= 7; }
            coordinates.WriteByte((byte)n);
        }
        var contour = new byte[24 + coordinates.Length];
        TmapFormat.Put64(contour, 0, 1); TmapFormat.PutI32(contour, 8, 100);
        TmapFormat.Put32(contour, 12, 2); TmapFormat.Put32(contour, 16, (uint)coordinates.Length);
        coordinates.ToArray().CopyTo(contour, 24);
        var parts = new[] { (1101U, nodes, 3UL), (1102U, edges, 2UL), (1103U, shapes, 3UL), (1011U, contour, 1UL) };
        var result = new byte[(parts.Length + 2) * pageSize];
        TmapFormat.Magic.CopyTo(result);
        TmapFormat.Put16(result, 8, 2); TmapFormat.Put32(result, 12, 256);
        TmapFormat.Put32(result, 16, 0x01020304); TmapFormat.Put32(result, 20, pageSize);
        TmapFormat.Put64(result, 32, (ulong)result.Length); TmapFormat.Put64(result, 40, pageSize);
        TmapFormat.Put32(result, 48, (uint)parts.Length); TmapFormat.Put32(result, 52, 64);
        TmapFormat.PutI32(result, 120, 94900000); TmapFormat.PutI32(result, 124, 470900000);
        TmapFormat.PutI32(result, 128, 95100000); TmapFormat.PutI32(result, 132, 471100000);
        for (var i = 0; i < parts.Length; i++)
        {
            var (type, raw, count) = parts[i];
            var entry = pageSize + i * 64; var at = (i + 2) * pageSize;
            TmapFormat.Put32(result, entry, type); TmapFormat.Put16(result, entry + 4, 1); TmapFormat.Put16(result, entry + 6, 2);
            TmapFormat.Put32(result, entry + 8, type); TmapFormat.Put64(result, entry + 16, (ulong)at);
            TmapFormat.Put64(result, entry + 24, pageSize); TmapFormat.Put64(result, entry + 32, count); TmapFormat.Put64(result, entry + 48, (ulong)raw.Length);
            "TPG2"u8.CopyTo(result.AsSpan(at)); TmapFormat.Put32(result, at + 4, type); TmapFormat.Put32(result, at + 12, (uint)raw.Length);
            raw.CopyTo(result, at + 32); TmapFormat.Put32(result, at + 16, TmapFormat.Crc32C(result.AsSpan(at, pageSize)));
        }
        TmapFormat.Put32(result, 164, TmapFormat.Crc32C(result.AsSpan(pageSize, parts.Length * 64)));
        TmapFormat.Put32(result, 160, TmapFormat.Crc32C(result.AsSpan(0, 256)));
        return result;
    }
    private static void WithMap(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "preview-" + Guid.NewGuid().ToString("N") + ".tmap");
        try { File.WriteAllBytes(path, Fixture()); action(path); }
        finally { File.Delete(path); }
    }
}
