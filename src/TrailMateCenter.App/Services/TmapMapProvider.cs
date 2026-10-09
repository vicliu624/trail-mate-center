using Mapsui;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Projections;
using Mapsui.Styles;
using SkiaSharp;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Services;

/// <summary>Viewport data from generated TMAP packages; no network or loose PNG/JSON files.</summary>
internal sealed class TmapMapProvider(int layer) : IProvider
{
    private const double HalfWorld = 20037508.342789244;
    private static readonly string LabelFont = OperatingSystem.IsWindows() ? "Microsoft YaHei" : "sans-serif";
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string? CRS { get; set; } = "EPSG:3857";
    public MRect GetExtent() => new(-HalfWorld, -HalfWorld, HalfWorld, HalfWorld);
    public async Task<IEnumerable<IFeature>> GetFeaturesAsync(FetchInfo fetchInfo)
    {
        var extent = fetchInfo.Extent;
        var resolution = fetchInfo.Resolution;
        var requested = Math.Clamp((int)Math.Round(Math.Log2(156543.03392804097 / fetchInfo.Resolution)), 0, 18);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(() => Read(extent, requested, resolution)).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private IEnumerable<IFeature> Read(MRect extent, int requested, double resolution)
    {
        var features = new List<IFeature>();
        var readers = new List<TmapReader>();
        try
        {
            foreach (var path in TmapPackageRegistry.Paths()) readers.Add(new TmapReader(path));
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var painted = new HashSet<(int Z, int X, int Y)>();
            var ordered = readers.OrderBy(r => r.CoverageArea).ThenByDescending(r => r.Revision).ToArray();
            var n = 1 << requested; var cell = HalfWorld * 2 / n;
            int X(double x) => Math.Clamp((int)Math.Floor((x + HalfWorld) / cell), 0, n - 1);
            int Y(double y) => Math.Clamp((int)Math.Floor((HalfWorld - y) / cell), 0, n - 1);
            var x0 = X(extent.MinX); var x1 = X(extent.MaxX); var y0 = Y(extent.MaxY); var y1 = Y(extent.MinY);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 128) return features;
            var annotationLists = new Dictionary<(int Z, int X, int Y), IReadOnlyList<TmapAnnotation>>();
            for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++)
            {
                // Resolve each requested region independently: detailed China
                // coverage must not hide the world pixels beside its border.
                for (var z = requested; z >= 0; z--)
                {
                    var tx = x >> (requested - z); var ty = y >> (requested - z); var key = (z, tx, ty);
                    if (painted.Contains(key)) break;
                    var tile = ordered.Where(r => r.HasRasterLayer(layer) && (r.ZoomMask & (1U << z)) != 0)
                        .Select(r => r.ReadTile(layer, z, tx, ty)).FirstOrDefault(t => t is not null);
                    if (tile is null) continue;
                    var size = HalfWorld * 2 / (1 << z);
                    var rect = new MRect(-HalfWorld + tx * size, HalfWorld - (ty + 1) * size,
                        -HalfWorld + (tx + 1) * size, HalfWorld - ty * size);
                    var raster = new RasterFeature(new MRaster(Encode(tile), rect)); raster.Styles.Add(new RasterStyle());
                    features.Add(raster); painted.Add(key); break;
                }
                // OSM annotations may live in a separate package with no
                // pixels. Avoid rereading a parent list for adjacent regions.
                if (layer >= 100) continue;
                for (var z = requested; z >= 0; z--)
                {
                    var tx = x >> (requested - z); var ty = y >> (requested - z);
                    var key = (z, tx, ty);
                    if (!annotationLists.TryGetValue(key, out var annotations))
                    {
                        annotations = [];
                        foreach (var reader in ordered.Where(r => r.HasAnnotations && (r.ZoomMask & (1U << z)) != 0))
                        {
                            annotations = reader.ReadDisplayAnnotations(z, tx, ty);
                            if (annotations.Count != 0) break;
                        }
                        annotationLists.Add(key, annotations);
                    }
                    if (annotations.Count == 0) continue;
                    foreach (var annotation in annotations)
                    {
                        if (labels.Count >= 4096) break;
                        if (string.IsNullOrWhiteSpace(annotation.Poi.Name)) continue;
                        var (px, py) = SphericalMercator.FromLonLat(annotation.Longitude, annotation.Latitude);
                        if (px < extent.MinX || px > extent.MaxX || py < extent.MinY || py > extent.MaxY) continue;
                        if (X(px) != x || Y(py) != y) continue;
                        if (!labels.Add(Convert.ToHexString(annotation.Poi.StableId))) continue;
                        var feature = new PointFeature(new MPoint(px, py));
                        feature["tmap_stable_id"] = Convert.ToHexString(annotation.Poi.StableId);
                        feature["tmap_priority"] = (int)annotation.Priority;
                        feature["tmap_name"] = annotation.Poi.Name;
                        feature["tmap_kind"] = annotation.Kind;
                        feature.Styles.Add(new LabelStyle { Text = annotation.Poi.Name, ForeColor = Color.Black,
                            BackColor = null, CollisionDetection = true, Halo = new Pen(Color.White, 2),
                            Font = new Font { FontFamily = LabelFont, Size = annotation.Kind == 2 ? 12 : 10 },
                            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center });
                        features.Add(feature);
                    }
                    break;
                }
            }
            // Paint parents first, detailed pixels above them, labels last.
            features.Sort((a, b) => a is RasterFeature ar && b is RasterFeature br
                ? br.Extent!.Width.CompareTo(ar.Extent!.Width) : a is RasterFeature ? -1 : b is RasterFeature ? 1
                : (b["tmap_priority"] is int bp ? bp : 0).CompareTo(a["tmap_priority"] is int ap ? ap : 0));
            return CullLabels(features, extent, resolution);
        }
        finally { foreach (var reader in readers) reader.Dispose(); }
    }
    private static IEnumerable<IFeature> CullLabels(List<IFeature> features, MRect extent, double resolution)
    {
        var result = new List<IFeature>();
        var cells = new Dictionary<(int X, int Y), List<SKRect>>();
        var roadNames = new HashSet<string>(StringComparer.Ordinal);
        using var typeface = SKTypeface.FromFamilyName(LabelFont);
        using var measure = new SKFont(typeface);
        foreach (var feature in features)
        {
            if (feature is not PointFeature point) { result.Add(feature); continue; }
            var label = feature.Styles.OfType<LabelStyle>().First(); measure.Size = (float)label.Font.Size;
            var name = (string)feature["tmap_name"]!;
            var road = feature["tmap_kind"] is int kind && kind == 3;
            if (road && roadNames.Contains(name)) continue;
            var width = measure.MeasureText(name, null) + 8; var height = measure.Size * 1.5f + 6;
            var px = (float)((point.Point.X - extent.MinX) / resolution); var py = (float)((extent.MaxY - point.Point.Y) / resolution);
            var rect = new SKRect(px - width / 2, py - height / 2, px + width / 2, py + height / 2);
            var x0 = (int)Math.Floor(rect.Left / 32); var x1 = (int)Math.Floor(rect.Right / 32);
            var y0 = (int)Math.Floor(rect.Top / 32); var y1 = (int)Math.Floor(rect.Bottom / 32); var collision = false;
            for (var x = x0; x <= x1 && !collision; x++) for (var y = y0; y <= y1 && !collision; y++)
                collision = cells.TryGetValue((x, y), out var entries) && entries.Any(r => r.IntersectsWith(rect));
            if (collision) continue;
            if (road) roadNames.Add(name);
            result.Add(feature);
            for (var x = x0; x <= x1; x++) for (var y = y0; y <= y1; y++)
            {
                if (!cells.TryGetValue((x, y), out var entries)) cells.Add((x, y), entries = []);
                entries.Add(rect);
            }
        }
        return result;
    }
    private static byte[] Encode(TmapTile tile)
    {
        using var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var target = bitmap.GetPixelSpan(); var pixels = tile.Pixels;
        for (var i = 0; i < 65536; i++)
        {
            if (tile.Codec == 2) pixels.AsSpan(i * 4, 4).CopyTo(target.Slice(i * 4, 4));
            else
            {
                var v = pixels[2 * i] | pixels[2 * i + 1] << 8;
                var r = v >> 11; var g = v >> 5 & 63; var b = v & 31;
                target[i * 4] = (byte)(r << 3 | r >> 2); target[i * 4 + 1] = (byte)(g << 2 | g >> 4);
                target[i * 4 + 2] = (byte)(b << 3 | b >> 2); target[i * 4 + 3] = 255;
            }
        }
        // Mapsui's raster feature interface accepts encoded images. This
        // adapter is desktop-only; firmware continues using raw native pixels.
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
}
