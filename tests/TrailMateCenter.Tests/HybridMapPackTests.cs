using System.Text.Json;
using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Tests;

public sealed class HybridMapPackTests
{
    [Fact]
    public void DefaultHighZoomPolicySurvivesExpandingInitialMapRange()
    {
        var selector = new PoiZoomSelectionViewModel();
        selector.SetBounds(10, 16);
        Assert.Equal(new[] { 16 }, selector.EnabledZoomLevels);
        selector.SetBounds(1, 18);
        Assert.Equal(new[] { 16, 17, 18 }, selector.EnabledZoomLevels);
    }

    [Fact]
    public void RendererFontContainsActualChineseGlyphs()
    {
        using var typeface = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Resources", "AnnotationFonts", "NotoSansCJKsc-Regular.otf"));
        Assert.NotNull(typeface);
        using var font = new SKFont(typeface, 12);
        Assert.All(font.GetGlyphs("昆明饮水点道路"), glyph => Assert.NotEqual((ushort)0, glyph));
    }

    [Fact]
    public async Task LowZoomBakesTextWhileHighZoomIndexesAreTheOnlyIndependentLevels()
    {
        using var fixture = new LocalMapPackTests();
        var source = fixture.Plan("hybrid", "昆明饮水点");
        var plan = source with
        {
            BaseLayers = source.BaseLayers with { MinimumZoom = 15, MaximumZoom = 18 },
            Poi = source.Poi with { IndexOptions = source.Poi.IndexOptions with { EnabledZoomLevels = [16, 17, 18] } },
        };
        var hybrid = await new LocalMapPackExporter().ExportAsync(plan);
        var clean = await new LocalMapPackExporter().ExportAsync(plan with
        {
            OutputDirectory = plan.OutputDirectory + "-clean",
            Poi = plan.Poi with { IndexOptions = plan.Poi.IndexOptions with { EnabledZoomLevels = [15, 16, 17, 18] } },
        });
        Assert.Equal(new[] { 16, 17, 18 }, Directory.GetDirectories(Path.Combine(hybrid.MapsRoot, "poi", "index"))
            .Select(p => int.Parse(Path.GetFileName(p))).Order());
        foreach (var zoom in new[] { 15, 16, 17, 18 })
        {
            var differences = 0;
            foreach (var path in Directory.GetFiles(Path.Combine(hybrid.MapsRoot, "base", "osm", zoom.ToString()), "*.png", SearchOption.AllDirectories))
            {
                var reference = Path.Combine(clean.MapsRoot, Path.GetRelativePath(hybrid.MapsRoot, path));
                if (!(await File.ReadAllBytesAsync(path)).SequenceEqual(await File.ReadAllBytesAsync(reference))) differences++;
            }
            if (zoom == 15) Assert.True(differences > 0);
            else Assert.Equal(0, differences);
        }
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(hybrid.MapsRoot, "map-pack.json")));
        Assert.Equal(new[] { 15 }, manifest.RootElement.GetProperty("labelled_zoom_levels").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { 16, 17, 18 }, manifest.RootElement.GetProperty("text_free_zoom_levels").EnumerateArray().Select(x => x.GetInt32()));
    }
}
