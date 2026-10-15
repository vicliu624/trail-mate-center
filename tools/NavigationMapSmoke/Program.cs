using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;
using TrailMateCenter.Styling;
using TrailMateCenter.ViewModels;
using TrailMateCenter.Views;

if (args.Length is not (5 or 6))
{
    Console.Error.WriteLine("NavigationMapSmoke ROAD_SOURCE HGT SDK_TOOLS PYTHON NEW_OUTPUT_ROOT [EXISTING_OUTDOOR_TMAP]");
    return 2;
}
using var session = HeadlessUnitTestSession.StartNew(typeof(SmokeAppBuilder));
await session.Dispatch(async () =>
{
    LocalizationService.Instance.ApplyCulture(new CultureInfo("en-US"));
    var output = Path.GetFullPath(args[4]);
    Directory.CreateDirectory(output);
    var model = new NavigationMapBuilderViewModel(Path.GetFullPath(args[0]), output,
        new GeoBounds(9.515, 47.14, 9.53, 47.15))
    {
        PackName = "navigation-gui-map", IncludeTerrain = true, HgtPath = Path.GetFullPath(args[1]),
        ToolsDirectory = Path.GetFullPath(args[2]), PythonExecutable = Path.GetFullPath(args[3]),
        TerrainAttribution = "Mapzen Skadi N47E009; Copernicus EU-DEM / USGS SRTM/GMTED2010; selected real OSM way fixture",
        DatumIndex = 1
    };
    if (!model.CanCreate) throw new InvalidOperationException(model.ValidationMessage);
    var window = new NavigationMapBuilderDialog(model);
    window.Show();
    Save(window, Path.Combine(output, "before.png"));
    var status = new List<string>();
    model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.StatusText)) status.Add(model.StatusText); };
    var heartbeats = 0;
    var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    heartbeat.Tick += (_, _) => heartbeats++;
    heartbeat.Start();
    await model.CreateCommand.ExecuteAsync(null);
    heartbeat.Stop();
    if (model.IsBusy || model.HasError || !model.HasResult || !model.HasPreview)
        throw new InvalidOperationException(model.StatusText + "\n" + model.ErrorDetails);
    if (heartbeats < 1) throw new InvalidOperationException("UI dispatcher did not run during production.");
    Save(window, Path.Combine(output, "completed.png"));
    var preview = new OutdoorMapPreviewWindow(model.Preview!);
    preview.Show(); Save(preview, Path.Combine(output, "preview.png")); preview.Close();
    var terrainProfile = TmapOutdoorPreviewReader.ReadElevationProfile(model.ResultPath,
        [new(9.516, 47.145), new(9.529, 47.145)]);
    if (terrainProfile.Samples.Count < 2 || terrainProfile.MinimumMeters is null || terrainProfile.ValidDistanceMeters <= 0)
        throw new InvalidOperationException("Real DEM profile did not cover the selected section.");
    await File.WriteAllTextAsync(Path.Combine(output, "elevation-profile.json"), JsonSerializer.Serialize(terrainProfile,
        new JsonSerializerOptions { WriteIndented = true }));
    var profileWindow = new ElevationProfileWindow(model.Preview!);
    profileWindow.Show(); await profileWindow.CalculateAsync();
    Save(profileWindow, Path.Combine(output, "elevation-profile.png")); profileWindow.Close();
    TmapOutdoorPreview? real = null;
    if (args.Length == 6)
    {
        real = await Task.Run(() => TmapOutdoorPreviewReader.Read(args[5]));
        var realWindow = new OutdoorMapPreviewWindow(real);
        realWindow.Show(); Save(realWindow, Path.Combine(output, "real-country-preview.png")); realWindow.Close();
    }
    var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(model.ResultPath))).ToLowerInvariant();
    await File.WriteAllTextAsync(Path.Combine(output, "gui-evidence.json"), JsonSerializer.Serialize(new
    {
        scope = "Actual GUI view-model command, real selected OSM way plus Skadi HGT, actual SDK tools; headless rendered controls. Not manual OS file-picker or physical display testing.",
        model.ResultPath, packageSha256 = hash, model.ResultSummary, heartbeats, status,
        preview = new { roads = model.Preview!.Roads.Count, contours = model.Preview.Contours.Count, model.Preview.Sampled },
        existingCountry = real is null ? null : new { real.Nodes, real.DirectedEdges, roads = real.Roads.Count, contours = real.Contours.Count, real.Sampled }
    }, new JsonSerializerOptions { WriteIndented = true }));
    window.Close();
    Console.WriteLine(model.ResultPath);
    return true;
}, CancellationToken.None);
return 0;

static void Save(Avalonia.Controls.Window window, string path)
{
    window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered UI frame.");
    image.Save(path);
}
public static class SmokeAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SmokeApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class SmokeApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var uri = new Uri("avares://TrailMateCenter.App/");
        Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = ThemeService.DefaultTheme.PaletteUri });
        Styles.Add(new StyleInclude(uri) { Source = ThemeService.DefaultTheme.ThemeUri });
    }
}
