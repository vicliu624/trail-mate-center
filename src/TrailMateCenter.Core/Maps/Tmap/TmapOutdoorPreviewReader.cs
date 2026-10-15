namespace TrailMateCenter.Maps.Tmap;

public readonly record struct TmapPreviewPoint(double Longitude, double Latitude);
public sealed record TmapPreviewLine(IReadOnlyList<TmapPreviewPoint> Points, int? ElevationMeters = null);
public sealed record TmapOutdoorPreview(GeoBounds Bounds, long FileBytes, ulong Nodes, ulong DirectedEdges,
    IReadOnlyList<TmapPreviewLine> Roads, IReadOnlyList<TmapPreviewLine> Contours, bool Sampled)
{
    public string SourcePath { get; init; } = "";
}

/// <summary>Bounded desktop preview of actual outdoor-v2 vectors. Not a routing engine.</summary>
public static partial class TmapOutdoorPreviewReader
{
    public static TmapOutdoorPreview Read(string path, CancellationToken token = default,
        long maximumReadBytes = 64 * 1024 * 1024)
    {
        using var source = new PagedSource(path, maximumReadBytes, token);
        var roads = new List<TmapPreviewLine>();
        var contours = new List<TmapPreviewLine>();
        var pointsRetained = 0;
        const int maxPoints = 100000, maxRoads = 1000, maxContours = 500;
        var edgeCount = source.Count(1102);
        if (edgeCount > uint.MaxValue || source.Count(1101) > uint.MaxValue ||
            source.Length(1102) != edgeCount * 48 || source.Length(1101) != source.Count(1101) * 32)
            throw new InvalidDataException("Invalid preview graph table dimensions.");
        var stride = Math.Max(1UL, (edgeCount + maxRoads - 1) / maxRoads);
        for (ulong row = 0; row < edgeCount; row += stride)
        {
            var edge = source.Read(1102, checked(row * 48), 48);
            var count = TmapFormat.U32(edge, 32);
            var offset = TmapFormat.U64(edge, 24);
            if (count is < 2 or > 1000000) throw new InvalidDataException("Invalid road shape for preview.");
            var line = new List<TmapPreviewPoint>();
            var step = Math.Max(1U, (count + 62) / 63);
            for (uint i = 0; i < count; i += step)
                line.Add(Coordinate(source.Read(1103, checked(offset + i * 8UL), 8)));
            if ((count - 1) % step != 0) line.Add(Coordinate(source.Read(1103, checked(offset + (count - 1) * 8UL), 8)));
            if ((TmapFormat.U16(edge, 10) & 1) != 0) line.Reverse();
            pointsRetained += line.Count;
            if (pointsRetained > maxPoints) break;
            roads.Add(new(line));
        }
        var contourType = source.Has(1013) ? 1013U : 1011U;
        var contourCount = source.Count(contourType);
        if (contourCount > 1000000) throw new InvalidDataException("Contour preview chunk budget exceeded.");
        var contourStride = Math.Max(1UL, (contourCount + maxContours - 1) / maxContours);
        ulong at = 0;
        for (ulong row = 0; row < contourCount; row++)
        {
            var header = source.Read(contourType, at, 24);
            var count = TmapFormat.U32(header, 12);
            var bytes = TmapFormat.U32(header, 16);
            var flags = TmapFormat.U32(header, 20);
            if (count is < 2 or > 1000000 || bytes < count * ((flags & 1) != 0 ? 3UL : 2UL) || (flags & ~1U) != 0)
                throw new InvalidDataException("Invalid contour chunk for preview.");
            at = checked(at + 24);
            if (row % contourStride == 0 && count <= 128 && pointsRetained + count <= maxPoints)
            {
                var encoded = source.Read(contourType, at, checked((int)bytes));
                var position = 0;
                long latitude = 0, longitude = 0;
                var line = new List<TmapPreviewPoint>();
                for (var i = 0; i < count; ++i)
                {
                    latitude += Varint(encoded, ref position);
                    longitude += Varint(encoded, ref position);
                    if (latitude is < -900000000 or > 900000000 || longitude is < -1800000000 or > 1800000000)
                        throw new InvalidDataException("Contour coordinate is outside WGS84.");
                    if ((flags & 1) != 0)
                    {
                        if (position >= encoded.Length || encoded[position] is < 12 or > 18)
                            throw new InvalidDataException("Invalid contour display level.");
                        position++;
                    }
                    line.Add(new(longitude / 1e7, latitude / 1e7));
                }
                if (position != encoded.Length) throw new InvalidDataException("Unexpected contour geometry bytes.");
                contours.Add(new(line, TmapFormat.I32(header, 8)));
                pointsRetained += line.Count;
            }
            at = checked(at + bytes);
            if (at > source.Length(contourType)) throw new InvalidDataException("Contour chunk exceeds section.");
        }
        if (at != source.Length(contourType)) throw new InvalidDataException("Contour record count does not match its data.");
        return new(source.Bounds, source.FileBytes, source.Count(1101), edgeCount, roads, contours,
            edgeCount > (ulong)roads.Count || contourCount > (ulong)contours.Count)
        { SourcePath = path };
    }

    private static TmapPreviewPoint Coordinate(byte[] raw)
    {
        var lat = TmapFormat.I32(raw, 0); var lon = TmapFormat.I32(raw, 4);
        if (lat is < -900000000 or > 900000000 || lon is < -1800000000 or > 1800000000)
            throw new InvalidDataException("Road coordinate is outside WGS84.");
        return new(lon / 1e7, lat / 1e7);
    }
    private static int Varint(byte[] raw, ref int position)
    {
        uint value = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            if (position >= raw.Length) throw new InvalidDataException("Truncated contour coordinate.");
            var b = raw[position++];
            if (shift == 28 && b > 15) throw new InvalidDataException("Contour coordinate overflow.");
            value |= (uint)(b & 127) << shift;
            if (b < 128) return unchecked((int)((value >> 1) ^ (0U - (value & 1))));
        }
        throw new InvalidDataException("Invalid contour coordinate.");
    }

    private sealed record Section(ulong Offset, ulong PhysicalBytes, ulong Count, ulong LogicalBytes);
    private sealed class PagedSource : IDisposable
    {
        private readonly FileStream _file;
        private readonly CancellationToken _token;
        private readonly long _maximumBytes;
        private long _readBytes;
        private readonly Dictionary<uint, Section> _sections = new();
        private readonly Dictionary<(uint, ulong), byte[]> _cache = new();
        private readonly Queue<(uint, ulong)> _order = new();
        private readonly int _pageSize;
        public long FileBytes => _file.Length;
        public GeoBounds Bounds { get; }
        public PagedSource(string path, long maximumBytes, CancellationToken token)
        {
            if (maximumBytes < 256) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _token = token; _maximumBytes = maximumBytes;
            try
            {
                var h = Physical(0, 256);
                if (!h.AsSpan(0, 8).SequenceEqual(TmapFormat.Magic) || TmapFormat.U16(h, 8) != 2 || TmapFormat.U16(h, 10) != 0 ||
                    TmapFormat.U32(h, 12) != 256 || TmapFormat.U32(h, 16) != 0x01020304 || TmapFormat.U64(h, 24) != 0 ||
                    TmapFormat.U64(h, 32) != (ulong)_file.Length || TmapFormat.U32(h, 52) != 64)
                    throw new InvalidDataException("Not a supported outdoor navigation TMAP.");
                var crc = TmapFormat.U32(h, 160); TmapFormat.Put32(h, 160, 0);
                if (TmapFormat.Crc32C(h) != crc) throw new InvalidDataException("Map header checksum failed.");
                _pageSize = checked((int)TmapFormat.U32(h, 20));
                if (_pageSize is not (1024 or 2048 or 4096)) throw new InvalidDataException("Unsupported map page size.");
                var count = TmapFormat.U32(h, 48);
                if (count is < 1 or > 64) throw new InvalidDataException("Invalid map section count.");
                var table = Physical(TmapFormat.U64(h, 40), checked((int)count * 64));
                if (TmapFormat.Crc32C(table) != TmapFormat.U32(h, 164)) throw new InvalidDataException("Map directory checksum failed.");
                for (var i = 0; i < count; i++)
                {
                    var row = table.AsSpan(i * 64, 64);
                    var offset = TmapFormat.U64(row, 16); var bytes = TmapFormat.U64(row, 24); var logical = TmapFormat.U64(row, 48);
                    var type = TmapFormat.U32(row, 0);
                    if (TmapFormat.U16(row, 4) != 1 || TmapFormat.U16(row, 6) != 2 || TmapFormat.U32(row, 8) != type ||
                        TmapFormat.U32(row, 12) != 0 || TmapFormat.U64(row, 40) != 0 || TmapFormat.U64(row, 56) != 0 ||
                        offset > (ulong)_file.Length || bytes > (ulong)_file.Length - offset ||
                        offset % (uint)_pageSize != 0 || bytes % (uint)_pageSize != 0 ||
                        logical > bytes / (uint)_pageSize * (uint)(_pageSize - 32) ||
                        !_sections.TryAdd(type, new(offset, bytes, TmapFormat.U64(row, 32), logical)))
                        throw new InvalidDataException("Invalid map section.");
                }
                Bounds = new(TmapFormat.I32(h, 120) / 1e7, TmapFormat.I32(h, 124) / 1e7,
                    TmapFormat.I32(h, 128) / 1e7, TmapFormat.I32(h, 132) / 1e7);
                if (!double.IsFinite(Bounds.West) || Bounds.West >= Bounds.East || Bounds.South >= Bounds.North ||
                    Bounds.West < -180 || Bounds.East > 180 || Bounds.South < -90 || Bounds.North > 90)
                    throw new InvalidDataException("Invalid map bounds.");
            }
            catch { _file.Dispose(); throw; }
        }
        public bool Has(uint type) => _sections.ContainsKey(type);
        public ulong Count(uint type) => _sections.TryGetValue(type, out var value) ? value.Count : 0;
        public ulong Length(uint type) => _sections.TryGetValue(type, out var value) ? value.LogicalBytes : 0;
        private byte[] Physical(ulong at, int bytes)
        {
            _token.ThrowIfCancellationRequested();
            if (bytes < 0 || at > (ulong)_file.Length || (ulong)bytes > (ulong)_file.Length - at) throw new InvalidDataException("Truncated map.");
            if (bytes > _maximumBytes - _readBytes) throw new InvalidDataException("Desktop map preview read budget exceeded.");
            _readBytes += bytes;
            var result = new byte[bytes]; _file.Position = checked((long)at); _file.ReadExactly(result);
            return result;
        }
        public byte[] Read(uint type, ulong at, int bytes)
        {
            _token.ThrowIfCancellationRequested();
            if (bytes is < 0 or > 2048 || !_sections.TryGetValue(type, out var section) || at > section.LogicalBytes || (ulong)bytes > section.LogicalBytes - at)
                throw new InvalidDataException("Invalid preview section read.");
            var result = new byte[bytes]; var written = 0;
            while (written < bytes)
            {
                var ordinal = at / (uint)(_pageSize - 32); var within = (int)(at % (uint)(_pageSize - 32));
                var key = (type, ordinal);
                if (!_cache.TryGetValue(key, out var page))
                {
                    if (ordinal >= section.PhysicalBytes / (uint)_pageSize) throw new InvalidDataException("Invalid page ordinal.");
                    page = Physical(section.Offset + ordinal * (uint)_pageSize, _pageSize);
                    var crc = TmapFormat.U32(page, 16); TmapFormat.Put32(page, 16, 0);
                    if (!page.AsSpan(0, 4).SequenceEqual("TPG2"u8) || TmapFormat.U32(page, 4) != type ||
                        TmapFormat.U32(page, 8) != ordinal || TmapFormat.U32(page, 12) > _pageSize - 32 || TmapFormat.Crc32C(page) != crc)
                        throw new InvalidDataException("Map data-page checksum or identity failed.");
                    if (_cache.Count == 8) _cache.Remove(_order.Dequeue());
                    _cache.Add(key, page); _order.Enqueue(key);
                }
                var used = checked((int)TmapFormat.U32(page, 12));
                var take = Math.Min(bytes - written, used - within);
                if (take <= 0) throw new InvalidDataException("Truncated map payload.");
                page.AsSpan(32 + within, take).CopyTo(result.AsSpan(written));
                written += take; at += (uint)take;
            }
            return result;
        }
        public void Dispose() => _file.Dispose();
    }
}
