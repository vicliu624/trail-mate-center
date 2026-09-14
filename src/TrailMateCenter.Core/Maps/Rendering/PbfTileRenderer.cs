using NetTopologySuite.Geometries;
using SkiaSharp;

namespace TrailMateCenter.Maps.Rendering;

/// <summary>Trail Mate's text-free raster style. Geometry only, suitable for heading-up rotation.</summary>
internal static class PbfTileRenderer
{
    public const string StyleVersion = "trailmate-text-free-v1";

    public static void Render(PbfBasemapStore store, TileCoordinate tile, bool terrain, string path, CancellationToken token)
    {
        var scale = 256.0 * (1 << tile.Z);
        var left = tile.X * 256.0;
        var top = tile.Y * 256.0;
        var envelope = new Envelope((left - 12) / scale, (left + 268) / scale, (top - 12) / scale, (top + 268) / scale);
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse(terrain ? "#f2efdf" : "#f1eee8"));
        // SQLite orders candidates on disk; do not materialize a low-zoom region in RAM.
        foreach (var feature in store.Query(envelope, tile.Z))
        {
            token.ThrowIfCancellationRequested();
            using var geometryPath = new SKPath { FillType = SKPathFillType.EvenOdd };
            Append(geometryPath, feature.Geometry, scale, left, top);
            var area = PbfBasemapStore.IsArea(feature.Kind);
            var width = Width(feature.Kind, tile.Z);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = area ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                Color = Color(feature.Kind, terrain),
                StrokeWidth = width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };
            if (feature.Kind is "highway" or "road" or "street")
            {
                using var casing = new SKPaint
                {
                    IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColor.Parse("#b6afa1"),
                    StrokeWidth = width + 1.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
                };
                canvas.DrawPath(geometryPath, casing);
            }
            if (feature.Kind == "path")
            {
                using var dash = SKPathEffect.CreateDash([3, 2], 0);
                paint.PathEffect = dash;
                canvas.DrawPath(geometryPath, paint);
            }
            else canvas.DrawPath(geometryPath, paint);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        png.SaveTo(output);
    }

    private static void Append(SKPath path, Geometry geometry, double scale, double left, double top)
    {
        switch (geometry)
        {
            case Polygon polygon:
                Ring(path, polygon.ExteriorRing.Coordinates, scale, left, top, true);
                for (var i = 0; i < polygon.NumInteriorRings; i++)
                    Ring(path, polygon.GetInteriorRingN(i).Coordinates, scale, left, top, true);
                break;
            case LineString line:
                Ring(path, line.Coordinates, scale, left, top, false);
                break;
            case GeometryCollection collection:
                for (var i = 0; i < collection.NumGeometries; i++) Append(path, collection.GetGeometryN(i), scale, left, top);
                break;
        }
    }

    private static void Ring(SKPath path, Coordinate[] points, double scale, double left, double top, bool close)
    {
        if (points.Length == 0) return;
        path.MoveTo((float)(points[0].X * scale - left), (float)(points[0].Y * scale - top));
        for (var i = 1; i < points.Length; i++) path.LineTo((float)(points[i].X * scale - left), (float)(points[i].Y * scale - top));
        if (close) path.Close();
    }

    private static float Width(string kind, int zoom)
    {
        var factor = Math.Clamp((zoom - 8) / 8f, 0.25f, 1.5f);
        return kind switch
        {
            "highway" => 5 * factor, "road" => 4 * factor, "street" => 3 * factor,
            "path" => 1.2f, "river" => 1.5f * factor, _ => 1,
        };
    }

    private static SKColor Color(string kind, bool terrain) => SKColor.Parse(kind switch
    {
        "water" or "river" or "coastline" => "#91c9e5", "building" => "#c7b9ac",
        "forest" => terrain ? "#b2c993" : "#bad5aa", "green" => "#d0e3b3",
        "farmland" => "#e8e3bb", "builtup" => "#e1dcd4", "highway" => "#edb877",
        "road" => "#f8df9b", "street" => "#ffffff", "path" => "#a57f57", "rail" => "#888888", _ => "#aaaaaa",
    });
}
