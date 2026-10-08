using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;

internal static class OsmWorldOverview
{
    public static object Check(string path, CancellationToken token)
    {
        using var reader = new TmapReader(path); var counts = new Dictionary<int, int>(); long annotations = 0;
        using var mosaic = new SKBitmap(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(mosaic);
        for (var z = 0; z <= 7; z++)
        {
            counts[z] = 0; var side = 1 << z;
            for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    token.ThrowIfCancellationRequested(); var tile = reader.ReadTile(1, z, x, y) ?? throw new InvalidDataException($"Missing {z}/{x}/{y}.");
                    if (tile.Codec != 1 || tile.Pixels.Length != 131072) throw new InvalidDataException("Incorrect native pixel format.");
                    counts[z]++; var items = reader.ReadAnnotations(z, x, y); annotations += items.Count;
                    if (items.Count > 200) throw new InvalidDataException("Annotation quota exceeded.");
                    if (z == 2)
                    {
                        using var b = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque); var bytes = b.GetPixelSpan();
                        for (var i = 0; i < 65536; i++)
                        {
                            var color = tile.Pixels[i * 2] | tile.Pixels[i * 2 + 1] << 8;
                            var r = color >> 11 & 31; var g = color >> 5 & 63; var blue = color & 31;
                            bytes[i * 4] = (byte)((r << 3) | (r >> 2)); bytes[i * 4 + 1] = (byte)((g << 2) | (g >> 4));
                            bytes[i * 4 + 2] = (byte)((blue << 3) | (blue >> 2)); bytes[i * 4 + 3] = 255;
                        }
                        canvas.DrawBitmap(b, x * 256, y * 256);
                    }
                }
            Console.Error.WriteLine($"verified z{z}: {counts[z]} tiles");
        }
        var queries = new Dictionary<string, object>();
        foreach (var term in new[] { "北京", "Beijing", "London", "Paris", "Moscow", "Sydney", "Toronto", "New York" })
        {
            var matches = reader.Search(term, token: token);
            if (matches.Count == 0) throw new InvalidDataException($"Expected world search result missing: {term}");
            queries[term] = matches.Take(3).Select(p => new { p.Name, p.Latitude, p.Longitude }).ToArray();
        }
        if (reader.ZoomMask != 255 || (reader.Capabilities & 7) != 7 || (reader.Capabilities & 8) != 0)
            throw new InvalidDataException("Unexpected world capabilities or zoom mask.");
        reader.ValidateAllPages(token); canvas.Flush();
        using var image = SKImage.FromBitmap(mosaic); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var preview = File.Create(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "world-osm-z2-preview.png")); png.SaveTo(preview);
        return new { valid = true, counts, reader.PoiCount, annotations, reader.Capabilities, reader.ZoomMask, queries };
    }
    private const double LatitudeLimit = 85.0511287798066;
    private static readonly SKColor Land = SKColor.Parse("#ecebdc"), Water = SKColor.Parse("#a9d2e3");
    private static readonly Dictionary<string, int> Order = new(StringComparer.Ordinal)
    { ["ocean"] = 0, ["land"] = 1, ["sites"] = 2, ["water_polygons"] = 3, ["water_lines"] = 4, ["boundaries"] = 5, ["streets"] = 6, ["ferries"] = 7 };
    public static TmapBuildResult Build(string sourcePath, string output, CancellationToken token) => BuildRegion(sourcePath, output, null, token);
    public static TmapBuildResult BuildChina(string sourcePath, string output, string coverageDirectory, CancellationToken token) =>
        BuildRegion(sourcePath, output, new CountryCoverage(coverageDirectory), token);
    private static IEnumerable<TileCoordinate> WorldTiles(int z)
    { for (var y = 0; y < 1 << z; y++) for (var x = 0; x < 1 << z; x++) yield return new(z, x, y); }
    private static TmapBuildResult BuildRegion(string sourcePath, string output, CountryCoverage? coverage, CancellationToken token)
    {
        var minimum = coverage is null ? 0 : 8; var maximum = coverage is null ? 7 : 12;
        var expected = Enumerable.Range(minimum, maximum - minimum + 1).ToDictionary(z => z,
            z => (coverage?.Tiles(z) ?? WorldTiles(z)).Count());
        var total = expected.Values.Sum(); var prefix = coverage is null ? "world-osm-z0-7" : "china-osm-z8-12";
        var bounds = coverage?.Bounds ?? new(-180, -LatitudeLimit, 180, LatitudeLimit);
        sourcePath = Path.GetFullPath(sourcePath); output = Path.GetFullPath(output);
        var parent = Path.GetDirectoryName(output)!; Directory.CreateDirectory(parent);
        var space = Math.Max(10L * 1024 * 1024 * 1024, total * 131072L * 2 + 12L * 1024 * 1024 * 1024);
        if (new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace < space)
            throw new IOException($"Raster/index staging requires {space:N0} free bytes.");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open(); var metadata = new Dictionary<string, string>();
        using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT name,value FROM metadata"; using var rows = command.ExecuteReader();
            while (rows.Read()) metadata.Add(rows.GetString(0), rows.GetString(1));
        }
        if (metadata.GetValueOrDefault("format") != "pbf" || metadata.GetValueOrDefault("minzoom") != minimum.ToString() || metadata.GetValueOrDefault("maxzoom") != maximum.ToString())
            throw new InvalidDataException($"Expected Shortbread OSM z{minimum}–{maximum} archive.");
        using var builder = new TmapBuilder(parent, Math.Max(10L * 1024 * 1024 * 1024, space), token);
        using var read = source.CreateCommand();
        read.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level=$z AND tile_column=$x AND tile_row=$y";
        read.Parameters.Add("$z", SqliteType.Integer); read.Parameters.Add("$x", SqliteType.Integer); read.Parameters.Add("$y", SqliteType.Integer);
        long done = 0, supplied = 0, empty = 0, labels = 0; var layerCounts = new Dictionary<string, long>(); var counts = new Dictionary<int, int>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew(); var lastReport = -10L;
        // Highest zoom first: first observation of each source identity gets the finest available coordinate.
        for (var z = maximum; z >= minimum; z--)
        {
            var side = 1 << z; counts[z] = 0;
            foreach (var coordinate in coverage?.Tiles(z) ?? WorldTiles(z))
                {
                    var x = coordinate.X; var y = coordinate.Y;
                    token.ThrowIfCancellationRequested(); read.Parameters["$z"].Value = z;
                    read.Parameters["$x"].Value = x; read.Parameters["$y"].Value = side - 1 - y;
                    var blob = read.ExecuteScalar() as byte[];
                    List<VectorLayer> layers = [];
                    if (blob is not null)
                    {
                        supplied++;
                        if (blob.Length >= 2 && blob[0] == 0x1f && blob[1] == 0x8b)
                        {
                            using var input = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress);
                            using var unpacked = new MemoryStream(); input.CopyTo(unpacked); blob = unpacked.ToArray();
                        }
                        layers = VectorTile.Read(blob);
                    }
                    else
                    {
                        // Ocean itself is a vector feature; source tiles with no features represent land background.
                        empty++;
                    }
                    foreach (var layer in layers)
                    {
                        layerCounts[layer.Name] = layerCounts.GetValueOrDefault(layer.Name) + layer.Features.Count;
                        if (!layer.Name.EndsWith("_labels", StringComparison.Ordinal) && layer.Name != "pois") continue;
                        foreach (var feature in layer.Features.Where(f => f.Type == 1))
                        {
                            var parts = VectorTile.Parts(feature.Geometry); if (parts.Count == 0 || parts[0].Count == 0) continue;
                            var point = parts[0][0]; var extent = layer.Extent;
                            // Ignore buffer duplicates; the owner tile has the same identity and the actual label.
                            if (point.X < 0 || point.Y < 0 || point.X >= extent || point.Y >= extent) continue;
                            var names = new List<string>();
                            foreach (var key in new[] { "name_zh", "name", "name_en" })
                                if (feature.Tags.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)) names.Add(text);
                            foreach (var tag in feature.Tags.Where(t => t.Key.StartsWith("name_", StringComparison.Ordinal)).OrderBy(t => t.Key, StringComparer.Ordinal))
                                if (!string.IsNullOrWhiteSpace(tag.Value)) names.Add(tag.Value);
                            if (names.Count == 0) continue;
                            if (feature.Id == 0) throw new InvalidDataException("Named feature has no source identity.");
                            var normalizedX = (x + point.X / extent) / side;
                            var normalizedY = (y + point.Y / extent) / side;
                            var lon = normalizedX * 360 - 180;
                            var lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * normalizedY))) * 180 / Math.PI;
                            if (coverage is not null && !coverage.Contains(lat, lon)) continue;
                            var category = layer.Name == "boundary_labels" ? "admin" : layer.Name == "place_labels" ? "settlement" : layer.Name.StartsWith("water", StringComparison.Ordinal) ? "water" : "generic";
                            var kind = layer.Name is "boundary_labels" or "place_labels" ? 2 : 1;
                            var importance = layer.Name == "boundary_labels" ? 62000 : 10000;
                            if (feature.Tags.TryGetValue("population", out var pop) && double.TryParse(pop, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var population))
                                importance = Math.Clamp((int)(Math.Log10(Math.Max(1, population)) * 8000), 0, 60000);
                            var id = new byte[16]; TmapFormat.Put32(id, 0, 3);
                            // Provider vector IDs can renumber multipolygons. Keep them in a separate registered namespace.
                            TmapFormat.Put16(id, 4, category == "admin" ? 3 : category == "settlement" ? 1 : 2); TmapFormat.Put64(id, 8, feature.Id);
                            builder.AddPlace(new(id, lat, lon, category, names, importance, kind));
                            builder.AddAnnotation(z, x, y, id, kind, importance, lat, lon); labels++;
                        }
                    }
                    using var bitmap = Render(layers, blob is null && (y + 0.5) / side > 0.75);
                    builder.AddTile(1, z, x, y, TmapPackExporter.Pixels(bitmap, false)); done++; counts[z]++;
                    if (z == 0 || z == 7 && ((x, y) is (100, 49) or (101, 54) or (41, 48) or (64, 42)))
                    {
                        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                        using var preview = File.Create(Path.Combine(parent, $"world-osm-z{z}-{x}-{y}.png")); png.SaveTo(preview);
                    }
                    if (stopwatch.ElapsedMilliseconds / 1000 - lastReport >= 5 || counts[z] == expected[z])
                    { Console.Error.WriteLine($"tiles {done}/{total} z{z}, labels {labels}, elapsed {stopwatch.Elapsed}"); lastReport = stopwatch.ElapsedMilliseconds / 1000; }
                }
        }
        if (done != total) throw new InvalidDataException("Region tile pyramid is incomplete.");
        Console.Error.WriteLine("Compiling search/spatial/annotation indexes, publishing and validating...");
        var result = builder.Complete(output, new() { Name = coverage is null ? "世界地图 · OSM · 0–7" : "中国地图 · OSM · 8–12", Bounds = bounds }, new()
        {
            FileName = Path.GetFileName(output), PackageKey = prefix, Tier = coverage is null ? TmapRegionTier.World : TmapRegionTier.LargeCountry,
            CountryCode = coverage is null ? "" : "CN",
            Series = "trail-mate-osm-shortbread-v1", SourceNamespaceId = 3,
            SourceNamespaceUri = "https://download.versatiles.org/osm.20260608.versatiles#shortbread-feature-id",
            Attribution = "© OpenStreetMap contributors; ODbL-1.0; OSM vectors distributed by VersaTiles; https://www.openstreetmap.org/copyright",
            SourceCoverage = $"Shortbread OSM vectors z{minimum}–{maximum}: coastlines, water, administrative boundaries, transport and supplied country/city/water point labels; generalized and filtered, not all planet POIs"
        });
        var manifest = new
        {
            result, zooms = counts.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value), suppliedVectorTiles = supplied,
            featurelessLandTiles = empty, annotationCandidates = labels, sourceFeatureCounts = layerCounts,
            sourceUrl = "https://download.versatiles.org/osm.20260608.versatiles", sourceExtract = sourcePath,
            sourceSnapshot = "2026-06-08", generatedAtUtc = DateTimeOffset.UtcNow,
            bounds, coverageFiles = coverage?.SourceFiles, coveragePolicy = coverage is null ? "global" : "Intersecting XYZ tiles of union of Geofabrik China and Taiwan extraction polygons; Hong Kong/Macao within China polygon; these are extraction extents, not official administrative boundary data",
            pixelFormat = "RGB565LE", tileSize = 256, separateLabels = true, routing = false, sourceNamespace = 3,
            limitations = $"Search includes only point names supplied in z{minimum}–{maximum} vector tiles, not all OSM POIs. Coordinates are tile-quantized. Provider namespace IDs require mapping to raw OSM IDs for cross-source deduplication."
        };
        File.WriteAllText(Path.Combine(parent, prefix + ".manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
    internal static SKBitmap Render(List<VectorLayer> layers, bool empty)
    {
        var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(empty ? SKColor.Parse("#f1f3f0") : Land);
        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var layer in layers.Where(l => Order.ContainsKey(l.Name)).OrderBy(l => Order[l.Name]))
            foreach (var feature in layer.Features)
            {
                var kind = feature.Tags.GetValueOrDefault("kind", ""); paint.Style = feature.Type == 3 ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
                paint.StrokeWidth = 0.65f; paint.PathEffect = null;
                paint.Color = layer.Name switch
                {
                    "ocean" => Water, "water_polygons" when kind == "glacier" => SKColor.Parse("#f1f3f0"),
                    "water_polygons" or "water_lines" => Water,
                    "land" => SKColor.Parse("#cdddc0"), "sites" => SKColor.Parse("#ded9ce"),
                    "boundaries" => SKColor.Parse("#b2a498"), "streets" when kind == "motorway" => SKColor.Parse("#dca77a"),
                    "streets" => SKColor.Parse("#cbb490"), "ferries" => SKColor.Parse("#8aadc3"), _ => Land
                };
                if (layer.Name == "boundaries")
                {
                    if (feature.Tags.GetValueOrDefault("maritime") == "true") continue;
                    paint.StrokeWidth = feature.Tags.GetValueOrDefault("admin_level") == "2" ? 0.8f : 0.4f;
                }
                if (layer.Name == "streets" && feature.Tags.GetValueOrDefault("rail") == "true") paint.Color = SKColor.Parse("#a4a098");
                using var path = new SKPath { FillType = SKPathFillType.EvenOdd }; var scale = 256f / layer.Extent;
                foreach (var part in VectorTile.Parts(feature.Geometry))
                {
                    if (part.Count == 0) continue; path.MoveTo(part[0].X * scale, part[0].Y * scale);
                    for (var i = 1; i < part.Count; i++) path.LineTo(part[i].X * scale, part[i].Y * scale);
                    if (feature.Type == 3) path.Close();
                }
                canvas.DrawPath(path, paint);
            }
        canvas.Flush(); return bitmap;
    }
}
