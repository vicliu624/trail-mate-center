using System.Text.Json;
using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;

internal static class CountryVerification
{
    public static object Check(string path, string coverageDirectory, CancellationToken token)
    {
        var coverage = new CountryCoverage(coverageDirectory); using var reader = new TmapReader(path);
        var counts = new Dictionary<int, int>(); long annotations = 0;
        var range = TileMath.BoundsToTileRange(coverage.Bounds, 8); const int thumb = 32;
        using var mosaic = new SKBitmap((range.MaxX - range.MinX + 1) * thumb, (range.MaxY - range.MinY + 1) * thumb, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(mosaic); canvas.Clear(SKColor.Parse("#dfe6e9"));
        var last = Environment.TickCount64;
        for (var z = 8; z <= 12; z++)
        {
            counts[z] = 0;
            foreach (var tile in coverage.Tiles(z))
            {
                token.ThrowIfCancellationRequested(); var raster = reader.ReadTile(1, z, tile.X, tile.Y) ?? throw new InvalidDataException($"Missing {z}/{tile.X}/{tile.Y}.");
                if (raster.Codec != 1 || raster.Pixels.Length != 131072) throw new InvalidDataException("Expected RGB565LE.");
                counts[z]++; annotations += reader.ReadAnnotations(z, tile.X, tile.Y).Count;
                if (z == 8)
                {
                    using var bitmap = Decode(raster.Pixels);
                    canvas.DrawBitmap(bitmap, new SKRect((tile.X - range.MinX) * thumb, (tile.Y - range.MinY) * thumb,
                        (tile.X - range.MinX + 1) * thumb, (tile.Y - range.MinY + 1) * thumb));
                }
                if (Environment.TickCount64 - last > 5000)
                { Console.Error.WriteLine($"checking z{z}: {counts[z]} tiles"); last = Environment.TickCount64; }
            }
            Console.Error.WriteLine($"verified z{z}: {counts[z]} tiles");
        }
        if (reader.ZoomMask != 7936 || reader.Capabilities != 7 || reader.ReadTile(1, 7, 100, 49) is not null)
            throw new InvalidDataException("Incorrect China zoom/capability contract.");
        var queries = CheckCities(reader, token);
        var samples = new[] { ("kunming", 102.7123, 25.0406), ("beijing", 116.3912, 39.9055), ("hong-kong", 114.1694, 22.3193), ("taipei", 121.5654, 25.0330) };
        foreach (var (name, lon, lat) in samples)
        {
            var center = TileMath.LonLatToTile(lon, lat, 12);
            using var detail = new SKBitmap(768, 768, SKColorType.Rgba8888, SKAlphaType.Opaque); using var detailCanvas = new SKCanvas(detail);
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var raster = reader.ReadTile(1, 12, center.X + dx, center.Y + dy) ?? throw new InvalidDataException("Missing preview tile.");
                    using var bitmap = Decode(raster.Pixels); detailCanvas.DrawBitmap(bitmap, (dx + 1) * 256, (dy + 1) * 256);
                }
            detailCanvas.Flush(); Save(detail, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"china-osm-{name}-z12.png"));
        }
        reader.ValidateAllPages(token); canvas.Flush(); Save(mosaic, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "china-osm-z8-preview.png"));
        return new { valid = true, counts, reader.PoiCount, annotations, reader.Capabilities, reader.ZoomMask, queries };
    }
    public static Dictionary<string, object> CheckCities(TmapReader reader, CancellationToken token)
    {
        var queries = new Dictionary<string, object>();
        var targets = new (string[] Names, double Latitude, double Longitude, double Radius)[]
        {
            (["北京", "北京市"], 39.9055, 116.3912, 1), (["上海市", "上海"], 31.23, 121.47, 1),
            (["昆明市", "昆明"], 25.04, 102.72, 1), (["乌鲁木齐"], 43.82, 87.61, 1),
            (["拉萨市", "拉萨"], 29.65, 91.12, 1), (["香港"], 22.28, 114.16, 0.5),
            (["澳门", "澳門", "Macao"], 22.20, 113.54, 0.3), (["臺北", "台北", "Taipei"], 25.03, 121.56, 0.5)
        };
        foreach (var target in targets)
        {
            var text = target.Names[0]; IReadOnlyList<TmapPoiInfo> results = [];
            foreach (var variant in target.Names)
            {
                text = variant;
                results = reader.Search(variant, limit: 1000, latitude: target.Latitude, longitude: target.Longitude, token: token)
                    .Where(p => Math.Abs(p.Latitude - target.Latitude) <= target.Radius && Math.Abs(p.Longitude - target.Longitude) <= target.Radius).ToArray();
                if (results.Count > 0) break;
            }
            if (results.Count == 0) throw new InvalidDataException("Expected city location missing: " + string.Join('/', target.Names));
            queries[text] = results.Take(3).Select(p => new { p.Name, p.Latitude, p.Longitude }).ToArray();
        }
        return queries;
    }
    private static SKBitmap Decode(byte[] pixels)
    {
        var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque); var bytes = bitmap.GetPixelSpan();
        for (var i = 0; i < 65536; i++)
        {
            var color = pixels[i * 2] | pixels[i * 2 + 1] << 8;
            var r = color >> 11 & 31; var g = color >> 5 & 63; var blue = color & 31;
            bytes[i * 4] = (byte)((r << 3) | (r >> 2)); bytes[i * 4 + 1] = (byte)((g << 2) | (g >> 4));
            bytes[i * 4 + 2] = (byte)((blue << 3) | (blue >> 2)); bytes[i * 4 + 3] = 255;
        }
        return bitmap;
    }
    private static void Save(SKBitmap bitmap, string path)
    { using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); using var output = File.Create(path); png.SaveTo(output); }
}
