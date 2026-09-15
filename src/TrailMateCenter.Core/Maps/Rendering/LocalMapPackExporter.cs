using System.Security.Cryptography;
using System.Text.Json;

namespace TrailMateCenter.Maps.Rendering;

public sealed record LocalMapPackProgress(string Stage, long Completed, long Total, int Zoom = 0, string Layer = "osm");
public sealed record LocalMapPackResult(string MapsRoot, long TileCount, PoiExportResult Poi, string? PreviousMapsRoot)
{
    public MapAnnotationIndexResult? Annotations { get; init; }
}

/// <summary>Produces a complete map pack with raster text complementary to independent POI zooms.</summary>
public sealed class LocalMapPackExporter
{
    public static void Validate(MapPackExportPlan plan)
    {
        if (!plan.Poi.EnablePoiSeparation && plan.Annotations is null) throw new ArgumentException("Local rendering requires independent map annotations.");
        if (!File.Exists(plan.Poi.PbfPath)) throw new FileNotFoundException("Select a local OSM PBF before building the text-free map.", plan.Poi.PbfPath);
        if (plan.BaseLayers.IncludeSatellite) throw new ArgumentException("Local PBF rendering cannot produce satellite imagery. Deselect Satellite.");
        if (plan.Annotations is null && plan.Poi.SelectedPoiTypes.Count == 0) throw new ArgumentException("Select at least one POI category.");
        if (plan.Annotations is null && !plan.Poi.GenerateTileIndex) throw new ArgumentException("Enable the tile POI index for an SD map pack.");
        if (plan.Annotations is not null && plan.BaseLayers.MinimumZoom < 1) throw new ArgumentException("Map annotation zoom levels start at 1.");
        if (string.IsNullOrWhiteSpace(plan.OutputDirectory)) throw new ArgumentException("Select a map pack output directory.");
        var b = plan.Area.Bounds;
        if (!double.IsFinite(b.West) || !double.IsFinite(b.East) || !double.IsFinite(b.North) || !double.IsFinite(b.South) ||
            b.West >= b.East || b.South >= b.North)
            throw new ArgumentException("Select a non-empty map area that does not cross the antimeridian.");
        if (plan.BaseLayers.MinimumZoom < 0 || plan.BaseLayers.MaximumZoom > 18 || plan.BaseLayers.MinimumZoom > plan.BaseLayers.MaximumZoom)
            throw new ArgumentException("Map zoom levels must be between 0 and 18 in ascending order.");
        if (plan.BaseLayers.IncludeContours && !plan.BaseLayers.IncludeOsm && !plan.BaseLayers.IncludeTerrain)
            throw new ArgumentException("Select an OSM or Terrain base layer for a local map pack with contours.");
        if (plan.BaseLayers.IncludeTerrain && !plan.BaseLayers.IncludeOsm && plan.BaseLayers.MinimumZoom > 17)
            throw new ArgumentException("Terrain tiles support zoom levels up to 17. Select OSM for zoom 18.");
        var target = ResolveMapsRoot(plan.OutputDirectory);
        if (Path.GetFullPath(plan.Poi.PbfPath).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source PBF must be outside the destination maps directory.");
    }

    public async Task<LocalMapPackResult> ExportAsync(MapPackExportPlan plan,
        IProgress<LocalMapPackProgress>? progress = null,
        Func<string, CancellationToken, Task>? addContours = null,
        CancellationToken cancellationToken = default)
    {
        Validate(plan);
        if (plan.BaseLayers.IncludeContours && addContours is null)
            throw new ArgumentException("Contour generation is unavailable.");
        var target = ResolveMapsRoot(plan.OutputDirectory);
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var work = Path.Combine(parent, $".map-pack-{Guid.NewGuid():N}");
        var stagedMaps = Path.Combine(work, "maps");
        Directory.CreateDirectory(stagedMaps);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourceHash;
            await using (var input = File.OpenRead(plan.Poi.PbfPath))
                sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            var layers = new List<(string Name, bool Terrain, int MaxZoom)>();
            if (plan.BaseLayers.IncludeOsm) layers.Add(("osm", false, plan.BaseLayers.MaximumZoom));
            if (plan.BaseLayers.IncludeTerrain) layers.Add(("terrain", true, Math.Min(17, plan.BaseLayers.MaximumZoom)));
            var total = layers.Sum(l => l.MaxZoom < plan.BaseLayers.MinimumZoom ? 0 :
                ExportEstimator.CountTiles(plan.Area.Bounds, plan.BaseLayers.MinimumZoom, l.MaxZoom));
            long completed = 0;
            MapAnnotationIndexResult? annotations = null;
            MapAnnotationFontPack? annotationFont = null;
            var independentPolicy = (plan.Poi.IndexOptions with
            {
                EnabledZoomLevels = plan.Poi.IndexOptions.Normalize().EnabledZoomLevels!
                    .Where(z => z >= plan.BaseLayers.MinimumZoom && z <= plan.BaseLayers.MaximumZoom).ToArray(),
            }).Normalize();
            var allLevels = Enumerable.Range(plan.BaseLayers.MinimumZoom, plan.BaseLayers.MaximumZoom - plan.BaseLayers.MinimumZoom + 1).ToArray();
            var labelledLevels = plan.Annotations is not null ? Array.Empty<int>() : allLevels.Where(z => !independentPolicy.IsEnabledAtZoom(z)).ToArray();
            var sourceIssueCount = 0;
            BasemapSourceIssue[] sourceIssues = [];
            if (layers.Count > 0 || plan.Annotations is not null)
            {
                await Task.Run(async () =>
                {
                    using var store = new PbfBasemapStore(Path.Combine(work, "geometry.sqlite"));
                    store.Import(plan.Poi.PbfPath, (stage, count) => progress?.Report(new(stage, count, 0)), cancellationToken, plan.Area.Bounds,
                        includeAnnotations: plan.Annotations is not null);
                    sourceIssueCount = store.SourceIssueCount;
                    sourceIssues = store.SourceIssues.ToArray();
                    var (west, north) = PbfBasemapStore.Project(plan.Area.Bounds.West, plan.Area.Bounds.North);
                    var (east, south) = PbfBasemapStore.Project(plan.Area.Bounds.East, plan.Area.Bounds.South);
                    if (layers.Count > 0 && !store.Query(new NetTopologySuite.Geometries.Envelope(west, east, north, south), plan.BaseLayers.MaximumZoom).Any())
                        throw new InvalidDataException("The PBF has no drawable map geometry in the selected area and zoom range.");
                    foreach (var layer in layers)
                        for (var z = plan.BaseLayers.MinimumZoom; z <= layer.MaxZoom; z++)
                        {
                            var range = TileMath.BoundsToTileRange(plan.Area.Bounds, z);
                            for (var x = range.MinX; x <= range.MaxX; x++)
                                for (var y = range.MinY; y <= range.MaxY; y++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var tile = new TileCoordinate(z, x, y);
                                    PbfTileRenderer.Render(store, tile, layer.Terrain,
                                        Path.Combine(stagedMaps, "base", layer.Name, z.ToString(), x.ToString(), $"{y}.png"), cancellationToken,
                                        bakeLabels: plan.Annotations is null && !independentPolicy.IsEnabledAtZoom(z));
                                    completed++;
                                    progress?.Report(new("tiles", completed, total, z, layer.Name));
                                }
                        }
                    if (plan.Annotations is not null)
                    {
                        progress?.Report(new("annotations", 0, total));
                        annotations = await MapAnnotationIndexWriter.WriteAsync(store, stagedMaps, plan.Area.Bounds,
                            plan.BaseLayers.MinimumZoom, plan.BaseLayers.MaximumZoom, plan.Annotations, cancellationToken).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(stagedMaps, "poi", "required-codepoints.json"),
                            JsonSerializer.Serialize(annotations.RequiredCodepoints), cancellationToken).ConfigureAwait(false);
                        progress?.Report(new("annotations", annotations.TileFiles, annotations.TileFiles));
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            var poi = annotations is not null ? new PoiExportResult
            {
                Success = true,
                PoiRoot = Path.Combine(stagedMaps, "poi"),
                SourcePoiCount = annotations.PoiFeatures,
                IndexRowsWritten = annotations.Rows,
                TileFilesWritten = annotations.TileFiles,
                WasAnyTileClipped = annotations.ClippedTiles > 0,
            } : await new MapPoiExportService().ExportFromPbfAsync(new PoiExportRequest
            {
                MapsRoot = stagedMaps,
                PbfPath = plan.Poi.PbfPath,
                Bounds = plan.Area.Bounds,
                BoundaryGeoJson = plan.Area.BoundaryGeoJson,
                AreaName = plan.Area.Name,
                AreaAdminLevel = plan.Area.AdminLevel,
                SourceProvider = plan.Poi.SourceProvider,
                SourceDownloadUrl = plan.Poi.SourceDownloadUrl,
                SelectedPoiTypes = plan.Poi.SelectedPoiTypes,
                IndexOptions = independentPolicy with
                {
                    GenerateFullPoisJsonl = plan.Poi.GenerateFullPoisJsonl,
                    GenerateTileIndex = plan.Poi.GenerateTileIndex,
                },
            }, new Progress<Osm.OsmPoiExtractionProgress>(p => progress?.Report(new("poi", p.ProcessedElements, 0))), cancellationToken).ConfigureAwait(false);
            if (!poi.Success) throw new InvalidDataException(poi.ErrorMessage);
            if (annotations is not null)
            {
                annotationFont = MapAnnotationFontPackBuilder.Build(work, annotations.RequiredCodepoints, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(stagedMaps, "poi", "font-resources.json"),
                    JsonSerializer.Serialize(new { font_pack_id = annotationFont?.Id, copy_sd_root = "trailmate/packs/fonts", text_background = "transparent" }), cancellationToken);
            }
            if (plan.BaseLayers.IncludeContours) await addContours!(stagedMaps, cancellationToken).ConfigureAwait(false);

            var sourceDiagnostics = new
            {
                ignored_invalid_relations_outside_requested_area = sourceIssueCount,
                examples = sourceIssues,
                examples_truncated = sourceIssueCount > sourceIssues.Length,
            };
            if (sourceIssueCount > 0)
                await File.WriteAllTextAsync(Path.Combine(stagedMaps, "source-geometry-warnings.json"),
                    JsonSerializer.Serialize(sourceDiagnostics, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(stagedMaps, "map-pack.json"), JsonSerializer.Serialize(new
            {
                version = plan.Annotations is null ? 2 : 3,
                generator = "TrailMateCenter",
                style = PbfTileRenderer.StyleVersion,
                text_in_basemap = labelledLevels.Length > 0,
                poi_symbols_in_basemap = false,
                tile_size = 256,
                labelled_zoom_levels = labelledLevels,
                text_free_zoom_levels = plan.Annotations is null ? independentPolicy.EnabledZoomLevels : allLevels,
                annotation_policy = plan.Annotations is null ? null : MapAnnotationPolicy.Version,
                scheme = "web-mercator-xyz",
                coordinates = "WGS84",
                source = Path.GetFileName(plan.Poi.PbfPath),
                source_sha256 = sourceHash,
                area = plan.Area,
                base_layers = layers.Select(l => l.Name).ToArray(),
                min_zoom = plan.BaseLayers.MinimumZoom,
                max_zoom = plan.BaseLayers.MaximumZoom,
                source_diagnostics = sourceDiagnostics,
                attribution = "© OpenStreetMap contributors; ODbL 1.0; https://www.openstreetmap.org/copyright",
                notes = "Raster text is rendered only at labelled_zoom_levels; independent POI zooms are text-free. Regional PBF coverage only, no hillshade or global ocean fill. Coastlines are outlines. Terrain is a land-cover palette; optional contours are separate.",
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);

            // No cancellation once publication begins. Preserve the old pack for recovery.
            cancellationToken.ThrowIfCancellationRequested();
            // Content-addressed font directories are additive. Publish the required
            // font before its map, preserving all unrelated SD resources. If map
            // publication fails, an unused font is harmless and the old map remains.
            if (annotationFont is not null)
            {
                var fontParent = Path.Combine(parent, "trailmate", "packs", "fonts");
                Directory.CreateDirectory(fontParent);
                var fontTarget = Path.Combine(fontParent, annotationFont.Id);
                if (Directory.Exists(fontTarget))
                {
                    foreach (var file in Directory.GetFiles(annotationFont.Directory))
                    {
                        var existing = Path.Combine(fontTarget, Path.GetFileName(file));
                        if (!File.Exists(existing) || !File.ReadAllBytes(existing).SequenceEqual(File.ReadAllBytes(file)))
                            throw new InvalidDataException("An existing map font package differs from its content identity. Move that font package aside and retry.");
                    }
                }
                else Directory.Move(annotationFont.Directory, fontTarget);
            }
            string? backup = null;
            if (Directory.Exists(target))
            {
                backup = target + $".previous-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
                Directory.Move(target, backup);
            }
            try { Directory.Move(stagedMaps, target); }
            catch
            {
                if (backup is not null) Directory.Move(backup, target);
                throw;
            }
            return new LocalMapPackResult(target, completed, poi with { PoiRoot = Path.Combine(target, "poi") }, backup) { Annotations = annotations };
        }
        finally
        {
            // This unique directory is owned exclusively by this export operation.
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (IOException) { /* Leave only this export's staging directory for later cleanup. */ }
            catch (UnauthorizedAccessException) { /* A cleanup error must not disguise the export outcome. */ }
        }
    }

    private static string ResolveMapsRoot(string output)
    {
        var full = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(full), "maps", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "maps");
    }
}
