using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationPreviewTests
{
    [Fact]
    public async Task PreviewUsesLocalGeometryAndIndependentRoadPolicy()
    {
        using var fixture = new LocalMapPackTests();
        var plan = fixture.Plan("preview-local", "人民路");
        var result = await MapAnnotationPreviewRenderer.RenderAsync(plan.Poi.PbfPath, plan.Area.Bounds, 16, new() { PoisEnabled = false });
        Assert.True(result.RoadLabels > 0);
        Assert.Equal(0, result.PoiLabels);
        using var bitmap = SKBitmap.Decode(result.Png);
        Assert.Equal(480, bitmap.Width);
        Assert.Equal(222, bitmap.Height);
        var disabled = await MapAnnotationPreviewRenderer.RenderAsync(plan.Poi.PbfPath, plan.Area.Bounds, 16,
            new() { RoadsEnabled = false, PlacesEnabled = false, PoisEnabled = false });
        Assert.Equal(0, disabled.RoadLabels);
        Assert.False(result.Png.SequenceEqual(disabled.Png));
    }
}
