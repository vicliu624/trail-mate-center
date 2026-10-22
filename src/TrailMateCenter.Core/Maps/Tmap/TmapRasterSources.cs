using SkiaSharp;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Center-only imagery acquisition and DEM shading. No source decoding runs on the device.</summary>
public sealed class TmapRasterSources : IDisposable
{
    public const string SatelliteUrl = "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public const string ElevationUrl = "https://elevation-tiles-prod.s3.amazonaws.com/terrarium/{z}/{x}/{y}.png";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _root;
    private readonly Dictionary<(int Z, int X, int Y), float[]> _heights = new();
    public TmapRasterSources(string? cacheRoot = null)
    {
        _root = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TrailMateCenter/0.1");
    }
    public async Task<byte[]> Satellite(int z, int x, int y, CancellationToken token)
    {
        // Reuse Center's existing satellite-cache/z/x/y.jpg tree.
        using var image = await Load("satellite-cache", "jpg", SatelliteUrl, z, x, y, token).ConfigureAwait(false);
        return TmapPackExporter.Pixels(image, false);
    }
    public async Task Shade(SKBitmap bitmap, int z, int x, int y, CancellationToken token)
    {
        var sourceZ = Math.Min(z, 15); var scale = 1 << (z - sourceZ); var n = 1 << sourceZ;
        var minX = (int)Math.Floor((x * 256.0 / scale - 1) / 256);
        var maxX = (int)Math.Floor(((x * 256.0 + 256) / scale + 1) / 256);
        var minY = (int)Math.Floor((y * 256.0 / scale - 1) / 256);
        var maxY = (int)Math.Floor(((y * 256.0 + 256) / scale + 1) / 256);
        var required = new HashSet<(int Z, int X, int Y)>();
        for (var tx = minX; tx <= maxX; tx++) for (var ty = minY; ty <= maxY; ty++)
                required.Add((sourceZ, (tx % n + n) % n, Math.Clamp(ty, 0, n - 1)));
        foreach (var old in _heights.Keys.Where(k => !required.Contains(k)).ToArray()) _heights.Remove(old);
        for (var tx = minX; tx <= maxX; tx++) for (var ty = minY; ty <= maxY; ty++)
            {
                var key = (sourceZ, (tx % n + n) % n, Math.Clamp(ty, 0, n - 1));
                if (_heights.ContainsKey(key)) continue;
                using var image = await Load("tmap-elevation-cache", "png", ElevationUrl, key.sourceZ, key.Item2, key.Item3, token).ConfigureAwait(false);
                var data = new float[65536];
                for (var py = 0; py < 256; py++) for (var px = 0; px < 256; px++)
                    { var p = image.GetPixel(px, py); data[py * 256 + px] = (float)(p.Red * 256 + p.Green + p.Blue / 256.0 - 32768); }
                _heights.Add(key, data);
            }
        double Sample(int gx, int gy)
        {
            gx = (gx % (n * 256) + n * 256) % (n * 256); gy = Math.Clamp(gy, 0, n * 256 - 1);
            return _heights[(sourceZ, gx / 256, gy / 256)][gy % 256 * 256 + gx % 256];
        }
        double Height(double gx, double gy)
        {
            var ix = (int)Math.Floor(gx); var iy = (int)Math.Floor(gy); var fx = gx - ix; var fy = gy - iy;
            return (Sample(ix, iy) * (1 - fx) + Sample(ix + 1, iy) * fx) * (1 - fy) +
                (Sample(ix, iy + 1) * (1 - fx) + Sample(ix + 1, iy + 1) * fx) * fy;
        }
        var step = 1.0 / scale;
        for (var py = 0; py < 256; py++)
        {
            token.ThrowIfCancellationRequested();
            var gy = (y * 256.0 + py + .5) / scale - .5;
            var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y * 256.0 + py + .5) / (256.0 * (1 << z)))));
            var spacing = Math.Max(.01, 156543.03392804097 / (1 << z) * Math.Cos(latitude));
            for (var px = 0; px < 256; px++)
            {
                var gx = (x * 256.0 + px + .5) / scale - .5;
                var dx = (Height(gx + step, gy) - Height(gx - step, gy)) / (2 * spacing);
                var dy = (Height(gx, gy - step) - Height(gx, gy + step)) / (2 * spacing);
                // North-west illumination, 45 degrees altitude. The same
                // one-pixel halo is used across all output tile boundaries.
                var light = Math.Max(0, (dx * .5 - dy * .5 + .7071067811865476) / Math.Sqrt(dx * dx + dy * dy + 1));
                var factor = .65 + .45 * light; var color = bitmap.GetPixel(px, py);
                bitmap.SetPixel(px, py, new SKColor((byte)Math.Clamp(color.Red * factor, 0, 255),
                    (byte)Math.Clamp(color.Green * factor, 0, 255), (byte)Math.Clamp(color.Blue * factor, 0, 255), color.Alpha));
            }
        }
    }
    private async Task<SKBitmap> Load(string cache, string extension, string template, int z, int x, int y, CancellationToken token)
    {
        var directory = Path.Combine(_root, cache, z.ToString(), x.ToString());
        var path = Path.Combine(directory, $"{y}.{extension}");
        if (File.Exists(path))
        {
            var saved = SKBitmap.Decode(path);
            if (saved is { Width: 256, Height: 256 }) return saved;
            saved?.Dispose();
        }
        var url = template.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var bytes = new MemoryStream(); var buffer = new byte[16384]; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("Raster response exceeds 4 MiB.");
                    bytes.Write(buffer, 0, count);
                }
                var image = SKBitmap.Decode(bytes.ToArray()) ?? throw new InvalidDataException("Invalid raster response.");
                if (image.Width != 256 || image.Height != 256) { image.Dispose(); throw new InvalidDataException("Expected a 256×256 raster."); }
                Directory.CreateDirectory(directory); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, bytes.ToArray()); File.Move(temporary, path, true); }
                catch { image.Dispose(); throw; }
                finally { File.Delete(temporary); }
                return image;
            }
            catch (HttpRequestException) when (attempt < 2) { await Task.Delay(500 * (attempt + 1), token).ConfigureAwait(false); }
        }
    }
    public void Dispose() { _http.Dispose(); _heights.Clear(); }
}
