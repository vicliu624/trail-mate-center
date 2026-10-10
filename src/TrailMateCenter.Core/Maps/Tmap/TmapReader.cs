namespace TrailMateCenter.Maps.Tmap;

public enum TmapSearchMode { Exact, Prefix, Substring }
public sealed record TmapPoiInfo(ulong Row, byte[] StableId, double Latitude, double Longitude, uint Category, int Importance, string Name)
{
    public string AdministrativePath { get; init; } = "";
    public byte AdministrativeFlags { get; init; } = 1;
    public string CountryCode { get; init; } = "";
}
public sealed record TmapTile(int Layer, int Zoom, int X, int Y, ushort Codec, byte[] Pixels);
public sealed record TmapFontDependency(string Id, byte[] Sha256, uint GlyphCount, uint EstimatedRamBytes, ulong FileBytes);
public sealed record TmapAnnotation(TmapPoiInfo Poi, int Kind, int Priority, double Latitude, double Longitude,
    IReadOnlyList<(double Latitude, double Longitude)> Path);
internal sealed record TmapReadSection(uint Type, uint Id, uint Owner, ulong Offset, ulong Length, ulong Count, ulong Root, bool Paged);

/// <summary>Read-only single-threaded reference reader with a bounded page cache. No whole index is loaded.</summary>
public sealed class TmapReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly Dictionary<uint, TmapReadSection> _sections = new();
    private readonly Dictionary<(uint, ulong), byte[]> _cache = new();
    private readonly Queue<(uint, ulong)> _fifo = new();
    public long PageReads { get; private set; }
    public ulong PoiCount => Section(20).Count;
    public uint ZoomMask { get; }
    public byte[] PackageId { get; }
    public uint Capabilities { get; }
    public ulong Revision { get; }
    public double CoverageArea { get; }
    public bool HasRasterLayer(int layer) => _sections.Values.Any(s => s.Type == 10 && s.Owner == layer);
    public bool HasAnnotations => Section(40).Count != 0;
    public bool HasFastLabels => _sections.ContainsKey(43) && _sections.ContainsKey(44);
    public bool HasAdministrativeAreas => _sections.ContainsKey(50) && _sections.ContainsKey(51);
    public IReadOnlyList<TmapFontDependency> RequiredFonts()
    {
        if (!_sections.ContainsKey(52) && !_sections.ContainsKey(53)) return [];
        if (!_sections.ContainsKey(52) || !_sections.ContainsKey(53) || Section(52).Count > 64)
            throw new InvalidDataException("Invalid font dependency sections.");
        var result = new List<TmapFontDependency>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (ulong index = 1; index <= Section(52).Count; index++)
        {
            var record = Row(52, index, 64); var id = String(TmapFormat.U64(record, 0), 53);
            if (id.Length != 20 || !id.StartsWith("map-", StringComparison.Ordinal) ||
                id.AsSpan(4).ContainsAnyExcept("0123456789abcdef") || !ids.Add(id) ||
                TmapFormat.U16(record, 56) != 16 || record[58] != 2 ||
                record.AsSpan(59, 5).ContainsAnyExcept((byte)0))
                throw new InvalidDataException("Invalid or duplicate map font dependency.");
            result.Add(new(id, record.AsSpan(8, 32).ToArray(), TmapFormat.U32(record, 40),
                TmapFormat.U32(record, 44), TmapFormat.U64(record, 48)));
        }
        return result;
    }
    public IEnumerable<string> SearchableNames(CancellationToken token = default)
    {
        for (ulong row = 1; row <= Section(22).Count; row++)
        { token.ThrowIfCancellationRequested(); yield return String(TmapFormat.U64(Row(22, row, 48), 8)); }
    }
    public TmapAdministrativeLocation ReadAdministrativeLocation(ulong row)
    {
        if (!HasAdministrativeAreas) return new("", 0, 1, "");
        if (Section(50).Count != PoiCount) throw new InvalidDataException("Administrative reference count does not match POIs.");
        var record = Row(50, row, 16); var reference = TmapFormat.U64(record, 0);
        if ((record[8] & ~63) != 0 || (record[9] & ~3) != 0 || record[13] != 0 || record[14] != 0 || record[15] != 0)
            throw new InvalidDataException("Invalid administrative reference flags.");
        return new(reference == 0 ? "" : String(reference, 51), record[8], record[9],
            TmapFormat.Utf8.GetString(record, 10, 3).TrimEnd('\0'));
    }
    internal IEnumerable<(int Zoom, int X, int Y)> AnnotationTiles()
    {
        foreach (var entry in Entries(Section(40), TmapKeyKind.Number, TmapFormat.OrderedKey(0)))
        {
            var key = TmapFormat.FromOrderedKey(entry.Key); var x = 0; var y = 0;
            for (var bit = 0; bit < 29; bit++) { x |= (int)(key >> (bit * 2) & 1) << bit; y |= (int)(key >> (bit * 2 + 1) & 1) << bit; }
            yield return ((int)(key >> 58), x, y);
        }
    }
    public IReadOnlyList<TmapAnnotation> ReadDisplayAnnotations(int zoom, int x, int y)
    {
        if (!HasFastLabels) return ReadAnnotations(zoom, x, y);
        var value = Find(44, TmapKeyKind.Number, TmapFormat.OrderedKey(TmapFormat.TileKey(zoom, x, y)));
        if (value is null) return [];
        var first = TmapFormat.U64(value, 0); var count = TmapFormat.U32(value, 8);
        if (first == 0 || count is 0 or > 200 || first > Section(43).Count || count > Section(43).Count - first + 1 || TmapFormat.U32(value, 12) != 0)
            throw new InvalidDataException("Invalid fast label reference.");
        var result = new List<TmapAnnotation>();
        for (uint i = 0; i < count; i++)
        {
            var b = Row(43, first + i, 176); var n = b[29]; var points = b[28];
            if (n > 79 || points > 8 || b[32 + n] != 0 || TmapFormat.U16(b, 30) != 0) throw new InvalidDataException("Invalid fast label row.");
            var lat = TmapFormat.I32(b, 16) / 1e7; var lon = TmapFormat.I32(b, 20) / 1e7;
            var kind = TmapFormat.U16(b, 24);
            if (lat is < -90 or > 90 || lon is < -180 or >= 180 || kind is < 1 or > 3) throw new InvalidDataException("Invalid fast label coordinates or kind.");
            var path = new List<(double Latitude, double Longitude)>();
            for (var p = 0; p < points; p++)
            {
                var latitude = TmapFormat.I32(b, 112 + p * 8) / 1e7; var longitude = TmapFormat.I32(b, 116 + p * 8) / 1e7;
                if (latitude is < -90 or > 90 || longitude is < -180 or >= 180) throw new InvalidDataException("Invalid fast label path.");
                path.Add((latitude, longitude));
            }
            var poi = new TmapPoiInfo(0, b.AsSpan(0, 16).ToArray(), lat, lon, 0, TmapFormat.U16(b, 26), TmapFormat.Utf8.GetString(b, 32, n));
            result.Add(new(poi, kind, TmapFormat.U16(b, 26), lat, lon, path));
        }
        return result;
    }
    public TmapReader(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            var h = Read(0, 256);
            if (!h.AsSpan(0, 8).SequenceEqual(TmapFormat.Magic) || TmapFormat.U16(h, 8) != 1 || TmapFormat.U32(h, 12) != 256 ||
                TmapFormat.U32(h, 16) != 0x01020304 || TmapFormat.U32(h, 20) != 4096 || TmapFormat.U64(h, 32) != (ulong)_stream.Length)
                throw new InvalidDataException("Unsupported or incomplete TMAP header.");
            CheckCrc(h, 160); var count = TmapFormat.U32(h, 48);
            if (count is 0 or > 256 || TmapFormat.U32(h, 52) != 64 || TmapFormat.U64(h, 40) != 4096 || TmapFormat.U32(h, 172) != TmapFormat.NormalizationProfile)
                throw new InvalidDataException("Unsupported TMAP profile.");
            if ((TmapFormat.U64(h, 24) & ~7UL) != 0) throw new InvalidDataException("Unsupported required capability.");
            var d = Read(4096, checked((int)count * 64));
            if (TmapFormat.Crc32C(d) != TmapFormat.U32(h, 164)) throw new InvalidDataException("TMAP directory CRC mismatch.");
            ulong previousEnd = (ulong)((4096 + d.Length + 4095) / 4096 * 4096);
            for (var i = 0; i < count; i++)
            {
                var b = d.AsSpan(i * 64, 64); var offset = TmapFormat.U64(b, 16); var length = TmapFormat.U64(b, 24);
                if (TmapFormat.U16(b, 4) != 1 || offset % 4096 != 0 || offset < previousEnd || offset > (ulong)_stream.Length || length > (ulong)_stream.Length - offset)
                    throw new InvalidDataException("Invalid section bounds or schema.");
                var section = new TmapReadSection(TmapFormat.U32(b, 0), TmapFormat.U32(b, 8), TmapFormat.U32(b, 12), offset, length,
                    TmapFormat.U64(b, 32), TmapFormat.U64(b, 40), (TmapFormat.U16(b, 6) & 2) != 0);
                if (section.Paged && length % 4096 != 0 || section.Root != 0 && (section.Root % 4096 != 0 || section.Root >= length))
                    throw new InvalidDataException("Invalid section page alignment.");
                if (!_sections.TryAdd(section.Id, section)) throw new InvalidDataException("Duplicate section ID.");
                previousEnd = offset + length;
            }
            ZoomMask = TmapFormat.U32(h, 136); PackageId = h.AsSpan(56, 16).ToArray(); Capabilities = TmapFormat.U32(h, 140);
            Revision = TmapFormat.U64(h, 104);
            CoverageArea = (TmapFormat.I32(h, 128) / 1e7 - TmapFormat.I32(h, 120) / 1e7) *
                (TmapFormat.I32(h, 132) / 1e7 - TmapFormat.I32(h, 124) / 1e7);
            foreach (uint id in new uint[] { 1, 2, 20, 21, 22, 23, 24, 30, 31, 32, 40, 41, 42 }) _ = Section(id);
        }
        catch { _stream.Dispose(); throw; }
    }
    private TmapReadSection Section(uint id) => _sections.TryGetValue(id, out var s) ? s : throw new InvalidDataException($"Missing section {id}.");
    private byte[] Read(ulong offset, int length)
    {
        if (offset > (ulong)_stream.Length || (ulong)length > (ulong)_stream.Length - offset) throw new InvalidDataException("Read outside TMAP.");
        var b = new byte[length]; _stream.Position = checked((long)offset); _stream.ReadExactly(b); return b;
    }
    private static void CheckCrc(byte[] b, int offset)
    {
        var expected = TmapFormat.U32(b, offset); TmapFormat.Put32(b, offset, 0); var actual = TmapFormat.Crc32C(b); TmapFormat.Put32(b, offset, expected);
        if (expected != actual) throw new InvalidDataException("TMAP CRC mismatch.");
    }
    private byte[] Page(TmapReadSection s, ulong offset)
    {
        if (offset % 4096 != 0 || offset >= s.Length || s.Length - offset < 4096) throw new InvalidDataException("Page outside section.");
        if (_cache.TryGetValue((s.Id, offset), out var cached)) return cached;
        var b = Read(checked(s.Offset + offset), 4096); PageReads++; CheckCrc(b, 48);
        if (TmapFormat.U32(b, 0) != 0x31475054 || TmapFormat.U64(b, 8) != offset || TmapFormat.U32(b, 16) > 2016) throw new InvalidDataException("Invalid page header.");
        if (_cache.Count == 32) _cache.Remove(_fifo.Dequeue()); _cache[(s.Id, offset)] = b; _fifo.Enqueue((s.Id, offset)); return b;
    }
    private static TmapTreeEntry Entry(byte[] page, int i, TmapKeyKind kind, bool inner)
    {
        if (i < 0 || (uint)i >= TmapFormat.U32(page, 16)) throw new InvalidDataException("Index slot outside page.");
        if (kind == TmapKeyKind.Text)
        {
            var slot = 4096 - 2 * (i + 1); var pos = TmapFormat.U16(page, slot);
            if (pos < 64 || pos + 4 > 4096 - 2 * TmapFormat.U32(page, 16)) throw new InvalidDataException("Invalid text slot.");
            var k = TmapFormat.U16(page, pos); var v = TmapFormat.U16(page, pos + 2);
            if (k > 512 || v != (inner ? 8 : 32) || pos + 4 + k + v > 4096 - 2 * TmapFormat.U32(page, 16)) throw new InvalidDataException("Invalid text entry.");
            return new(page.AsSpan(pos + 4, k).ToArray(), page.AsSpan(pos + 4 + k, v).ToArray());
        }
        var size = TmapFormat.U16(page, 20); var keySize = kind == TmapKeyKind.Number ? 8 : 16;
        var at = 64 + i * size;
        if (size < keySize + 8 || at + size > 4096) throw new InvalidDataException("Invalid fixed index entry.");
        var key = kind == TmapKeyKind.Number ? TmapFormat.OrderedKey(TmapFormat.U64(page, at)) : page.AsSpan(at, keySize).ToArray();
        return new(key, page.AsSpan(at + keySize, size - keySize).ToArray());
    }
    private static int LowerBound(byte[] p, byte[] key, TmapKeyKind kind, bool inner)
    {
        var lo = 0; var hi = (int)TmapFormat.U32(p, 16);
        while (lo < hi) { var m = lo + (hi - lo) / 2; if (Entry(p, m, kind, inner).Key.AsSpan().SequenceCompareTo(key) < 0) lo = m + 1; else hi = m; }
        return lo;
    }
    private IEnumerable<TmapTreeEntry> Entries(TmapReadSection s, TmapKeyKind kind, byte[] lower)
    {
        if (s.Root == 0) yield break;
        var pos = s.Root; var depth = 0; byte[] page;
        var expectedLevel = -1;
        while (true)
        {
            if (++depth > 32) throw new InvalidDataException("Excessive tree height.");
            page = Page(s, pos); var level = TmapFormat.U16(page, 6);
            if (expectedLevel >= 0 && level != expectedLevel) throw new InvalidDataException("Non-decreasing tree level.");
            if (level == 0) break;
            var at = LowerBound(page, lower, kind, true); if (at == TmapFormat.U32(page, 16)) yield break;
            pos = TmapFormat.U64(Entry(page, at, kind, true).Value, 0); expectedLevel = level - 1;
        }
        ulong visited = 0; byte[]? last = null;
        while (true)
        {
            if (++visited > s.Length / 4096) throw new InvalidDataException("Cyclic leaf chain.");
            for (var at = LowerBound(page, lower, kind, false); at < TmapFormat.U32(page, 16); at++)
            {
                var entry = Entry(page, at, kind, false);
                if (last is not null && last.AsSpan().SequenceCompareTo(entry.Key) >= 0) throw new InvalidDataException("Unsorted index.");
                last = entry.Key; yield return entry;
            }
            pos = TmapFormat.U64(page, 24); if (pos == 0) break; page = Page(s, pos);
            if (TmapFormat.U16(page, 6) != 0) throw new InvalidDataException("Invalid leaf chain.");
        }
    }
    private byte[]? Find(uint sectionId, TmapKeyKind kind, byte[] key)
    { var e = Entries(Section(sectionId), kind, key).FirstOrDefault(); return e is not null && e.Key.AsSpan().SequenceEqual(key) ? e.Value : null; }

    public TmapTile? ReadTile(int layer, int z, int x, int y)
    {
        var index = _sections.Values.SingleOrDefault(s => s.Type == 10 && s.Owner == layer); if (index is null) return null;
        var entry = Find(index.Id, TmapKeyKind.Number, TmapFormat.OrderedKey(TmapFormat.TileKey(z, x, y))); if (entry is null) return null;
        var data = _sections.Values.Single(s => s.Type == 11 && s.Owner == layer);
        var offset = TmapFormat.U64(entry, 0); var length = TmapFormat.U32(entry, 8); var codec = TmapFormat.U16(entry, 16);
        if (length != (codec == 1 ? 131072 : codec == 2 ? 262144 : 0) || length == 0 || offset > data.Length || length > data.Length - offset)
            throw new InvalidDataException("Invalid tile payload.");
        var pixels = Read(data.Offset + offset, (int)length);
        if (TmapFormat.Crc32C(pixels) != TmapFormat.U32(entry, 20)) throw new InvalidDataException("Tile CRC mismatch.");
        return new(layer, z, x, y, codec, pixels);
    }
    private byte[] Row(uint section, ulong row, int size)
    {
        var s = Section(section); if (row == 0 || row > s.Count) throw new InvalidDataException("Invalid record ID.");
        var capacity = (uint)(4032 / size); var page = Page(s, (row - 1) / capacity * 4096); var slot = (int)((row - 1) % capacity);
        if ((uint)slot >= TmapFormat.U32(page, 16) || TmapFormat.U16(page, 20) != size) throw new InvalidDataException("Invalid record page.");
        return page.AsSpan(64 + slot * size, size).ToArray();
    }
    private string String(ulong reference, uint section = 23)
    {
        var page = Page(Section(section), reference / 4096 * 4096); var at = (int)(reference % 4096);
        if (at < 64 || at + 4 > 4096) throw new InvalidDataException("Invalid string reference.");
        var n = TmapFormat.U16(page, at); if (n > 512 || at + 4 + n > 4096) throw new InvalidDataException("Invalid string length.");
        return TmapFormat.Utf8.GetString(page.AsSpan(at + 4, n));
    }
    public TmapPoiInfo ReadPoi(ulong row)
    {
        var p = Row(20, row, 96); var name = TmapFormat.U64(p, 32);
        var location = ReadAdministrativeLocation(row);
        return new(row, p.AsSpan(0, 16).ToArray(), TmapFormat.I32(p, 16) / 1e7, TmapFormat.I32(p, 20) / 1e7,
            TmapFormat.U32(p, 24), TmapFormat.U16(p, 56), name == 0 ? "" : String(TmapFormat.U64(Row(22, name, 48), 8)))
        { AdministrativePath = location.Path, AdministrativeFlags = location.Flags, CountryCode = location.CountryCode };
    }
    public TmapPoiInfo? FindPoi(byte[] stableId)
    { if (stableId.Length != 16) throw new ArgumentException("Expected a 16-byte identity."); var r = Find(21, TmapKeyKind.Id, stableId); return r is null ? null : ReadPoi(TmapFormat.U64(r, 0)); }
    public IReadOnlyList<TmapAnnotation> ReadAnnotations(int zoom, int x, int y)
    {
        var value = Find(40, TmapKeyKind.Number, TmapFormat.OrderedKey(TmapFormat.TileKey(zoom, x, y)));
        if (value is null) return [];
        var bytes = TmapFormat.U32(value, 8);
        if (bytes > 8000 || bytes % 40 != 0) throw new InvalidDataException("Invalid annotation list size.");
        var result = new List<TmapAnnotation>(); var reference = TmapFormat.U64(value, 0);
        while (reference != 0)
        {
            var page = Page(Section(41), reference); var count = TmapFormat.U32(page, 16);
            if (TmapFormat.U16(page, 4) != 15 || count is 0 or > 100 || result.Count + count > bytes / 40)
                throw new InvalidDataException("Invalid annotation page.");
            for (var i = 0; i < count; i++)
            {
                var at = 64 + i * 40; var path = new List<(double, double)>();
                var geometry = TmapFormat.U64(page, at + 24);
                if (geometry != 0)
                {
                    var g = Page(Section(42), geometry / 4096 * 4096); var start = (int)(geometry % 4096);
                    if (start < 64 || start + 16 > 4096 || TmapFormat.U16(g, start) != 1)
                        throw new InvalidDataException("Invalid annotation geometry.");
                    var points = TmapFormat.U32(g, start + 8);
                    if (points > 8 || TmapFormat.U32(g, start + 4) != points * 8 || start + 16 + points * 8 > 4096)
                        throw new InvalidDataException("Invalid annotation point count.");
                    for (var j = 0; j < points; j++)
                        path.Add((TmapFormat.I32(g, start + 16 + j * 8) / 1e7, TmapFormat.I32(g, start + 20 + j * 8) / 1e7));
                }
                result.Add(new(ReadPoi(TmapFormat.U64(page, at)), TmapFormat.U16(page, at + 8), TmapFormat.U16(page, at + 12),
                    TmapFormat.I32(page, at + 16) / 1e7, TmapFormat.I32(page, at + 20) / 1e7, path));
            }
            reference = TmapFormat.U64(page, 24);
        }
        if (result.Count != bytes / 40) throw new InvalidDataException("Truncated annotation list.");
        return result;
    }
    private IEnumerable<ulong> Posting(byte[] reference)
    {
        var total = TmapFormat.U64(reference, 8); var blocks = TmapFormat.U32(reference, 16); var start = TmapFormat.U64(reference, 0);
        if (TmapFormat.U16(reference, 20) != 0 || blocks != (total + 503) / 504) throw new InvalidDataException("Invalid posting reference.");
        ulong seen = 0, previous = 0;
        for (uint block = 0; block < blocks; block++)
        {
            var dir = Page(Section(32), checked(start + block / 168 * 4096UL)); var at = 64 + (int)(block % 168) * 24;
            var data = Page(Section(32), TmapFormat.U64(dir, at + 16)); var n = TmapFormat.U32(data, 16);
            if (n is 0 or > 504) throw new InvalidDataException("Invalid posting block.");
            for (var j = 0; j < n; j++)
            {
                var id = TmapFormat.U64(data, 64 + j * 8);
                if (id <= previous || ++seen > total) throw new InvalidDataException("Unsorted posting list."); previous = id; yield return id;
            }
        }
        if (seen != total) throw new InvalidDataException("Truncated posting list.");
    }
    private bool PostingContains(byte[] reference, ulong id)
    {
        long lo = 0, hi = TmapFormat.U32(reference, 16); var start = TmapFormat.U64(reference, 0);
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2; var d = Page(Section(32), checked(start + (ulong)(mid / 168) * 4096)); var at = 64 + (int)(mid % 168) * 24;
            if (TmapFormat.U64(d, at + 8) < id) lo = mid + 1; else hi = mid;
        }
        if (lo == TmapFormat.U32(reference, 16)) return false;
        var dir = Page(Section(32), checked(start + (ulong)(lo / 168) * 4096)); var slot = 64 + (int)(lo % 168) * 24;
        if (id < TmapFormat.U64(dir, slot)) return false;
        var data = Page(Section(32), TmapFormat.U64(dir, slot + 16)); var a = 0; var b = (int)TmapFormat.U32(data, 16);
        if (b > 504) throw new InvalidDataException("Invalid posting page.");
        while (a < b) { var mid = a + (b - a) / 2; if (TmapFormat.U64(data, 64 + mid * 8) < id) a = mid + 1; else b = mid; }
        return a < TmapFormat.U32(data, 16) && TmapFormat.U64(data, 64 + a * 8) == id;
    }
    private IEnumerable<ulong> Candidates(string query, TmapSearchMode mode, CancellationToken token)
    {
        if (mode != TmapSearchMode.Substring)
        {
            var key = TmapFormat.Utf8.GetBytes(query);
            foreach (var entry in Entries(Section(30), TmapKeyKind.Text, key))
            {
                token.ThrowIfCancellationRequested();
                if (mode == TmapSearchMode.Exact ? !entry.Key.AsSpan().SequenceEqual(key) : !entry.Key.AsSpan().StartsWith(key)) yield break;
                foreach (var id in Posting(entry.Value)) yield return id;
            }
            yield break;
        }
        var refs = new List<byte[]>(8);
        foreach (var gram in TmapFormat.Grams(query, false))
        {
            token.ThrowIfCancellationRequested();
            var reference = Find(31, TmapKeyKind.Number, TmapFormat.OrderedKey(gram)); if (reference is null) yield break;
            refs.Add(reference); refs.Sort((a, b) => TmapFormat.U64(a, 8).CompareTo(TmapFormat.U64(b, 8))); if (refs.Count > 8) refs.RemoveAt(8);
        }
        if (refs.Count == 0) yield break;
        foreach (var id in Posting(refs[0]))
        { token.ThrowIfCancellationRequested(); if (refs.Skip(1).All(r => PostingContains(r, id))) yield return id; }
    }
    public IReadOnlyList<TmapPoiInfo> Search(string text, TmapSearchMode mode = TmapSearchMode.Substring, int limit = 20,
        double? latitude = null, double? longitude = null, CancellationToken token = default)
    {
        if (limit is < 1 or > 1000 || latitude.HasValue != longitude.HasValue || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(limit));
        if (latitude.HasValue && (!double.IsFinite(latitude.Value) || !double.IsFinite(longitude!.Value) || latitude is < -90 or > 90 || longitude is < -180 or > 180))
            throw new ArgumentOutOfRangeException(nameof(latitude));
        var query = TmapFormat.NormalizeName(text); if (query.Length == 0) return [];
        var top = new SortedSet<RankedPoi>(Comparer<RankedPoi>.Create((a, b) =>
        {
            var n = a.Match.CompareTo(b.Match); if (n == 0) n = a.Distance.CompareTo(b.Distance);
            if (n == 0) n = b.Poi.Importance.CompareTo(a.Poi.Importance); if (n == 0) n = a.Poi.StableId.AsSpan().SequenceCompareTo(b.Poi.StableId); return n;
        }));
        foreach (var name in Candidates(query, mode, token))
        {
            token.ThrowIfCancellationRequested(); var nr = Row(22, name, 48); var row = TmapFormat.U64(nr, 0);
            var poi = ReadPoi(row); var p = Row(20, row, 96); var best = 3;
            for (uint i = 0; i < TmapFormat.U32(p, 48); i++)
            {
                token.ThrowIfCancellationRequested();
                var normalized = String(TmapFormat.U64(Row(22, checked(TmapFormat.U64(p, 40) + i), 48), 16));
                var match = normalized == query ? 0 : normalized.StartsWith(query, StringComparison.Ordinal) ? 1 : normalized.Contains(query, StringComparison.Ordinal) ? 2 : 3;
                best = Math.Min(best, match);
            }
            if (best > (int)mode) continue;
            top.Add(new(poi, best, latitude.HasValue ? Distance(latitude.Value, longitude!.Value, poi.Latitude, poi.Longitude) : 0));
            if (top.Count > limit) top.Remove(top.Max!);
        }
        return top.Select(x => x.Poi).ToArray();
    }
    private sealed record RankedPoi(TmapPoiInfo Poi, int Match, double Distance);
    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        var dlat = (lat2 - lat1) * Math.PI / 180; var dlon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dlon / 2), 2);
        return Math.Round(6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))));
    }
    public IEnumerable<TmapPoiInfo> QueryBounds(GeoBounds bounds, CancellationToken token = default)
    {
        var s = Section(24); if (s.Root == 0) yield break;
        foreach (var row in Visit(s.Root, 0)) yield return ReadPoi(row);
        IEnumerable<ulong> Visit(ulong reference, int depth)
        {
            token.ThrowIfCancellationRequested(); if (depth > 32) throw new InvalidDataException("Spatial tree cycle.");
            var page = Page(s, reference); var leaf = TmapFormat.U16(page, 4) == 14; var count = TmapFormat.U32(page, 16);
            if (count > 126) throw new InvalidDataException("Invalid spatial node.");
            for (var i = 0; i < count; i++)
            {
                var at = 64 + i * 32;
                if (TmapFormat.I32(page, at) / 1e7 > bounds.East || TmapFormat.I32(page, at + 8) / 1e7 < bounds.West ||
                    TmapFormat.I32(page, at + 4) / 1e7 > bounds.North || TmapFormat.I32(page, at + 12) / 1e7 < bounds.South) continue;
                var child = TmapFormat.U64(page, at + 16);
                if (!leaf) { foreach (var row in Visit(child, depth + 1)) yield return row; }
                else
                {
                    var n = TmapFormat.U32(page, at + 24); if (n > 42) throw new InvalidDataException("Invalid spatial leaf span.");
                    for (uint j = 0; j < n; j++) { var p = ReadPoi(checked(child + j)); if (bounds.Contains(p.Latitude, p.Longitude)) yield return p.Row; }
                }
            }
        }
    }
    public void ValidateAllPages(CancellationToken token = default)
    {
        foreach (var s in _sections.Values.Where(s => s.Paged))
            for (ulong at = 0; at < s.Length; at += 4096) { token.ThrowIfCancellationRequested(); _ = Page(s, at); }
        foreach (var index in _sections.Values.Where(s => s.Type == 10))
            foreach (var entry in Entries(index, TmapKeyKind.Number, new byte[8]))
            {
                token.ThrowIfCancellationRequested(); var data = _sections.Values.Single(s => s.Type == 11 && s.Owner == index.Owner);
                var offset = TmapFormat.U64(entry.Value, 0); var length = TmapFormat.U32(entry.Value, 8);
                if (length is not (131072 or 262144) || offset > data.Length || length > data.Length - offset) throw new InvalidDataException("Invalid tile span.");
                if (TmapFormat.Crc32C(Read(data.Offset + offset, (int)length)) != TmapFormat.U32(entry.Value, 20)) throw new InvalidDataException("Invalid tile CRC.");
            }
    }
    public void Dispose() => _stream.Dispose();
}
