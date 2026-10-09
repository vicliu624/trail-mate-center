using NetTopologySuite.Geometries;
using SkiaSharp;
using TrailMateCenter.Maps.Rendering;
using TrailMateCenter.Osm;
using TrailMateCenter.Places;

namespace TrailMateCenter.Maps.Tmap;

public sealed class TmapPackExporter
{
    public static void Validate(MapPackExportPlan plan)
    {
        var options = plan.Tmap ?? throw new ArgumentException("TMAP options are required.");
        if (!File.Exists(plan.Poi.PbfPath)) throw new FileNotFoundException("Select the local OSM PBF for this region.", plan.Poi.PbfPath);
        if (string.IsNullOrWhiteSpace(plan.OutputDirectory) || string.IsNullOrWhiteSpace(options.PackageKey)) throw new ArgumentException("Output directory and stable package key are required.");
        if (Path.GetFileName(options.FileName) != options.FileName || !options.FileName.EndsWith(".tmap", StringComparison.OrdinalIgnoreCase) || options.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Choose a simple .tmap filename, without directories.");
        var b = plan.Area.Bounds;
        if (!double.IsFinite(b.West) || !double.IsFinite(b.East) || !double.IsFinite(b.South) || !double.IsFinite(b.North) ||
            b.West < -180 || b.East > 180 || b.South < -90 || b.North > 90 || b.West >= b.East || b.South >= b.North)
            throw new ArgumentException("Select valid WGS84 bounds. Split an antimeridian-crossing area into separate packs.");
        var l = plan.BaseLayers;
        if (l.MinimumZoom < 0 || l.MaximumZoom > 18 || l.MinimumZoom > l.MaximumZoom) throw new ArgumentException("PBF rendering supports z0–18.");
        if (options.Tier != TmapRegionTier.Custom && (l.MinimumZoom, l.MaximumZoom) != TmapOptions.Zooms(options.Tier))
            throw new ArgumentException("Zoom range does not match the world/country/administrative-region preset.");
        if (options.MaximumOutputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumOutputBytes));
        // Conservative raw preflight estimate; deduplication savings are known only after staging.
        var layers = (l.IncludeOsm ? 1L : 0) + (l.IncludeTerrain ? 1L : 0) + (l.IncludeSatellite ? 1L : 0);
        var bytes = checked(ExportEstimator.CountTiles(b, l.MinimumZoom, l.MaximumZoom) * layers * 131072L);
        if (bytes > options.MaximumOutputBytes) throw new IOException($"Native raster payload alone requires {bytes:N0} bytes, exceeding the output budget.");
        var workingBytes = checked(bytes * 2 + 64L * 1024 * 1024);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(plan.OutputDirectory))!);
        if (drive.AvailableFreeSpace < workingBytes)
            throw new IOException($"Raster staging and publication need approximately {workingBytes:N0} free bytes before POI/source-cache overhead.");
        if (Path.GetFullPath(Path.Combine(plan.OutputDirectory, options.FileName)).Equals(Path.GetFullPath(plan.Poi.PbfPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output must not replace the source PBF.");
    }

    public Task<TmapBuildResult> ExportAsync(MapPackExportPlan plan, IProgress<LocalMapPackProgress>? progress = null,
        Func<string, CancellationToken, Task>? addContours = null, CancellationToken cancellationToken = default)
    {
        Validate(plan);
        if (plan.BaseLayers.IncludeContours && addContours is null) throw new ArgumentException("Contour input is unavailable.");
        return Task.Run(async () =>
        {
            var temporary = Path.Combine(Path.GetFullPath(plan.OutputDirectory), ".tmap-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                using var builder = new TmapBuilder(temporary, plan.Tmap!.MaximumOutputBytes, cancellationToken);
                using var terrain = plan.BaseLayers.IncludeTerrain ? new TmapBuilder(temporary, plan.Tmap.MaximumOutputBytes, cancellationToken) : null;
                using var satellite = plan.BaseLayers.IncludeSatellite ? new TmapBuilder(temporary, plan.Tmap.MaximumOutputBytes, cancellationToken) : null;
                using var sources = new TmapRasterSources();
                using var store = new PbfBasemapStore(Path.Combine(temporary, "geometry.sqlite"));
                store.Import(plan.Poi.PbfPath, (stage, n) => progress?.Report(new(stage, n, 0)), cancellationToken, plan.Area.Bounds, includeAnnotations: true);
                var polygon = GeoJsonPointInPolygonFilter.TryCreate(plan.Area.BoundaryGeoJson);
                var (west, north) = PbfBasemapStore.Project(plan.Area.Bounds.West, plan.Area.Bounds.North);
                var (east, south) = PbfBasemapStore.Project(plan.Area.Bounds.East, plan.Area.Bounds.South);
                // Full source features, before zoom/class display quotas, including ways and relations.
                foreach (var f in store.QueryAnnotations(new Envelope(west, east, north, south), 29))
                {
                    cancellationToken.ThrowIfCancellationRequested(); var p = f.Geometry.InteriorPoint.Coordinate; if (p is null) continue;
                    var lat = Latitude(p.Y); var lon = p.X * 360 - 180;
                    if (!plan.Area.Bounds.Contains(lat, lon) || polygon is not null && !polygon.Contains(lat, lon)) continue;
                    var names = new[] { f.Name, f.Reference ?? "" }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                    builder.AddPlace(new(StableId(f.Id), lat, lon, f.Rule.Category, names, f.Rule.Priority, Kind(f.Rule.Kind), f.Rule.MinimumZoom));
                }
                // Preserve all named node aliases, independent of the annotation classification policy.
                await new OsmPlaceExtractor().ExtractToAsync(new PlaceExtractionOptions
                { PbfPath = plan.Poi.PbfPath, Bounds = plan.Area.Bounds, BoundaryGeoJson = plan.Area.BoundaryGeoJson }, place =>
                {
                    builder.AddPlace(new(TmapFormat.OsmId(place.OsmType, place.OsmId!.Value), place.Latitude, place.Longitude, place.Category, place.Names, place.Rank));
                    return ValueTask.CompletedTask;
                }, cancellationToken: cancellationToken).ConfigureAwait(false);

                var layers = new List<int>(); if (plan.BaseLayers.IncludeOsm) layers.Add(1); if (plan.BaseLayers.IncludeTerrain) layers.Add(2); if (plan.BaseLayers.IncludeSatellite) layers.Add(3);
                var total = ExportEstimator.CountTiles(plan.Area.Bounds, plan.BaseLayers.MinimumZoom, plan.BaseLayers.MaximumZoom) * layers.Count;
                long done = 0;
                for (var z = plan.BaseLayers.MinimumZoom; z <= plan.BaseLayers.MaximumZoom; z++)
                {
                    var range = TileMath.BoundsToTileRange(plan.Area.Bounds, z);
                    for (var x = range.MinX; x <= range.MaxX; x++)
                        for (var y = range.MinY; y <= range.MaxY; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested(); var tile = new TileCoordinate(z, x, y);
                            foreach (var layer in layers)
                            {
                                if (layer == 3)
                                    satellite!.AddTile(3, z, x, y, await sources.Satellite(z, x, y, cancellationToken).ConfigureAwait(false));
                                else
                                    PbfTileRenderer.Render(store, tile, layer == 2, "", cancellationToken, pixelSink: bitmap =>
                                    {
                                        if (layer == 2) sources.Shade(bitmap, z, x, y, cancellationToken).GetAwaiter().GetResult();
                                        (layer == 2 ? terrain! : builder).AddTile(layer, z, x, y, Pixels(bitmap, false));
                                    });
                                progress?.Report(new("tiles", ++done, total, z, layer switch { 2 => "terrain", 3 => "satellite", _ => "osm" }));
                            }
                            // Bounded display candidates; every searchable place was already staged above.
                            var selected = new List<(MapAnnotationCandidate Candidate, byte[] Id)>(200);
                            foreach (var feature in store.QueryAnnotations(MapAnnotationCandidateBuilder.TileEnvelope(tile), z))
                            {
                                var anchor = feature.Geometry.InteriorPoint.Coordinate;
                                if (anchor is null || !plan.Area.Bounds.Contains(Latitude(anchor.Y), anchor.X * 360 - 180) ||
                                    polygon is not null && !polygon.Contains(Latitude(anchor.Y), anchor.X * 360 - 180)) continue;
                                foreach (var candidate in MapAnnotationCandidateBuilder.Build(feature, tile, plan.Annotations ?? new MapAnnotationOptions()))
                                {
                                    selected.Add((candidate, StableId(feature.Id)));
                                    if (selected.Count >= 200) break;
                                }
                                if (selected.Count >= 200) break;
                            }
                            foreach (var (candidate, id) in selected)
                            {
                                var path = new List<(double, double)>(); var points = candidate.Path;
                                if (points is not null)
                                    for (var i = 0; i < points.Length; i += 2)
                                    { var scale = 256.0 * (1 << z); path.Add((Latitude((y * 256.0 + points[i + 1]) / scale), (x * 256.0 + points[i]) / scale * 360 - 180)); }
                                builder.AddAnnotation(z, x, y, id, Kind(candidate.Kind), candidate.Priority, candidate.Latitude, candidate.Longitude, path);
                            }
                        }
                }
                if (plan.BaseLayers.IncludeContours)
                {
                    var maps = Path.Combine(temporary, "maps"); Directory.CreateDirectory(maps);
                    await addContours!(maps, cancellationToken).ConfigureAwait(false);
                    ImportRasterDirectory(terrain ?? builder, maps, cancellationToken);
                }
                progress?.Report(new("tmap-indexes", 0, 0));
                var root = TmapLayout.Root(plan.OutputDirectory); var packages = new List<TmapBuildResult>();
                void Stage(TmapBuilder output, string style, string attribution)
                {
                    var options = plan.Tmap with { FileName = TmapLayout.FileName(plan.Tmap.FileName, style),
                        PackageKey = plan.Tmap.PackageKey + ":" + style, Attribution = attribution,
                        GenerateFontPacks = style == "osm", FontOutputDirectory = temporary,
                        AdministrativeBoundaryManifest = style == "osm" ? plan.Tmap.AdministrativeBoundaryManifest : null };
                    var result = output.Complete(Path.Combine(temporary, style, options.FileName), plan.Area, options);
                    packages.Add(result);
                }
                // One authoritative OSM package contains full POI/search and
                // annotation data even when only imagery pixels were selected.
                Stage(builder, "osm", plan.Tmap.Attribution);
                if (terrain is not null) Stage(terrain, "terrain", plan.Tmap.Attribution + "; Terrain Tiles: https://github.com/tilezen/joerd/blob/master/docs/attribution.md");
                if (satellite is not null) Stage(satellite, "satellite", "Esri World Imagery; https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer");
                if (packages.Sum(p => p.FileBytes) > plan.Tmap.MaximumOutputBytes) throw new IOException("Combined TMAP output exceeds the configured budget.");
                // Publish content-addressed font resources before making their
                // dependent TMAP visible. Unrelated SD resources are preserved.
                var stagedFonts = Path.Combine(temporary, "trailmate", "packs", "fonts");
                if (Directory.Exists(stagedFonts))
                    foreach (var source in Directory.EnumerateDirectories(stagedFonts))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var destination = Path.Combine(TmapLayout.FontRootForPackage(Path.Combine(root, "osm", plan.Tmap.FileName)),
                            "trailmate", "packs", "fonts", Path.GetFileName(source));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        if (!Directory.Exists(destination)) Directory.Move(source, destination);
                        else foreach (var file in Directory.EnumerateFiles(source))
                            if (!File.Exists(Path.Combine(destination, Path.GetFileName(file))) ||
                                !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(destination, Path.GetFileName(file)))))
                                throw new InvalidDataException("Existing map font differs from its content-addressed identity.");
                    }
                for (var i = 0; i < packages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var style = Path.GetFileName(Path.GetDirectoryName(packages[i].FilePath)!);
                    var target = Path.Combine(root, style, Path.GetFileName(packages[i].FilePath));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(packages[i].FilePath, target, true);
                    if (File.Exists(packages[i].FilePath + ".font-resources.json"))
                        File.Move(packages[i].FilePath + ".font-resources.json", target + ".font-resources.json", true);
                    packages[i] = packages[i] with { FilePath = target }; TmapPackageRegistry.Register(target);
                }
                return packages[0] with { Packages = packages, TileCount = packages.Sum(p => p.TileCount), FileBytes = packages.Sum(p => p.FileBytes) };
            }
            finally { try { Directory.Delete(temporary, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }, cancellationToken);
    }
    internal static int Kind(MapAnnotationKind kind) => kind switch { MapAnnotationKind.Place => 2, MapAnnotationKind.Road => 3, _ => 1 };
    internal static byte[] StableId(string feature) { var parts = feature.Split('/'); return TmapFormat.OsmId(parts[0], long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)); }
    private static double Latitude(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI;
    public static byte[] Pixels(SKBitmap bitmap, bool alpha)
    {
        if (bitmap.Width != 256 || bitmap.Height != 256) throw new InvalidDataException("TMAP raster tiles must be 256×256.");
        var result = new byte[256 * 256 * (alpha ? 4 : 2)]; var offset = 0;
        if (!alpha && bitmap.ColorType == SKColorType.Rgba8888 && bitmap.AlphaType == SKAlphaType.Opaque)
        {
            var source = bitmap.GetPixelSpan();
            for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                {
                    var at = y * bitmap.RowBytes + x * 4;
                    var rgb565 = (source[at] >> 3 << 11) | (source[at + 1] >> 2 << 5) | (source[at + 2] >> 3);
                    result[offset++] = (byte)rgb565; result[offset++] = (byte)(rgb565 >> 8);
                }
            return result;
        }
        for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (alpha) { result[offset++] = p.Red; result[offset++] = p.Green; result[offset++] = p.Blue; result[offset++] = p.Alpha; }
            else { if (p.Alpha != 255) throw new InvalidDataException("An opaque base tile contains transparent pixels."); TmapFormat.Put16(result, offset, (p.Red >> 3 << 11) | (p.Green >> 2 << 5) | (p.Blue >> 3)); offset += 2; }
        }
        return result;
    }
    public static void ImportRasterDirectory(TmapBuilder builder, string mapsRoot, CancellationToken token = default)
    {
        var layers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["base/osm"] = 1, ["base/terrain"] = 2, ["base/satellite"] = 3, ["contour/major-500"] = 100, ["contour/major-200"] = 101,
            ["contour/major-100"] = 102, ["contour/major-50"] = 103, ["contour/major-25"] = 104, ["contour/minor-100"] = 110,
            ["contour/minor-50"] = 111, ["contour/minor-20"] = 112, ["contour/minor-10"] = 113, ["contour/minor-5"] = 114 };
        foreach (var (folder, layer) in layers)
        {
            var root = Path.Combine(mapsRoot, folder); if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (parts.Length != 3 || !int.TryParse(parts[0], out var z) || !int.TryParse(parts[1], out var x) || !int.TryParse(Path.GetFileNameWithoutExtension(parts[2]), out var y))
                    throw new InvalidDataException("Invalid XYZ tile path: " + file);
                using var bitmap = SKBitmap.Decode(file) ?? throw new InvalidDataException("Cannot decode " + file);
                builder.AddTile(layer, z, x, y, Pixels(bitmap, layer >= 100), layer >= 100);
            }
        }
    }
}
