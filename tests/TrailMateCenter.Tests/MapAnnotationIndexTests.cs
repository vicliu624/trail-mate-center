using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationIndexTests
{
    [Fact]
    public async Task OneIndexCarriesRoadGeometryAndFacilitiesWithRequiredGlyphs()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("unified-index", "人民路");
        using var store = new PbfBasemapStore(Path.Combine(Path.GetDirectoryName(plan.Poi.PbfPath)!, "unified.sqlite"));
        store.Import(plan.Poi.PbfPath, null, default, plan.Area.Bounds, includeAnnotations: true);
        var maps = Path.Combine(plan.OutputDirectory, "maps");
        var result = await MapAnnotationIndexWriter.WriteAsync(store, maps, plan.Area.Bounds, 16, 16, new(), default);
        Assert.True(result.Rows >= 2);
        Assert.Contains((int)'人', result.RequiredCodepoints);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(maps, "poi", "manifest.json")));
        Assert.Equal(3, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("transparent", manifest.RootElement.GetProperty("text_background").GetString());
        Assert.False(manifest.RootElement.GetProperty("text_halo").GetBoolean());
        var index = Assert.Single(Directory.GetFiles(Path.Combine(maps, "poi", "index"), "*.jsonl", SearchOption.AllDirectories));
        var rows = (await File.ReadAllLinesAsync(index)).Select(s => JsonDocument.Parse(s)).ToArray();
        try
        {
            Assert.Contains(rows, r => r.RootElement.GetProperty("kind").GetString() == "road" && r.RootElement.GetProperty("path").GetArrayLength() >= 4);
            Assert.Contains(rows, r => r.RootElement.GetProperty("kind").GetString() == "poi" && r.RootElement.GetProperty("type").GetString() == "water");
            Assert.All(rows, r => Assert.Equal("人民路", r.RootElement.GetProperty("name").GetString()));
        }
        finally { foreach (var row in rows) row.Dispose(); }
    }
}
