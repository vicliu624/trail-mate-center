using System.Text.Json;
using SkiaSharp;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;

// Natural Earth v5.1.2 overview. Source IDs never impersonate OSM IDs.
internal static class WorldOverview
{
    private sealed record Shape(List<SKPoint[]> Rings, bool Closed, int Layer, double MinimumZoom);
    private sealed record Drawing(SKPath Path, int Layer);
    private static readonly string[] Themes = ["land", "lakes", "rivers_lake_centerlines", "admin_0_boundary_lines_land"];
    private const double LatitudeLimit = 85.0511287798066;
    private static SKPoint Project(double lon, double lat)
    {
        lat = Math.Clamp(lat, -LatitudeLimit, LatitudeLimit);
        var radians = lat * Math.PI / 180;
        return new((float)((lon + 180) / 360), (float)((1 - Math.Log(Math.Tan(Math.PI / 4 + radians / 2)) / Math.PI) / 2));
    }
    private static double Number(JsonElement p, string key, double fallback = 0) =>
        p.TryGetProperty(key, out var v) && v.TryGetDouble(out var d) ? d : fallback;

    public static TmapBuildResult Build(string sourceDirectory, string output, CancellationToken token)
    {
        output = Path.GetFullPath(output); var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        if (new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace < 7L * 1024 * 1024 * 1024)
            throw new IOException("World build requires at least 7 GiB free for staging and publication.");
        var shapes = new List<Shape>();
        for (var layer = 0; layer < Themes.Length; layer++)
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(sourceDirectory, $"ne_10m_{Themes[layer]}.geojson")));
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                token.ThrowIfCancellationRequested(); var p = f.GetProperty("properties");
                var minimum = Number(p, "min_zoom", layer == 2 ? 3 : 0);
                if (minimum > 7) continue;
                var g = f.GetProperty("geometry"); var coords = g.GetProperty("coordinates");
                SKPoint[] Line(JsonElement a) => a.EnumerateArray().Select(c => Project(c[0].GetDouble(), c[1].GetDouble())).ToArray();
                void Polygon(JsonElement a) => shapes.Add(new(a.EnumerateArray().Select(Line).ToList(), true, layer, minimum));
                switch (g.GetProperty("type").GetString())
                {
                    case "Polygon": Polygon(coords); break;
                    case "MultiPolygon": foreach (var a in coords.EnumerateArray()) Polygon(a); break;
                    case "LineString": shapes.Add(new([Line(coords)], false, layer, minimum)); break;
                    case "MultiLineString": foreach (var a in coords.EnumerateArray()) shapes.Add(new([Line(a)], false, layer, minimum)); break;
                }
            }
            Console.Error.WriteLine($"source {Themes[layer]}: {shapes.Count} cumulative shapes");
        }
        using var builder = new TmapBuilder(parent, 4L * 1024 * 1024 * 1024, token);
        using (var cities = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(sourceDirectory, "ne_10m_populated_places.geojson"))))
        {
            var seen = new HashSet<ulong>();
            foreach (var f in cities.RootElement.GetProperty("features").EnumerateArray())
            {
                token.ThrowIfCancellationRequested(); var p = f.GetProperty("properties");
                var coordinate = f.GetProperty("geometry").GetProperty("coordinates");
                var lon = coordinate[0].GetDouble(); var lat = coordinate[1].GetDouble();
                if (lat < -LatitudeLimit || lat > LatitudeLimit) continue;
                var nativeId = checked((ulong)Number(p, "NE_ID"));
                if (nativeId == 0 || !seen.Add(nativeId)) throw new InvalidDataException("Missing or duplicate Natural Earth NE_ID.");
                var names = new List<string>();
                foreach (var key in new[] { "NAME_ZH", "NAME", "NAME_EN", "NAMEASCII", "NAMEPAR", "NAMEALT" })
                    if (p.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                        names.Add(value.GetString()!);
                foreach (var property in p.EnumerateObject())
                    if (property.Name.StartsWith("NAME_", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                        names.Add(property.Value.GetString()!);
                if (names.Count == 0) continue;
                var id = new byte[16]; TmapFormat.Put32(id, 0, 2); TmapFormat.Put16(id, 4, 1); TmapFormat.Put64(id, 8, nativeId);
                var minZoom = Math.Clamp((int)Math.Ceiling(Number(p, "MIN_ZOOM", 5)), 0, 7);
                var importance = Math.Clamp((int)(Math.Log10(Math.Max(1, Number(p, "POP_MAX"))) * 8000), 0, 65535);
                builder.AddPlace(new(id, lat, lon, "settlement", names, importance, 2, minZoom));
                var point = Project(lon, lat);
                for (var z = minZoom; z <= 7; z++)
                {
                    var side = 1 << z;
                    builder.AddAnnotation(z, Math.Clamp((int)(point.X * side), 0, side - 1), Math.Clamp((int)(point.Y * side), 0, side - 1), id, 2, importance, lat, lon);
                }
            }
            Console.Error.WriteLine($"staged cities: {seen.Count}");
        }
        using var land = new SKPaint { Color = SKColor.Parse("#ecebdc"), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var lake = new SKPaint { Color = SKColor.Parse("#a9d2e3"), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var river = new SKPaint { Color = SKColor.Parse("#8bbfd6"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.65f };
        using var boundary = new SKPaint { Color = SKColor.Parse("#aa9f8d"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.65f };
        SKPaint[] paints = [land, lake, river, boundary]; long completed = 0;
        for (var z = 0; z <= 7; z++)
        {
            var side = 1 << z; var pixels = side * 256f; var index = new STRtree<Drawing>(); var drawings = new List<Drawing>();
            try
            {
                foreach (var shape in shapes.Where(s => s.MinimumZoom <= z))
                {
                    var path = new SKPath { FillType = SKPathFillType.EvenOdd };
                    foreach (var ring in shape.Rings)
                    {
                        if (ring.Length == 0) continue;
                        var previous = new SKPoint(ring[0].X * pixels, ring[0].Y * pixels); path.MoveTo(previous);
                        for (var i = 1; i < ring.Length; i++)
                        {
                            var next = new SKPoint(ring[i].X * pixels, ring[i].Y * pixels);
                            if (i != ring.Length - 1 && Math.Abs(next.X - previous.X) + Math.Abs(next.Y - previous.Y) < 0.25f) continue;
                            path.LineTo(next); previous = next;
                        }
                        if (shape.Closed) path.Close();
                    }
                    var b = path.Bounds; var drawing = new Drawing(path, shape.Layer); drawings.Add(drawing);
                    index.Insert(new Envelope(b.Left, b.Right, b.Top, b.Bottom), drawing);
                }
                index.Build();
                for (var my = 0; my < side; my += 4)
                    for (var mx = 0; mx < side; mx += 4)
                    {
                        token.ThrowIfCancellationRequested(); var width = Math.Min(4, side - mx) * 256; var height = Math.Min(4, side - my) * 256;
                        // Two-pixel gutter keeps antialiased strokes continuous across metatile edges.
                        using var bitmap = new SKBitmap(width + 4, height + 4, SKColorType.Rgba8888, SKAlphaType.Opaque);
                        using (var canvas = new SKCanvas(bitmap))
                        {
                            canvas.Clear(lake.Color); canvas.Translate(2 - mx * 256, 2 - my * 256);
                            foreach (var d in index.Query(new Envelope(mx * 256 - 2, mx * 256 + width + 2, my * 256 - 2, my * 256 + height + 2)).OrderBy(d => d.Layer))
                                canvas.DrawPath(d.Path, paints[d.Layer]);
                            canvas.Flush();
                        }
                        for (var dy = 0; dy < height / 256; dy++)
                            for (var dx = 0; dx < width / 256; dx++)
                            {
                                using var tile = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque);
                                using (var canvas = new SKCanvas(tile))
                                    canvas.DrawBitmap(bitmap, new SKRect(2 + dx * 256, 2 + dy * 256, 258 + dx * 256, 258 + dy * 256), new SKRect(0, 0, 256, 256));
                                builder.AddTile(1, z, mx + dx, my + dy, TmapPackExporter.Pixels(tile, false)); completed++;
                            }
                        if (z == 2)
                        {
                            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                            using var preview = File.Create(Path.Combine(parent, "world-z2-preview.png")); png.SaveTo(preview);
                        }
                        Console.Error.WriteLine($"tiles {completed}/21845 z{z} metatile {mx},{my}");
                    }
            }
            finally { foreach (var drawing in drawings) drawing.Path.Dispose(); }
        }
        if (completed != 21845) throw new InvalidDataException("Incomplete world coverage.");
        Console.Error.WriteLine("Compiling POI/search/spatial/annotation indexes and validating output...");
        return builder.Complete(output, new() { Name = "世界概览 · Natural Earth · 0–7", Bounds = new(-180, -LatitudeLimit, 180, LatitudeLimit) }, new()
        {
            PackageKey = "world-natural-earth-z0-7",
            FileName = Path.GetFileName(output),
            Tier = TmapRegionTier.World,
            Series = "trail-mate-natural-earth-v1",
            Revision = 1,
            SourceNamespaceId = 2,
            SourceNamespaceUri = "https://www.naturalearthdata.com/ne-id",
            Attribution = "Natural Earth v5.1.2 — public domain; https://www.naturalearthdata.com/",
            SourceCoverage = "Global overview: Natural Earth 10m land, lakes, rivers, boundaries and populated places; not full OSM POIs or street navigation"
        });
    }
}
