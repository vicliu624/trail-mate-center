using System.Buffers.Binary;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class TmapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", "tmap-" + Guid.NewGuid().ToString("N"));
    private static readonly MapPackAreaSelection Area = new() { Name = "云南", Bounds = new(90, 10, 120, 40) };
    private static readonly TmapOptions Options = new() { PackageKey = "cn-yunnan", CountryCode = "CN", AdminCode = "CN-YN" };
    public TmapTests() => Directory.CreateDirectory(_root);
    private string Build(int places = 400)
    {
        var target = Path.Combine(_root, "map.tmap");
        using var b = new TmapBuilder(_root);
        var pixels = new byte[131072]; for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)i;
        b.AddTile(1, 0, 0, 0, pixels);
        b.AddTile(1, 8, 200, 100, pixels);
        for (var i = 1; i <= places; i++) b.AddPlace(new(TmapFormat.OsmId("node", i), 20 + i * 0.0001, 100, "water", [$"云南水源{i:D5}", $"WATER {i:D5}"], i % 100));
        b.AddAnnotation(8, 200, 100, TmapFormat.OsmId("node", 1), 1, 100, 20.0001, 100);
        var result = b.Complete(target, Area, Options); Assert.Equal(places, result.PoiCount); Assert.Equal(places * 2, result.NameCount);
        return target;
    }
    [Fact]
    public void IdenticalNativeTilesSharePayload_WithoutCrossLayerOrChangedPixelReuse()
    {
        var target = Path.Combine(_root, "shared.tmap"); var original = new byte[131072]; original[123] = 99;
        var changed = (byte[])original.Clone(); changed[123] = 100;
        using (var builder = new TmapBuilder(_root))
        {
            builder.AddTile(1, 2, 0, 0, original); builder.AddTile(1, 2, 1, 0, original);
            builder.AddTile(1, 2, 2, 0, changed); builder.AddTile(2, 2, 0, 0, original);
            builder.Complete(target, Area, Options);
        }
        using (var reader = new TmapReader(target))
        {
            Assert.Equal(original, reader.ReadTile(1, 2, 0, 0)!.Pixels);
            Assert.Equal(original, reader.ReadTile(1, 2, 1, 0)!.Pixels);
            Assert.Equal(changed, reader.ReadTile(1, 2, 2, 0)!.Pixels);
            Assert.Equal(original, reader.ReadTile(2, 2, 0, 0)!.Pixels); reader.ValidateAllPages();
        }
        using var stream = File.OpenRead(target); var header = new byte[256]; stream.ReadExactly(header);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(48)); stream.Position = 4096;
        var directory = new byte[count * 64]; stream.ReadExactly(directory);
        var lengths = Enumerable.Range(0, (int)count).Select(i => directory.AsMemory(i * 64, 64))
            .Where(entry => BinaryPrimitives.ReadUInt32LittleEndian(entry.Span) == 11)
            .ToDictionary(entry => BinaryPrimitives.ReadUInt32LittleEndian(entry.Span[12..]), entry => BinaryPrimitives.ReadUInt64LittleEndian(entry.Span[24..]));
        Assert.Equal(262144UL, lengths[1]); Assert.Equal(131072UL, lengths[2]);
    }
    [Fact]
    public void RoundTrip_ContainsNativePixelsIndexedPoiAndChineseSearch()
    {
        using var r = new TmapReader(Build());
        var tile = r.ReadTile(1, 8, 200, 100)!; Assert.Equal(1, tile.Codec); Assert.Equal(131072, tile.Pixels.Length);
        for (var i = 0; i < tile.Pixels.Length; i++) Assert.Equal((byte)i, tile.Pixels[i]);
        Assert.Null(r.ReadTile(1, 8, 201, 100));
        var p = r.FindPoi(TmapFormat.OsmId("node", 287))!; Assert.Equal(20.0287, p.Latitude, 7);
        Assert.Single(r.Search("云南水源00287", TmapSearchMode.Exact));
        Assert.Single(r.Search("ＷＡＴＥＲ　00287", TmapSearchMode.Exact));
        Assert.Equal(100, r.Search("南水", limit: 100).Count);
        Assert.Empty(r.Search("水南"));
        Assert.Equal(100, r.QueryBounds(new(99, 20.00005, 101, 20.01005)).Count());
        r.ValidateAllPages();
    }
    [Fact]
    public void CommonCharacter_MultiplePostingPages_CompleteDistanceRanking()
    {
        using var r = new TmapReader(Build(1200));
        var result = r.Search("源", limit: 20, latitude: 20.12, longitude: 100);
        Assert.Equal(20, result.Count); Assert.Equal(20.12, result[0].Latitude, 7);
        Assert.Equal(10, r.Search("云南水源001", TmapSearchMode.Prefix, 10).Count);
    }
    [Fact]
    public void AliasGramsCannotCombine_AndDuplicateAliasesReturnOnePlace()
    {
        var target = Path.Combine(_root, "aliases.tmap");
        using (var b = new TmapBuilder(_root))
        {
            b.AddPlace(new(TmapFormat.OsmId("node", 1), 25, 102, "generic", ["云南", "南大学", "云南"]));
            b.AddPlace(new(TmapFormat.OsmId("node", 2), 25, 102, "generic", ["云南大学", "云南大学校区"]));
            b.Complete(target, Area, Options);
        }
        using var r = new TmapReader(target);
        var result = r.Search("云南大学"); Assert.Single(result); Assert.Equal(TmapFormat.OsmId("node", 2), result[0].StableId);
    }
    [Fact]
    public void CorruptHeaderAndPixelAreRejected()
    {
        var file = Build(2); var bytes = File.ReadAllBytes(file);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
        var payload = Enumerable.Range(0, (int)count).Select(i => 4096 + i * 64).First(i => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 11);
        var offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(payload + 16)));
        bytes[offset] ^= 1; File.WriteAllBytes(file, bytes);
        using (var r = new TmapReader(file)) Assert.Throws<InvalidDataException>(() => r.ReadTile(1, 0, 0, 0));
        bytes[120] ^= 1; File.WriteAllBytes(file, bytes); Assert.Throws<InvalidDataException>(() => new TmapReader(file));
    }
    [Fact]
    public void CancelOrInvalidNamePreservesExistingOutput()
    {
        var path = Path.Combine(_root, "keep.tmap"); File.WriteAllText(path, "old");
        using var cts = new CancellationTokenSource();
        using (var b = new TmapBuilder(_root, token: cts.Token))
        {
            Assert.Throws<InvalidDataException>(() => b.AddPlace(new(TmapFormat.OsmId("node", 1), 25, 102, "generic", [new string('云', 200)])));
            cts.Cancel(); Assert.Throws<OperationCanceledException>(() => b.Complete(path, Area, Options));
        }
        Assert.Equal("old", File.ReadAllText(path)); Assert.Empty(Directory.GetDirectories(_root, ".tmap-build-*"));
    }
    [Fact]
    public async Task RealPbfExport_ProducesOnlySingleMapFileAndSearchablePois()
    {
        using var fixture = new LocalMapPackTests(); var p = fixture.Plan("tmap-source", "云南测试水源");
        p = p with { OutputDirectory = _root, Tmap = Options with { FileName = "pbf.tmap" }, Annotations = new MapAnnotationOptions() };
        var result = await new TmapPackExporter().ExportAsync(p);
        using var reader = new TmapReader(result.FilePath);
        Assert.NotEmpty(reader.Search("测试水源")); Assert.Single(Directory.GetFiles(_root, "*.tmap", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(_root, "maps", "tmap", "osm", "pbf-osm.tmap"), result.FilePath);
        Assert.Empty(Directory.GetDirectories(_root, ".tmap-*", SearchOption.TopDirectoryOnly));
        Assert.NotEqual(0U, reader.Capabilities & 4); Assert.Equal(0U, reader.Capabilities & 8);
    }
    [Theory]
    [InlineData(TmapRegionTier.World, 0, 7)]
    [InlineData(TmapRegionTier.LargeCountry, 8, 12)]
    [InlineData(TmapRegionTier.AdministrativeRegion, 13, 17)]
    public void RegionPresets(TmapRegionTier tier, int min, int max) => Assert.Equal((min, max), TmapOptions.Zooms(tier));
    [Fact]
    public void CrcAndMortonGoldenValues()
    {
        Assert.Equal(0xe3069283U, TmapFormat.Crc32C("123456789"u8));
        Assert.Equal((1UL << 58) | 3, TmapFormat.TileKey(1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TmapFormat.TileKey(1, 2, 1));
        Assert.Equal("water 云南", TmapFormat.NormalizeName(" ＷＡＴＥＲ　云南 "));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
