using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps;
using TrailMateCenter.Styling;
using TrailMateCenter.ViewModels;
using TrailMateCenter.Views;

[assembly: AvaloniaTestApplication(typeof(TrailMateCenter.Tests.NavigationTestAppBuilder))]

namespace TrailMateCenter.Tests;

public static class NavigationTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<NavigationTestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class NavigationTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var baseUri = new Uri("avares://TrailMateCenter.App/");
        Resources.MergedDictionaries.Add(new ResourceInclude(baseUri) { Source = ThemeService.DefaultTheme.PaletteUri });
        Styles.Add(new StyleInclude(baseUri) { Source = ThemeService.DefaultTheme.ThemeUri });
        LocalizationService.Instance.ApplyCulture(new CultureInfo("zh-CN"));
    }
}

[CollectionDefinition("Navigation UI", DisableParallelization = true)]
public sealed class NavigationUiCollection { }

[Collection("Navigation UI")]
public sealed class NavigationMapGuiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", 1020, 820)]
    [InlineData("en-US", 820, 600)]
    public void DialogLoadsLocalizedControlsAndKeepsActionsVisible(string culture, int width, int height)
    {
        LocalizationService.Instance.ApplyCulture(new CultureInfo(culture));
        var vm = new NavigationMapBuilderViewModel();
        var window = new NavigationMapBuilderDialog(vm) { Width = width, Height = height };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
        Assert.DoesNotContain(text, t => t.StartsWith("Ui.NavigationMap."));
        var action = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.CreateCommand);
        Assert.False(vm.CreateCommand.CanExecute(null));
        Assert.False(action.IsEffectivelyEnabled);
        Assert.True(action.Bounds.Height >= 36);
        Assert.False(string.IsNullOrWhiteSpace(vm.ValidationMessage));
        var artifactRoot = Environment.GetEnvironmentVariable("TMAP_UI_EVIDENCE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(artifactRoot))
        {
            Directory.CreateDirectory(artifactRoot);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var image = window.CaptureRenderedFrame();
            Assert.NotNull(image);
            image.Save(Path.Combine(artifactRoot, $"navigation-{culture}-{width}.png"));
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task CancelStopsTheActiveBuildAndAllowsRetryWithoutStaleSuccess()
    {
        using var fixture = new Inputs();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = fixture.Create(async (_, token, _) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        Assert.True(vm.CanCreate);
        var operation = vm.CreateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanCreate);
        vm.CancelCommand.Execute(null);
        await operation;
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanCreate);
        Assert.False(vm.HasResult);
        Assert.False(vm.HasError);
        Assert.Equal(LocalizationService.Instance.GetString("Ui.NavigationMap.Canceled"), vm.StatusText);
    }

    [AvaloniaFact]
    public async Task FailedBuildKeepsInputsAndCanBeRetried()
    {
        using var fixture = new Inputs();
        var vm = fixture.Create((_, _, _) => throw new InvalidDataException("source references are incomplete"));
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy);
        Assert.True(vm.HasError);
        Assert.Contains("incomplete", vm.ErrorDetails);
        Assert.True(vm.CanCreate);
        Assert.Equal(fixture.Pbf, vm.PbfPath);
    }

    [AvaloniaFact]
    public async Task CompletedBuildPublishesActualPreviewAndAllowsANewName()
    {
        using var fixture = new Inputs();
        var vm = fixture.Create(async (plan, _, _) =>
        {
            Directory.CreateDirectory(plan.OutputDirectory);
            var path = Path.Combine(plan.OutputDirectory, "navigation.tmap");
            await File.WriteAllBytesAsync(path, TmapOutdoorPreviewTests.Fixture());
            return path;
        });
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.True(vm.HasResult && vm.HasPreview);
        Assert.False(vm.HasError);
        Assert.Equal(2, vm.Preview!.Roads.Count);
        Assert.False(vm.CanCreate);
        vm.PackName = "another-map";
        Assert.True(vm.CanCreate);
    }

    [AvaloniaFact]
    public async Task ExistingPreviewDoesNotNeedProductionEnvironmentAndProtectsPreviousResult()
    {
        using var fixture = new Inputs();
        var raw = TmapOutdoorPreviewTests.Fixture();
        var path = Path.Combine(fixture.Root, "existing.tmap");
        await File.WriteAllBytesAsync(path, raw);
        var vm = new NavigationMapBuilderViewModel();
        await vm.OpenExistingAsync(path);
        Assert.True(vm.HasPreview && vm.HasResult);
        Assert.Equal(3UL, vm.Preview!.Nodes);
        var bad = Path.Combine(fixture.Root, "bad.tmap");
        await File.WriteAllTextAsync(bad, "invalid map");
        await vm.OpenExistingAsync(bad);
        Assert.True(vm.HasError);
        Assert.Equal(path, vm.ResultPath);
        Assert.Equal(raw, await File.ReadAllBytesAsync(path));
    }

    [AvaloniaFact]
    public void AdditionalSourcesAreIncludedAndCanBeRemoved()
    {
        using var fixture = new Inputs();
        var vm = fixture.Create((_, _, _) => Task.FromResult("unused"));
        var extra = Path.Combine(fixture.Root, "neighbour.osm"); File.WriteAllText(extra, "source");
        vm.AddSources([extra, extra]);
        Assert.Single(vm.AdditionalSources);
        Assert.Equal(new[] { extra }, vm.BuildPlan().RegionalPbfPaths);
        vm.RemoveSourceCommand.Execute(null);
        Assert.Empty(vm.BuildPlan().RegionalPbfPaths);
    }

    [AvaloniaFact]
    public async Task ClosingAnActiveDialogWaitsForCancellation()
    {
        using var fixture = new Inputs();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = fixture.Create(async (_, token, _) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return "unreachable";
        });
        var window = new NavigationMapBuilderDialog(vm); window.Show();
        var task = vm.CreateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        window.Close();
        await task;
        Assert.False(vm.IsBusy);
        Assert.False(window.IsVisible);
        Assert.False(vm.HasResult);
    }

    [AvaloniaFact]
    public void PreflightProtectsExistingOutputAndTerrainNeedsAValidCrop()
    {
        using var fixture = new Inputs();
        var vm = fixture.Create((_, _, _) => Task.FromResult("unused"));
        Assert.True(vm.CanCreate);
        Directory.CreateDirectory(vm.OutputPreview);
        vm.PackName = "already-there";
        Directory.CreateDirectory(vm.OutputPreview);
        Assert.False(vm.CanCreate);
        vm.PackName = "new-map";
        vm.IncludeTerrain = true;
        Assert.False(vm.CanCreate);
        vm.HgtPath = Path.Combine(fixture.Root, "N47E009.hgt"); File.WriteAllText(vm.HgtPath, "fixture");
        Assert.True(vm.CanCreate);
        vm.North = 49;
        Assert.False(vm.CanCreate);
        vm.North = 47.15;
        vm.West = vm.East;
        Assert.False(vm.CanCreate);
        vm.IncludeTerrain = false;
        Assert.True(vm.CanCreate);
        vm.PackName = "../escape";
        Assert.False(vm.CanCreate);
    }

    private sealed class Inputs : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", "nav-gui-" + Guid.NewGuid().ToString("N"));
        public string Pbf => Path.Combine(Root, "roads.osm");
        public Inputs()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Pbf, "fixture");
            File.WriteAllText(Path.Combine(Root, "import_osm.py"), "fixture");
            File.WriteAllText(Path.Combine(Root, "tmap_pack.py"), "fixture");
        }
        public NavigationMapBuilderViewModel Create(Func<TrailMateCenter.Maps.Tmap.TmapNavigationExportPlan, CancellationToken,
            IProgress<TrailMateCenter.Maps.Tmap.TmapNavigationExportProgress>, Task<string>> export)
            => new(Pbf, Path.Combine(Root, "output"), new GeoBounds(9.50, 47.10, 9.55, 47.15), export)
            { ToolsDirectory = Root, PythonExecutable = "python" };
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
