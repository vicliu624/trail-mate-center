using System.Security.Cryptography;

namespace TrailMateCenter.Maps.Tmap;

internal static class TmapFontInstaller
{
    public static void EnsureInstalled(string prepared, string destination, string? sourceSdRoot, CancellationToken token)
    {
        using var reader = new TmapReader(prepared);
        var fonts = reader.RequiredFonts();
        var targetRoot = Path.Combine(TmapLayout.FontRootForPackage(destination), "trailmate", "packs", "fonts");
        foreach (var font in fonts)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(targetRoot, font.Id);
            if (Directory.Exists(target)) { Verify(target, font); continue; }
            if (string.IsNullOrWhiteSpace(sourceSdRoot))
                throw new FileNotFoundException($"Install required font {font.Id} or provide its staging SD root.");
            var source = Path.Combine(Path.GetFullPath(sourceSdRoot), "trailmate", "packs", "fonts", font.Id);
            Verify(source, font);
            Directory.CreateDirectory(targetRoot);
            var temporary = Path.Combine(targetRoot, ".install-" + font.Id + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var files = Directory.EnumerateFiles(source).OrderBy(p => Path.GetFileName(p) == "manifest.ini" ? 1 : 0).ToArray();
                if (files.Length > 32 || Directory.EnumerateDirectories(source).Any())
                    throw new InvalidDataException("Unexpected map font resource contents.");
                var buffer = new byte[65536];
                foreach (var path in files)
                {
                    token.ThrowIfCancellationRequested();
                    using var input = File.OpenRead(path);
                    using var output = new FileStream(Path.Combine(temporary, Path.GetFileName(path)), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    int length;
                    while ((length = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, length); }
                    output.Flush(true);
                }
                Verify(temporary, font); token.ThrowIfCancellationRequested();
                Directory.Move(temporary, target);
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    foreach (var path in Directory.EnumerateFiles(temporary)) File.Delete(path);
                    Directory.Delete(temporary);
                }
            }
        }
    }

    private static void Verify(string directory, TmapFontDependency font)
    {
        using var input = File.OpenRead(Path.Combine(directory, "font.bin"));
        if ((ulong)input.Length != font.FileBytes || !SHA256.HashData(input).AsSpan().SequenceEqual(font.Sha256))
            throw new InvalidDataException($"Map font {font.Id} has an invalid size or SHA-256.");
        var fields = File.ReadAllLines(Path.Combine(directory, "manifest.ini"))
            .Where(line => line.Contains('='))
            .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.Ordinal);
        if (!fields.TryGetValue("kind", out var kind) || kind != "font" ||
            !fields.TryGetValue("id", out var id) || id != font.Id ||
            !fields.TryGetValue("usage", out var usage) || usage != "content" ||
            !fields.TryGetValue("source", out var source) || source != "binfont" ||
            !fields.TryGetValue("file", out var file) || file != "font.bin" ||
            !fields.TryGetValue("ranges", out var ranges) || ranges != "ranges.txt" ||
            !fields.TryGetValue("estimated_ram_bytes", out var ram) || !uint.TryParse(ram, out var bytes) || bytes != font.EstimatedRamBytes ||
            !File.Exists(Path.Combine(directory, "ranges.txt")))
            throw new InvalidDataException($"Invalid manifest or coverage file for map font {font.Id}.");
    }
}
