namespace TrailMateCenter.Maps.Tmap;

/// <summary>Materializes display rows; global POI/name/search sections are preserved byte-for-byte.</summary>
public static class TmapFastLabels
{
    public sealed record Result(string Path, long OriginalBytes, long Bytes, long Tiles, ulong Labels, bool Changed);
    /// <summary>Copies only appended display sections to a matching older pack, e.g. over USB MSC.</summary>
    public static Result Install(string prepared, string destination, CancellationToken token = default)
    {
        prepared = System.IO.Path.GetFullPath(prepared); destination = System.IO.Path.GetFullPath(destination);
        if (prepared.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and destination must differ.");
        using var lease = new FileStream(destination + ".fast-labels-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var journal = destination + ".fast-labels-backup";
        if (File.Exists(journal)) Restore(destination, journal);
        using (var check = new TmapReader(prepared))
            if (!check.HasFastLabels) throw new InvalidDataException("Source lacks prepared display sections.");
        using var source = File.OpenRead(prepared);
        var before = new byte[8192]; var after = new byte[8192]; source.ReadExactly(after);
        long original;
        using (var target = File.OpenRead(destination)) { original = target.Length; target.ReadExactly(before); }
        using (var check = new TmapReader(destination))
            if (check.HasFastLabels)
            {
                if (original != source.Length || !before.AsSpan().SequenceEqual(after))
                    throw new InvalidDataException("Destination already has different prepared display sections.");
                return new(destination, original, original, 0, 0, false);
            }
        var oldCount = TmapFormat.U32(before, 48); var newCount = TmapFormat.U32(after, 48);
        if (oldCount > 62 || newCount != oldCount + 2 || TmapFormat.U64(before, 32) != (ulong)original ||
            TmapFormat.U64(after, 104) != TmapFormat.U64(before, 104) + 1 ||
            !before.AsSpan(4096, (int)oldCount * 64).SequenceEqual(after.AsSpan(4096, (int)oldCount * 64)))
            throw new InvalidDataException("Destination is not the matching base version of the prepared pack.");
        // All immutable header fields must match. Build ID, size, directory
        // count, revision and CRCs are the only fields changed by this upgrade.
        var a = before.AsSpan(0, 256).ToArray(); var b = after.AsSpan(0, 256).ToArray();
        foreach (var (offset, length) in new[] { (32, 8), (48, 4), (72, 16), (104, 8), (160, 8) })
        { a.AsSpan(offset, length).Clear(); b.AsSpan(offset, length).Clear(); }
        if (!a.AsSpan().SequenceEqual(b)) throw new InvalidDataException("Map identity or coverage differs.");
        var tail = checked((long)TmapFormat.U64(after, 4096 + (int)oldCount * 64 + 16));
        if (tail != (original + 4095) / 4096 * 4096 || source.Length != (long)TmapFormat.U64(after, 32))
            throw new InvalidDataException("Invalid display delta bounds.");
        var backup = new byte[8244]; "TMAPUPG1"u8.CopyTo(backup); TmapFormat.Put64(backup, 8, (ulong)original);
        System.Security.Cryptography.SHA256.HashData(TmapFormat.Utf8.GetBytes(destination)).CopyTo(backup, 16);
        before.CopyTo(backup, 48); TmapFormat.Put32(backup, 8240, TmapFormat.Crc32C(backup.AsSpan(0, 8240)));
        using (var output = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(backup); output.Flush(true); }
        var mutated = false;
        try
        {
            using (var target = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var current = new byte[8192]; target.ReadExactly(current);
                if (target.Length != original || !current.AsSpan().SequenceEqual(before)) throw new IOException("Destination changed during preparation.");
                mutated = true;
                target.Position = original; target.Write(new byte[checked((int)(tail - original))]); source.Position = tail;
                var buffer = new byte[65536]; int n;
                while ((n = source.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); target.Write(buffer, 0, n); }
                target.Flush(true); // Persist payload before making the new directory visible.
                target.Position = 4096; target.Write(after.AsSpan(4096, 4096)); target.Position = 0; target.Write(after.AsSpan(0, 4096)); target.Flush(true);
            }
            using (var check = new TmapReader(destination))
                foreach (var tile in check.AnnotationTiles()) { token.ThrowIfCancellationRequested(); _ = check.ReadDisplayAnnotations(tile.Zoom, tile.X, tile.Y); }
            File.Delete(journal);
            return new(destination, original, new FileInfo(destination).Length,
                checked((long)TmapFormat.U64(after, 4096 + (int)(oldCount + 1) * 64 + 32)),
                TmapFormat.U64(after, 4096 + (int)oldCount * 64 + 32), true);
        }
        catch { if (mutated) Restore(destination, journal); else File.Delete(journal); throw; }
    }
    // Space-saving publication for an existing generated pack. Only its first
    // 8 KiB and original length need rollback; all added sections are at EOF.
    public static Result Upgrade(string path, CancellationToken token = default, long maximumBytes = long.MaxValue)
    {
        path = System.IO.Path.GetFullPath(path);
        var journal = path + ".fast-labels-backup";
        using var lease = new FileStream(path + ".fast-labels-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (File.Exists(journal)) Restore(path, journal);
        using (var reader = new TmapReader(path))
            if (reader.HasFastLabels) return new(path, new FileInfo(path).Length, new FileInfo(path).Length, 0, 0, false);
        var backup = new byte[8244]; "TMAPUPG1"u8.CopyTo(backup);
        var identity = System.Security.Cryptography.SHA256.HashData(TmapFormat.Utf8.GetBytes(path)); identity.CopyTo(backup, 16);
        using (var input = File.OpenRead(path))
        { TmapFormat.Put64(backup, 8, (ulong)input.Length); input.ReadExactly(backup.AsSpan(48, 8192)); }
        TmapFormat.Put32(backup, 8240, TmapFormat.Crc32C(backup.AsSpan(0, 8240)));
        using (var output = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(backup); output.Flush(true); }
        var mutated = false;
        try
        {
            var result = Append(path, token, maximumBytes, () => mutated = true, backup.AsSpan(48, 256).ToArray());
            File.Delete(journal); return result;
        }
        catch
        {
            if (mutated) Restore(path, journal); else File.Delete(journal);
            throw;
        }
    }
    private static void Restore(string path, string journal)
    {
        var backup = File.ReadAllBytes(journal);
        if (backup.Length != 8244 || !backup.AsSpan(0, 8).SequenceEqual("TMAPUPG1"u8) ||
            TmapFormat.Crc32C(backup.AsSpan(0, 8240)) != TmapFormat.U32(backup, 8240) ||
            !backup.AsSpan(16, 32).SequenceEqual(System.Security.Cryptography.SHA256.HashData(TmapFormat.Utf8.GetBytes(path))))
            throw new InvalidDataException("Invalid fast-label rollback journal.");
        var length = checked((long)TmapFormat.U64(backup, 8));
        if (length < 8192) throw new InvalidDataException("Invalid original map size.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (file.Length < length) throw new InvalidDataException("Map is shorter than its rollback snapshot.");
        file.SetLength(length); file.Position = 0; file.Write(backup.AsSpan(48, 8192)); file.Flush(true);
        File.Delete(journal);
    }

    // Publish a new file only after its display sections have been built. Source
    // can equal output: the original remains intact until the final file move.
    public static Result Create(string source, string output, CancellationToken token = default, long maximumBytes = long.MaxValue)
    {
        source = System.IO.Path.GetFullPath(source); output = System.IO.Path.GetFullPath(output);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(source))
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024]; int n;
                while ((n = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); target.Write(buffer, 0, n); }
            }
            var result = Append(temporary, token, maximumBytes);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: true);
            return result with { Path = output };
        }
        finally { File.Delete(temporary); }
    }

    // Used only on an unpublished builder temporary or a private clone.
    internal static Result Append(string path, CancellationToken token, long maximumBytes, Action? mutating = null, byte[]? expectedHeader = null, bool updateIdentity = true)
    {
        var work = path + ".labels-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(work);
        var rowsPath = System.IO.Path.Combine(work, "rows");
        var entriesPath = System.IO.Path.Combine(work, "entries");
        var indexPath = System.IO.Path.Combine(work, "index");
        long tiles = 0; ulong labels = 0;
        var originalBytes = new FileInfo(path).Length;
        try
        {
            using (var reader = new TmapReader(path))
            {
                if (reader.HasFastLabels) return new(path, originalBytes, originalBytes, 0, 0, false);
                using var rows = new TmapTable(rowsPath, 9, 176);
                using var entries = new BinaryWriter(File.Create(entriesPath));
                foreach (var tile in reader.AnnotationTiles())
                {
                    token.ThrowIfCancellationRequested();
                    var annotations = reader.ReadAnnotations(tile.Zoom, tile.X, tile.Y);
                    if (annotations.Count == 0) continue;
                    entries.Write(TmapFormat.TileKey(tile.Zoom, tile.X, tile.Y));
                    entries.Write(rows.Count + 1); entries.Write((uint)annotations.Count);
                    foreach (var annotation in annotations) rows.Add(Encode(annotation));
                    tiles++; labels += (uint)annotations.Count;
                    if (tiles % 4096 == 0) Console.WriteLine($"fast-labels tiles={tiles} labels={labels}");
                }
            }
            var root = TmapTree.Build(indexPath, ReadEntries(entriesPath), tiles, TmapKeyKind.Number, 16, 3, 8, token);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var header = new byte[256]; file.ReadExactly(header);
                if (file.Length != originalBytes || expectedHeader is not null && !header.AsSpan().SequenceEqual(expectedHeader))
                    throw new IOException("Map changed during label generation.");
                var oldCount = TmapFormat.U32(header, 48);
                if (oldCount > 62) throw new InvalidDataException("Fast labels exceed the firmware's 64-section limit.");
                var directory = new byte[(oldCount + 2) * 64]; file.Position = 4096;
                file.ReadExactly(directory.AsSpan(0, checked((int)oldCount * 64)));
                mutating?.Invoke();
                file.Position = file.Length;
                AppendSection(file, directory.AsSpan((int)oldCount * 64, 64), 43, rowsPath, labels, 0, maximumBytes, token);
                AppendSection(file, directory.AsSpan((int)(oldCount + 1) * 64, 64), 44, indexPath, (ulong)tiles, root, maximumBytes, token);
                TmapFormat.Put32(header, 48, oldCount + 2);
                TmapFormat.Put64(header, 32, (ulong)file.Length);
                if (updateIdentity)
                {
                    Guid.NewGuid().TryWriteBytes(header.AsSpan(72, 16));
                    TmapFormat.Put64(header, 104, checked(TmapFormat.U64(header, 104) + 1));
                }
                TmapFormat.Put32(header, 164, TmapFormat.Crc32C(directory));
                TmapFormat.Put32(header, 160, 0); TmapFormat.Put32(header, 160, TmapFormat.Crc32C(header));
                file.Position = 4096; file.Write(directory); file.Position = 0; file.Write(header); file.Flush(true);
            }
            // Read every display list through the reference API, and every new
            // page through its CRC path, before publishing the generated file.
            using (var reader = new TmapReader(path))
            {
                ulong seen = 0;
                foreach (var tile in reader.AnnotationTiles())
                { token.ThrowIfCancellationRequested(); seen += (uint)reader.ReadDisplayAnnotations(tile.Zoom, tile.X, tile.Y).Count; }
                if (seen != labels) throw new InvalidDataException("Fast label count mismatch.");
            }
            return new(path, originalBytes, new FileInfo(path).Length, tiles, labels, true);
        }
        finally
        {
            File.Delete(rowsPath); File.Delete(entriesPath); File.Delete(indexPath);
            Directory.Delete(work); // Private GUID directory, no recursive deletion.
        }
    }

    private static IEnumerable<TmapTreeEntry> ReadEntries(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            var key = reader.ReadUInt64(); var first = reader.ReadUInt64(); var count = reader.ReadUInt32();
            var value = new byte[16]; TmapFormat.Put64(value, 0, first); TmapFormat.Put32(value, 8, count);
            yield return new(TmapFormat.OrderedKey(key), value);
        }
    }
    private static byte[] Encode(TmapAnnotation annotation)
    {
        var row = new byte[176]; annotation.Poi.StableId.CopyTo(row, 0);
        TmapFormat.PutI32(row, 16, checked((int)Math.Round(annotation.Latitude * 1e7)));
        TmapFormat.PutI32(row, 20, checked((int)Math.Round(annotation.Longitude * 1e7)));
        TmapFormat.Put16(row, 24, annotation.Kind); TmapFormat.Put16(row, 26, annotation.Priority);
        if (annotation.Path.Count > 8) throw new InvalidDataException("Too many label path points.");
        row[28] = (byte)annotation.Path.Count; var used = 0;
        foreach (var rune in annotation.Poi.Name.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > 79) break;
            used += rune.EncodeToUtf8(row.AsSpan(32 + used));
        }
        row[29] = (byte)used;
        for (var i = 0; i < annotation.Path.Count; i++)
        {
            TmapFormat.PutI32(row, 112 + i * 8, checked((int)Math.Round(annotation.Path[i].Latitude * 1e7)));
            TmapFormat.PutI32(row, 116 + i * 8, checked((int)Math.Round(annotation.Path[i].Longitude * 1e7)));
        }
        return row;
    }
    private static void AppendSection(FileStream file, Span<byte> directory, uint id, string path, ulong count, ulong root,
        long maximumBytes, CancellationToken token)
    {
        TmapFormat.Align(file); var offset = (ulong)file.Position;
        using var input = File.OpenRead(path);
        if (file.Position + input.Length > maximumBytes) throw new IOException("Fast labels exceed the output budget.");
        var buffer = new byte[65536]; int n;
        while ((n = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); file.Write(buffer, 0, n); }
        TmapFormat.Put32(directory, 0, id); TmapFormat.Put16(directory, 4, 1); TmapFormat.Put16(directory, 6, 2); // Optional, paged.
        TmapFormat.Put32(directory, 8, id); TmapFormat.Put64(directory, 16, offset); TmapFormat.Put64(directory, 24, (ulong)input.Length);
        TmapFormat.Put64(directory, 32, count); TmapFormat.Put64(directory, 40, root);
    }
}
