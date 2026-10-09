using System.Security.Cryptography;

namespace TrailMateCenter.Maps.Tmap;

internal sealed record TmapExtension(uint Id, string Path, ulong Count, bool Paged = true);

/// <summary>Append-only publication with an 8 KiB rollback journal; no raster copy.</summary>
internal static class TmapExtensionPublisher
{
    public static void Publish(string path, IReadOnlyList<TmapExtension> sections, CancellationToken token,
        long maximumBytes = long.MaxValue, bool updateIdentity = true)
    {
        path = System.IO.Path.GetFullPath(path);
        if (sections.Count == 0 || sections.Select(s => s.Id).Distinct().Count() != sections.Count)
            throw new ArgumentException("Distinct extension sections are required.");
        using var lease = new FileStream(path + ".extensions-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
        var journal = path + ".extensions-backup";
        if (File.Exists(journal)) Restore(path, journal);
        using (var reader = new TmapReader(path)) { _ = reader.PoiCount; }
        var before = new byte[8192]; long original;
        using (var input = File.OpenRead(path)) { original = input.Length; input.ReadExactly(before); }
        var count = TmapFormat.U32(before, 48);
        if (count > 64) throw new InvalidDataException("Map directory exceeds the device section limit.");
        var directory = new List<byte[]>();
        for (var i = 0; i < count; i++)
        {
            var row = before.AsSpan(4096 + i * 64, 64);
            var id = TmapFormat.U32(row, 8);
            if (!sections.Any(s => s.Id == id)) directory.Add(row.ToArray());
        }
        if (directory.Count + sections.Count > 64) throw new InvalidDataException("Extensions exceed the device's 64-section limit.");
        long end = original;
        foreach (var section in sections)
        {
            var length = new FileInfo(section.Path).Length;
            if (section.Paged && length % 4096 != 0) throw new InvalidDataException("Extension pages must be aligned.");
            end = checked((end + 4095) / 4096 * 4096 + length);
        }
        if (end > maximumBytes) throw new IOException("Extended TMAP exceeds its output budget.");
        var backup = new byte[8244]; "TMAPEXT1"u8.CopyTo(backup);
        TmapFormat.Put64(backup, 8, (ulong)original);
        SHA256.HashData(TmapFormat.Utf8.GetBytes(path)).CopyTo(backup, 16);
        before.CopyTo(backup, 48); TmapFormat.Put32(backup, 8240, TmapFormat.Crc32C(backup.AsSpan(0, 8240)));
        using (var output = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write(backup); output.Flush(true); }
        var mutated = false;
        try
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var current = new byte[8192]; file.ReadExactly(current);
                if (file.Length != original || !current.AsSpan().SequenceEqual(before)) throw new IOException("Map changed during extension preparation.");
                file.Position = original;
                var buffer = new byte[65536];
                foreach (var section in sections)
                {
                    token.ThrowIfCancellationRequested(); mutated = true; TmapFormat.Align(file);
                    var row = new byte[64]; var offset = (ulong)file.Position;
                    using var input = File.OpenRead(section.Path); int n;
                    while ((n = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); file.Write(buffer, 0, n); }
                    TmapFormat.Put32(row, 0, section.Id); TmapFormat.Put16(row, 4, 1);
                    TmapFormat.Put16(row, 6, section.Paged ? 2 : 0); // Optional for v1 readers.
                    TmapFormat.Put32(row, 8, section.Id); TmapFormat.Put64(row, 16, offset);
                    TmapFormat.Put64(row, 24, (ulong)input.Length); TmapFormat.Put64(row, 32, section.Count);
                    directory.Add(row);
                }
                file.Flush(true);
                var header = before.AsSpan(0, 256).ToArray(); var bytes = directory.SelectMany(b => b).ToArray();
                TmapFormat.Put64(header, 32, (ulong)file.Length); TmapFormat.Put32(header, 48, (uint)directory.Count);
                if (updateIdentity)
                {
                    Guid.NewGuid().TryWriteBytes(header.AsSpan(72, 16));
                    TmapFormat.Put64(header, 104, checked(TmapFormat.U64(header, 104) + 1));
                }
                TmapFormat.Put32(header, 164, TmapFormat.Crc32C(bytes));
                TmapFormat.Put32(header, 160, 0); TmapFormat.Put32(header, 160, TmapFormat.Crc32C(header));
                file.Position = 4096; file.Write(bytes); file.Position = 0; file.Write(header); file.Flush(true);
            }
            using (var check = new TmapReader(path)) { _ = check.PoiCount; }
            // Keep the exact prior header for a later USB metadata-only install.
            if (!File.Exists(path + ".extensions-base")) File.WriteAllBytes(path + ".extensions-base", backup);
            File.Delete(journal);
        }
        catch { if (mutated) Restore(path, journal); else File.Delete(journal); throw; }
    }

    internal static void Restore(string path, string journal)
    {
        var backup = File.ReadAllBytes(journal);
        if (backup.Length != 8244 || !backup.AsSpan(0, 8).SequenceEqual("TMAPEXT1"u8) ||
            !backup.AsSpan(16, 32).SequenceEqual(SHA256.HashData(TmapFormat.Utf8.GetBytes(path))) ||
            TmapFormat.U32(backup, 8240) != TmapFormat.Crc32C(backup.AsSpan(0, 8240)))
            throw new InvalidDataException("Invalid TMAP extension rollback journal.");
        var length = checked((long)TmapFormat.U64(backup, 8));
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (length < 8192 || file.Length < length) throw new InvalidDataException("Invalid original map length.");
        file.SetLength(length); file.Position = 0; file.Write(backup.AsSpan(48, 8192)); file.Flush(true);
        File.Delete(journal);
    }
}
