using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using System.Globalization;
using System.Text.Json;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps.Tmap;
using TrailMateCenter.Styling;
using TrailMateCenter.Views;

if (args.Length != 6) return 2;
var package = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var coordinates = args.Skip(2).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
Directory.CreateDirectory(output);
using var session = HeadlessUnitTestSession.StartNew(typeof(RegionalSmokeBuilder));
await session.Dispatch(async () =>
{
    LocalizationService.Instance.ApplyCulture(new CultureInfo("en-US"));
    var map = await Task.Run(() => TmapOutdoorPreviewReader.Read(package));
    var preview = new OutdoorMapPreviewWindow(map);
    preview.Show(); Save(preview, Path.Combine(output, "map-preview.png")); preview.Close();
    var points = new[] { new TmapPreviewPoint(coordinates[0], coordinates[1]), new(coordinates[2], coordinates[3]) };
    var profile = TmapOutdoorPreviewReader.ReadElevationProfile(package, points, 112, 3);
    if (profile.MinimumMeters is null || profile.ValidDistanceMeters < profile.DistanceMeters * .999)
        throw new InvalidDataException("Selected regional profile is not fully covered by numeric DEM.");
    var window = new ElevationProfileWindow(map);
    window.Show();
    var inputs = window.GetLogicalDescendants().OfType<NumericUpDown>().ToArray();
    if (inputs.Length != 6) throw new InvalidOperationException("Unexpected profile inputs.");
    for (var i = 0; i < 4; ++i) inputs[i].Value = (decimal)coordinates[i];
    inputs[4].Value = 112; inputs[5].Value = 3;
    await window.CalculateAsync();
    Save(window, Path.Combine(output, "elevation-profile.png")); window.Close();
    await File.WriteAllTextAsync(Path.Combine(output, "center-profile.json"), JsonSerializer.Serialize(new
    {
        producer = "TrailMateCenter regional preview and actual elevation-profile window; headless, English UI",
        map.SourcePath, map.FileBytes, map.Nodes, map.DirectedEdges, map.Sampled,
        roads = map.Roads.Count, contours = map.Contours.Count, points, profile
    }, new JsonSerializerOptions { WriteIndented = true }));
    return true;
}, CancellationToken.None);
return 0;

static void Save(Window window, string path)
{
    window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
    image.Save(path);
}
public static class RegionalSmokeBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<RegionalSmokeApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class RegionalSmokeApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var uri = new Uri("avares://TrailMateCenter.App/");
        Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = ThemeService.DefaultTheme.PaletteUri });
        Styles.Add(new StyleInclude(uri) { Source = ThemeService.DefaultTheme.ThemeUri });
    }
}
