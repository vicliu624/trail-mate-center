namespace TrailMateCenter.Maps.Tmap;

public static class TmapLayout
{
    public static string Root(string destination)
    {
        destination = Path.GetFullPath(destination);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(destination));
        if (name.Equals("tmap", StringComparison.OrdinalIgnoreCase)) return destination;
        return name.Equals("maps", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(destination, "tmap") : Path.Combine(destination, "maps", "tmap");
    }
    public static string FileName(string filename, string style)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        foreach (var existing in new[] { "osm", "terrain", "satellite" })
        {
            var at = stem.IndexOf("-" + existing + "-", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return stem[..at] + "-" + style + stem[(at + existing.Length + 1)..] + ".tmap";
            if (stem.EndsWith("-" + existing, StringComparison.OrdinalIgnoreCase)) return stem[..^(existing.Length + 1)] + "-" + style + ".tmap";
        }
        var zoom = stem.LastIndexOf("-z", StringComparison.OrdinalIgnoreCase);
        return zoom >= 0 && zoom + 2 < stem.Length && char.IsAsciiDigit(stem[zoom + 2])
            ? stem[..zoom] + "-" + style + stem[zoom..] + ".tmap" : stem + "-" + style + ".tmap";
    }
}
