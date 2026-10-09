using System.Buffers.Binary;
using System.Text;

namespace TrailMateCenter.Maps.Tmap;

public static class TmapFormat
{
    public const int PageSize = 4096, PageHeader = 64;
    public const uint NormalizationProfile = 2; // Explicit portable scalar profile; never impersonate Unicode NFKC_CF.
    public static ReadOnlySpan<byte> Magic => "TMAP\r\n\u001a\n"u8;
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b[o..]);
    public static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    public static ulong U64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b[o..]);
    public static int I32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b[o..]);
    public static void Put16(Span<byte> b, int o, int n) => BinaryPrimitives.WriteUInt16LittleEndian(b[o..], checked((ushort)n));
    public static void Put32(Span<byte> b, int o, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(b[o..], n);
    public static void Put64(Span<byte> b, int o, ulong n) => BinaryPrimitives.WriteUInt64LittleEndian(b[o..], n);
    public static void PutI32(Span<byte> b, int o, int n) => BinaryPrimitives.WriteInt32LittleEndian(b[o..], n);
    public static ulong Morton(uint x, uint y)
    {
        ulong result = 0;
        for (var i = 0; i < 29; i++) result |= ((ulong)(x >> i & 1) << (2 * i)) | ((ulong)(y >> i & 1) << (2 * i + 1));
        return result;
    }
    public static ulong TileKey(int z, int x, int y)
    {
        if (z is < 0 or > 29 || x < 0 || y < 0 || x >= 1L << z || y >= 1L << z) throw new ArgumentOutOfRangeException(nameof(z));
        return ((ulong)z << 58) | Morton((uint)x, (uint)y);
    }
    internal static byte[] OrderedKey(ulong value)
    {
        var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); return b;
    }
    internal static ulong FromOrderedKey(byte[] b) => BinaryPrimitives.ReadUInt64BigEndian(b);
    public static byte[] OsmId(string type, long id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var b = new byte[16]; Put32(b, 0, 1);
        Put16(b, 4, type switch { "node" => 1, "way" => 2, "relation" => 3, _ => throw new ArgumentException("Unknown OSM object type.") });
        Put64(b, 8, (ulong)id); return b;
    }
    // Profile 2 is deliberately frozen and table-free: full-width ASCII, ASCII lowercase,
    // listed whitespace. All other Unicode scalars remain unchanged. Readers reproduce it exactly.
    public static string NormalizeName(string input)
    {
        _ = Utf8.GetByteCount(input); // Reject invalid surrogate sequences.
        var output = new StringBuilder(); var space = false;
        foreach (var rune in input.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is >= 0xff01 and <= 0xff5e) c -= 0xfee0;
            if (c is >= 0x41 and <= 0x5a) c += 32;
            if (c is 9 or 10 or 11 or 12 or 13 or 32 or 0x85 or 0xa0 or 0x1680 or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 || c is >= 0x2000 and <= 0x200a)
            { space = output.Length > 0; continue; }
            if (space) output.Append(' ');
            space = false; output.Append(new Rune(c));
        }
        var value = output.ToString();
        if (Utf8.GetByteCount(value) > 512) throw new ArgumentException("TMAP names/queries must fit in 512 UTF-8 bytes.");
        return value;
    }
    internal static IEnumerable<ulong> Grams(string value, bool includeSingles)
    {
        var r = value.EnumerateRunes().Select(x => (uint)x.Value).ToArray();
        var seen = new HashSet<ulong>();
        for (var i = 0; i < r.Length; i++)
        {
            if (includeSingles || r.Length == 1)
            { var k = (1UL << 42) | ((ulong)r[i] << 21); if (seen.Add(k)) yield return k; }
            if (i + 1 < r.Length)
            { var k = (2UL << 42) | ((ulong)r[i] << 21) | r[i + 1]; if (seen.Add(k)) yield return k; }
        }
    }
    public static uint Crc32C(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
        return ~crc;
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i; for (var j = 0; j < 8; j++) c = (c >> 1) ^ ((c & 1) != 0 ? 0x82f63b78U : 0); return c;
    }).ToArray();
    internal static byte[] Page(ushort type, ulong offset, int count, int size = 0, int level = 0, ulong next = 0)
    {
        var b = new byte[PageSize]; Put32(b, 0, 0x31475054); Put16(b, 4, type); Put16(b, 6, level);
        Put64(b, 8, offset); Put32(b, 16, (uint)count); Put16(b, 20, size); Put64(b, 24, next); return b;
    }
    internal static void WritePage(Stream stream, byte[] page)
    { Put32(page, 48, 0); Put32(page, 48, Crc32C(page)); stream.Write(page); }
    internal static void Align(Stream s, int alignment = PageSize)
    { var n = (alignment - s.Position % alignment) % alignment; if (n != 0) s.Write(new byte[checked((int)n)]); }
}

public enum TmapRegionTier { Custom, World, LargeCountry, AdministrativeRegion }
public sealed record TmapOptions
{
    public uint SourceNamespaceId { get; init; } = 1;
    public string SourceNamespaceUri { get; init; } = "https://www.openstreetmap.org";
    public string Attribution { get; init; } = "© OpenStreetMap contributors; ODbL-1.0; https://www.openstreetmap.org/copyright";
    public string SourceCoverage { get; init; } = "OSM named nodes plus supported annotation ways/relations; not all OSM objects";
    public string FileName { get; init; } = "map.tmap";
    public string PackageKey { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public string AdminCode { get; init; } = "";
    public string Series { get; init; } = "trail-mate-base-v1";
    public TmapRegionTier Tier { get; init; }
    public ulong Revision { get; init; } = 1;
    public long MaximumOutputBytes { get; init; } = long.MaxValue;
    public string? AdministrativeBoundaryManifest { get; init; }
    public bool GenerateFontPacks { get; init; } = true;
    public string? FontOutputDirectory { get; init; }
    public static (int Minimum, int Maximum) Zooms(TmapRegionTier tier) => tier switch
    { TmapRegionTier.World => (0, 7), TmapRegionTier.LargeCountry => (8, 12), TmapRegionTier.AdministrativeRegion => (13, 17), _ => (0, 18) };
}
public sealed record TmapBuildResult(string FilePath, long TileCount, long PoiCount, long NameCount, long FileBytes)
{
    public IReadOnlyList<TmapBuildResult> Packages { get; init; } = [];
    public long UniqueTilePayloads { get; init; }
    public long StoredPixelBytes { get; init; }
}
internal sealed record TmapSection(uint Type, uint Id, uint Owner, string Path, ulong Count = 0, ulong Root = 0, bool Paged = true);
public sealed record TmapPlace(byte[] StableId, double Latitude, double Longitude, string Category, IReadOnlyList<string> Names,
    int Importance = 0, int Kind = 1, int MinimumZoom = 0);
