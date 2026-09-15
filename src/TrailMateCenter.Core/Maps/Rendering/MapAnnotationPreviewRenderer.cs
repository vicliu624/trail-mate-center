using NetTopologySuite.Geometries;
using SkiaSharp;

namespace TrailMateCenter.Maps.Rendering;

public sealed record MapAnnotationPreview(byte[] Png, int Zoom, int RoadLabels, int PlaceLabels, int PoiLabels);

/// <summary>Local data/style preview, never an online labelled basemap masquerading as export output.</summary>
public static class MapAnnotationPreviewRenderer
{
    public static Task<MapAnnotationPreview> RenderAsync(string pbf, GeoBounds area, int zoom, MapAnnotationOptions options,
        CancellationToken token = default) => Task.Run(() =>
    {
        var work = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            using var store = new PbfBasemapStore(Path.Combine(work, "geometry.sqlite"));
            store.Import(pbf, null, token, area, includeAnnotations: true);
            return Render(store, area, zoom, options, work, token);
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }, token);

    internal static MapAnnotationPreview Render(PbfBasemapStore store, GeoBounds area, int zoom, MapAnnotationOptions options,
        string work, CancellationToken token)
    {
        zoom = Math.Clamp(zoom, 1, 18);
        const int width = 480, height = 222;
        var (centerX, centerY) = PbfBasemapStore.Project((area.West + area.East) / 2, (area.South + area.North) / 2);
        var scale = 256.0 * (1 << zoom);
        var left = centerX * scale - width / 2;
        var top = centerY * scale - height / 2;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#f1eee8"));
        var candidates = new List<(MapAnnotationCandidate Data, float X, float Y)>();
        for (var x = (int)Math.Floor(left / 256); x <= Math.Floor((left + width - 1) / 256); x++)
            for (var y = (int)Math.Floor(top / 256); y <= Math.Floor((top + height - 1) / 256); y++)
            {
                token.ThrowIfCancellationRequested();
                var tile = new TileCoordinate(zoom, x, y);
                var file = Path.Combine(work, $"{zoom}-{x}-{y}.png");
                PbfTileRenderer.Render(store, tile, false, file, token, bakeLabels: false);
                using var raster = SKBitmap.Decode(file);
                canvas.DrawBitmap(raster, (float)(x * 256.0 - left), (float)(y * 256.0 - top));
                foreach (var feature in store.QueryAnnotations(MapAnnotationCandidateBuilder.TileEnvelope(tile), zoom))
                    foreach (var candidate in MapAnnotationCandidateBuilder.Build(feature, tile, options))
                    {
                        var (px, py) = PbfBasemapStore.Project(candidate.Longitude, candidate.Latitude);
                        var sx = (float)(px * scale - left); var sy = (float)(py * scale - top);
                        if (sx >= 0 && sy >= 0 && sx < width && sy < height && !string.IsNullOrWhiteSpace(candidate.Name))
                            candidates.Add((candidate, sx, sy));
                    }
            }
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AnnotationFonts", "NotoSansCJKsc-Regular.otf");
        using var face = SKTypeface.FromFile(fontPath) ?? throw new FileNotFoundException("Bundled preview font is missing.", fontPath);
        using var font = new SKFont(face, 16);
        using var paint = new SKPaint { Color = SKColor.Parse("#3a2a1a"), IsAntialias = true };
        var rectangles = new List<SKRect>();
        var displayed = new HashSet<string>();
        var roadNames = new Dictionary<string, List<SKPoint>>();
        var counts = new int[3];
        var maxLabels = zoom <= 6 ? 8 : zoom <= 11 ? 16 : zoom <= 15 ? 24 : 32;
        var ordered = candidates.OrderBy(c => c.Data.Kind == MapAnnotationKind.Road ? 0 : c.Data.Kind == MapAnnotationKind.Place ? 1 : 2)
            .ThenByDescending(c => c.Data.Priority).ThenBy(c => c.Data.Id, StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            token.ThrowIfCancellationRequested();
            if (rectangles.Count >= maxLabels) break;
            if (displayed.Contains(candidate.Data.FeatureId)) continue;
            if (candidate.Data.Kind == MapAnnotationKind.Road && roadNames.TryGetValue(candidate.Data.Name, out var positions) &&
                positions.Any(p => Math.Abs(p.X - candidate.X) < 200 && Math.Abs(p.Y - candidate.Y) < 60)) continue;
            var text = candidate.Data.Name;
            var runes = text.EnumerateRunes().ToArray();
            var length = runes.Length;
            while (font.MeasureText(text) > 128 && length > 1) text = string.Concat(runes.Take(--length).Select(r => r.ToString())) + "…";
            var textWidth = font.MeasureText(text);
            var bounds = new SKRect(candidate.X - textWidth / 2, candidate.Y - 8, candidate.X + textWidth / 2, candidate.Y + 8);
            if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > width || bounds.Bottom > height || rectangles.Any(r => r.IntersectsWith(bounds))) continue;
            canvas.DrawText(text, bounds.Left, bounds.Top - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
            bounds.Inflate(2, 2); rectangles.Add(bounds);
            displayed.Add(candidate.Data.FeatureId); counts[(int)candidate.Data.Kind]++;
            if (candidate.Data.Kind == MapAnnotationKind.Road)
            {
                if (!roadNames.TryGetValue(candidate.Data.Name, out var list)) roadNames[candidate.Data.Name] = list = new();
                list.Add(new(candidate.X, candidate.Y));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(png.ToArray(), zoom, counts[(int)MapAnnotationKind.Road], counts[(int)MapAnnotationKind.Place], counts[(int)MapAnnotationKind.Poi]);
    }
}
