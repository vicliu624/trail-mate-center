using System.Text.Json;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Small path registry, never a copy of map data or indexes.</summary>
public static class TmapPackageRegistry
{
    private static readonly object Gate = new();
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter", "maps", "tmap");
    private static string Index => Path.Combine(Root, "packages.json");
    public static IReadOnlyList<string> Paths()
    {
        lock (Gate)
        {
            var paths = File.Exists(Index) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(Index)) ?? [] : [];
            var found = new List<string>(paths);
            foreach (var directory in new[] { Root, Path.Combine(Root, "osm"), Path.Combine(Root, "terrain"), Path.Combine(Root, "satellite") })
                if (Directory.Exists(directory)) found.AddRange(Directory.EnumerateFiles(directory, "*.tmap"));
            return found.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
        }
    }
    public static void Register(string path)
    {
        path = Path.GetFullPath(path);
        using (var reader = new TmapReader(path)) { }
        lock (Gate)
        {
            var paths = Paths().ToList(); paths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); paths.Add(path);
            if (paths.Count > 256) throw new InvalidOperationException("At most 256 TMAP packages can be registered.");
            Directory.CreateDirectory(Root); var temporary = Index + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(paths)); File.Move(temporary, Index, true); }
            finally { File.Delete(temporary); }
        }
    }
}
