using System.Buffers.Binary;

namespace TrailMateCenter.Maps.Tmap;

internal sealed class TmapTable : IDisposable
{
    private readonly FileStream _stream;
    private readonly ushort _type;
    private readonly int _entrySize;
    private byte[] _page;
    private int _used = 64, _count;
    public ulong Count { get; private set; }
    public TmapTable(string path, ushort type, int entrySize = 0)
    {
        _stream = File.Create(path); _type = type; _entrySize = entrySize;
        _page = TmapFormat.Page(type, 0, 0, entrySize);
    }
    public ulong Add(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 4032 || (_entrySize != 0 && bytes.Length != _entrySize)) throw new InvalidDataException("Invalid table record size.");
        if (_used + bytes.Length > 4096) FlushPage();
        var address = (ulong)_stream.Position + (uint)_used;
        bytes.CopyTo(_page.AsSpan(_used)); _used += bytes.Length; _count++; Count++; return address;
    }
    public ulong String(string value)
    {
        var text = TmapFormat.Utf8.GetBytes(value);
        if (text.Length > 512) throw new InvalidDataException("TMAP string exceeds 512 bytes.");
        var row = new byte[4 + text.Length]; TmapFormat.Put16(row, 0, text.Length); text.CopyTo(row, 4); return Add(row);
    }
    private void FlushPage()
    {
        if (_count == 0) return;
        TmapFormat.Put32(_page, 16, (uint)_count); TmapFormat.WritePage(_stream, _page);
        _page = TmapFormat.Page(_type, (ulong)_stream.Position, 0, _entrySize); _used = 64; _count = 0;
    }
    public void Dispose() { FlushPage(); _stream.Dispose(); }
}

internal sealed record TmapTreeEntry(byte[] Key, byte[] Value);
internal enum TmapKeyKind { Number, Id, Text }

/// <summary>Bulk builds one level at a time; only one page and one child reference are resident.</summary>
internal static class TmapTree
{
    public static ulong Build(string path, IEnumerable<TmapTreeEntry> entries, long count,
        TmapKeyKind kind, int valueBytes, ushort leafType, int maximumKeyBytes, CancellationToken token)
    {
        if (count == 0) { using var empty = File.Create(path); return 0; }
        var fixedKey = kind == TmapKeyKind.Number ? 8 : 16;
        var capacity = kind == TmapKeyKind.Text ? 4032 / (maximumKeyBytes + valueBytes + 6) : 4032 / (fixedKey + valueBytes);
        var innerCapacity = kind == TmapKeyKind.Text ? 4032 / (maximumKeyBytes + 14) : 4032 / (fixedKey + 8);
        if (capacity < 3 || innerCapacity < 3) throw new InvalidDataException("Tree key too large.");
        using var output = File.Create(path);
        output.Write(new byte[4096]);
        var refsA = path + ".refs-a"; var refsB = path + ".refs-b";
        try
        {
            var nodes = WriteLevel(output, entries, count, capacity, kind, valueBytes, leafType, 0, refsA, token);
            var level = 0; var current = refsA; var next = refsB;
            while (nodes > 1)
            {
                level++;
                nodes = WriteLevel(output, ReadReferences(current), nodes, innerCapacity, kind, 8,
                    kind == TmapKeyKind.Number ? (ushort)2 : kind == TmapKeyKind.Id ? (ushort)4 : (ushort)6,
                    level, next, token);
                (current, next) = (next, current);
            }
            var root = TmapFormat.U64(ReadReferences(current).Single().Value, 0);
            var descriptor = TmapFormat.Page(1, 0, 0);
            TmapFormat.Put16(descriptor, 64, (int)kind + 1); TmapFormat.Put16(descriptor, 66, leafType);
            TmapFormat.Put16(descriptor, 68, maximumKeyBytes); TmapFormat.Put64(descriptor, 72, (ulong)count);
            TmapFormat.Put64(descriptor, 80, root); TmapFormat.Put32(descriptor, 88, (uint)(level + 1));
            output.Position = 0; TmapFormat.WritePage(output, descriptor); return root;
        }
        finally { File.Delete(refsA); File.Delete(refsB); }
    }

    private static long WriteLevel(Stream output, IEnumerable<TmapTreeEntry> entries, long count, int capacity,
        TmapKeyKind kind, int valueBytes, ushort type, int level, string refs, CancellationToken token)
    {
        using var source = entries.GetEnumerator(); using var referenceFile = File.Create(refs); using var writer = new BinaryWriter(referenceFile);
        var pages = (count + capacity - 1) / capacity;
        var basic = count / pages; var remainder = count % pages;
        byte[]? previous = null;
        for (long i = 0; i < pages; i++)
        {
            token.ThrowIfCancellationRequested();
            var size = (int)(basic + (i < remainder ? 1 : 0)); var offset = (ulong)output.Position;
            var fixedSize = kind == TmapKeyKind.Text ? 0 : (kind == TmapKeyKind.Number ? 8 : 16) + valueBytes;
            var page = TmapFormat.Page(type, offset, size, fixedSize, level, level == 0 && i + 1 < pages ? offset + 4096 : 0);
            var used = 64; byte[] last = [];
            for (var j = 0; j < size; j++)
            {
                if (!source.MoveNext()) throw new InvalidDataException("Index count exceeds input.");
                var entry = source.Current;
                if (entry.Value.Length != valueBytes || (previous is not null && previous.AsSpan().SequenceCompareTo(entry.Key) >= 0))
                    throw new InvalidDataException("Index keys must be strictly increasing.");
                if (kind == TmapKeyKind.Text)
                {
                    TmapFormat.Put16(page, 4096 - 2 * (j + 1), used);
                    TmapFormat.Put16(page, used, entry.Key.Length); TmapFormat.Put16(page, used + 2, valueBytes); used += 4;
                    entry.Key.CopyTo(page, used); used += entry.Key.Length;
                }
                else if (kind == TmapKeyKind.Number)
                {
                    var numericKey = TmapFormat.FromOrderedKey(entry.Key);
                    TmapFormat.Put64(page, used, numericKey); used += 8;
                    if (j == 0) TmapFormat.Put64(page, 32, numericKey);
                    TmapFormat.Put64(page, 40, numericKey);
                }
                else { entry.Key.CopyTo(page, used); used += 16; }
                entry.Value.CopyTo(page, used); used += entry.Value.Length;
                previous = last = entry.Key;
            }
            TmapFormat.WritePage(output, page);
            writer.Write((ushort)last.Length); writer.Write(last); writer.Write(offset);
        }
        if (source.MoveNext()) throw new InvalidDataException("Index input exceeds count.");
        return pages;
    }
    private static IEnumerable<TmapTreeEntry> ReadReferences(string path)
    {
        using var s = File.OpenRead(path); using var r = new BinaryReader(s);
        while (s.Position < s.Length)
        { var key = r.ReadBytes(r.ReadUInt16()); var value = new byte[8]; TmapFormat.Put64(value, 0, r.ReadUInt64()); yield return new(key, value); }
    }
}
