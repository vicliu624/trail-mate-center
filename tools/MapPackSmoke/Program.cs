using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

if (args.Length != 8)
{
    Console.Error.WriteLine("Usage: MapPackSmoke <pbf> <output> <west> <south> <east> <north> <minZoom> <maxZoom>");
    return 2;
}
var bounds = new GeoBounds(Parse(args[2]), Parse(args[3]), Parse(args[4]), Parse(args[5]));
var maxZoom = int.Parse(args[7], CultureInfo.InvariantCulture);
var plan = new MapPackExportPlan
{
    OutputDirectory = Path.GetFullPath(args[1]), Area = new MapPackAreaSelection { Name = "Local rendering verification", Bounds = bounds },
    BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = int.Parse(args[6], CultureInfo.InvariantCulture), MaximumZoom = maxZoom },
    Poi = new MapPackPoiSelection
    {
        EnablePoiSeparation = true, PbfPath = Path.GetFullPath(args[0]),
        SelectedPoiTypes = ["water", "camp", "shelter", "peak", "viewpoint", "parking", "toilet", "emergency", "trailhead"],
        IndexOptions = new PoiIndexOptions { MinZoom = int.Parse(args[6], CultureInfo.InvariantCulture), MaxZoom = maxZoom },
    },
};
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var timer = Stopwatch.StartNew();
var result = await new LocalMapPackExporter().ExportAsync(plan, new ConsoleProgress(), cancellationToken: cts.Token);
var report = new { result.MapsRoot, result.TileCount, result.Poi.SourcePoiCount, result.Poi.TileFilesWritten, elapsed_seconds = timer.Elapsed.TotalSeconds };
Console.WriteLine(JsonSerializer.Serialize(report));
await File.WriteAllTextAsync(Path.Combine(plan.OutputDirectory, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

// Visual QA artifact, never included inside maps/ or sent to the device.
var range = TileMath.BoundsToTileRange(bounds, maxZoom);
var columns = Math.Min(4, range.MaxX - range.MinX + 1);
var rows = Math.Min(4, range.MaxY - range.MinY + 1);
using var mosaic = new SKBitmap(columns * 256, rows * 256);
using (var canvas = new SKCanvas(mosaic))
for (var x = 0; x < columns; x++)
for (var y = 0; y < rows; y++)
{
    using var tile = SKBitmap.Decode(Path.Combine(result.MapsRoot, "base", "osm", maxZoom.ToString(), (range.MinX + x).ToString(), $"{range.MinY + y}.png"));
    canvas.DrawBitmap(tile, x * 256, y * 256);
}
using (var image = SKImage.FromBitmap(mosaic))
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
using (var output = File.Create(Path.Combine(plan.OutputDirectory, "preview.png"))) data.SaveTo(output);
return 0;

static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
sealed class ConsoleProgress : IProgress<LocalMapPackProgress>
{
    private string _stage = "";
    private long _lastTick;
    public void Report(LocalMapPackProgress p)
    {
        var now = Environment.TickCount64;
        if (_stage != p.Stage || now - _lastTick > 1500 || p.Completed == p.Total)
        {
            _stage = p.Stage;
            _lastTick = now;
            Console.WriteLine($"{p.Stage}: {p.Completed}/{p.Total} z{p.Zoom}");
        }
    }
}
