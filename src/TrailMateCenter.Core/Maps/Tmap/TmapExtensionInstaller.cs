using System.Security.Cryptography;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Installs a prepared metadata tail without retransmitting native raster pixels.</summary>
public static class TmapExtensionInstaller
{
    public sealed record Result(string Path, long AppendedBytes, long FinalBytes, bool Changed);

    public static Result Install(string prepared, string destination, CancellationToken token = default, string? fontSdRoot = null)
    {
        prepared = Path.GetFullPath(prepared); destination = Path.GetFullPath(destination);
        if (prepared.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Prepared and destination maps must differ.");
        using var lease = new FileStream(destination + ".extensions-lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var journal = destination + ".extensions-backup";
        if (File.Exists(journal)) TmapExtensionPublisher.Restore(destination, journal);
        using (var reader = new TmapReader(prepared)) { _ = reader.PoiCount; }
        using (var reader = new TmapReader(destination)) { _ = reader.PoiCount; }
        using var source = File.OpenRead(prepared);
        var after = new byte[8192]; source.ReadExactly(after);
        var before = new byte[8192]; long original;
        using (var target = File.OpenRead(destination)) { original = target.Length; target.ReadExactly(before); }
        if (original == source.Length && before.AsSpan().SequenceEqual(after))
        {
            TmapFontInstaller.EnsureInstalled(prepared, destination, fontSdRoot, token);
            return new(destination, 0, original, false);
        }

        // This small receipt identifies the exact base used by append-only
        // preparation. A package with a similar name or bounds is not enough.
        var receipt = File.ReadAllBytes(prepared + ".extensions-base");
        if (receipt.Length != 8244 || !receipt.AsSpan(0, 8).SequenceEqual("TMAPEXT1"u8) ||
            TmapFormat.U32(receipt, 8240) != TmapFormat.Crc32C(receipt.AsSpan(0, 8240)) ||
            TmapFormat.U64(receipt, 8) != (ulong)original ||
            !receipt.AsSpan(48, 8192).SequenceEqual(before) || source.Length <= original)
            throw new InvalidDataException("Destination does not match the prepared map's exact base version.");
        ValidateDirectory(before, after, original);
        TmapFontInstaller.EnsureInstalled(prepared, destination, fontSdRoot, token);
        var backup = receipt.ToArray();
        SHA256.HashData(TmapFormat.Utf8.GetBytes(destination)).CopyTo(backup, 16);
        TmapFormat.Put32(backup, 8240, TmapFormat.Crc32C(backup.AsSpan(0, 8240)));
        using (var output = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write(backup); output.Flush(true); }
        var mutated = false;
        try
        {
            using (var target = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var current = new byte[8192]; target.ReadExactly(current);
                if (target.Length != original || !current.AsSpan().SequenceEqual(before))
                    throw new IOException("Destination changed during installation preparation.");
                var buffer = new byte[65536];
                using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                source.Position = original; target.Position = original;
                int length;
                while ((length = source.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested(); mutated = true;
                    expected.AppendData(buffer, 0, length); target.Write(buffer, 0, length);
                }
                target.Flush(true);
                using var actual = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                target.Position = original;
                while ((length = target.Read(buffer)) != 0)
                { token.ThrowIfCancellationRequested(); actual.AppendData(buffer, 0, length); }
                if (target.Length != source.Length || !actual.GetHashAndReset().AsSpan().SequenceEqual(expected.GetHashAndReset()))
                    throw new IOException("Installed metadata tail failed read-back verification.");
                token.ThrowIfCancellationRequested();
                // Make the new directory visible only after its payload is durable.
                target.Position = 4096; target.Write(after.AsSpan(4096, 4096));
                target.Position = 0; target.Write(after.AsSpan(0, 4096)); target.Flush(true);
            }
            using (var reader = new TmapReader(destination)) { _ = reader.PoiCount; }
            File.Delete(journal);
            return new(destination, source.Length - original, source.Length, true);
        }
        catch
        {
            if (mutated) TmapExtensionPublisher.Restore(destination, journal);
            else File.Delete(journal);
            throw;
        }
    }

    private static void ValidateDirectory(byte[] before, byte[] after, long original)
    {
        var oldCount = TmapFormat.U32(before, 48); var newCount = TmapFormat.U32(after, 48);
        if (oldCount > 64 || newCount > 64 ||
            !before.AsSpan(56, 16).SequenceEqual(after.AsSpan(56, 16)))
            throw new InvalidDataException("Invalid extension package identity or directory size.");
        var oldRows = new Dictionary<uint, byte[]>();
        for (var i = 0; i < oldCount; i++)
        {
            var row = before.AsSpan(4096 + i * 64, 64).ToArray();
            oldRows.Add(TmapFormat.U32(row, 8), row);
        }
        for (var i = 0; i < newCount; i++)
        {
            var row = after.AsSpan(4096 + i * 64, 64); var id = TmapFormat.U32(row, 8);
            if (oldRows.Remove(id, out var previous) && row.SequenceEqual(previous)) continue;
            if (id is < 50 or > 54 || TmapFormat.U64(row, 16) < (ulong)original)
                throw new InvalidDataException("Incremental installation may only replace appended administrative and font sections.");
        }
        if (oldRows.Keys.Any(id => id is < 50 or > 54))
            throw new InvalidDataException("A required base section was removed from the prepared package.");
    }
}
