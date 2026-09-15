using TrailMateCenter.Maps;

namespace TrailMateCenter.Tests;

public sealed class MapAnnotationPolicyTests
{
    [Fact]
    public void TurningOffFacilitiesDoesNotHideRoadOrPlaceNames()
    {
        var policy = new MapAnnotationOptions { PoisEnabled = false };
        Assert.True(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Road, "residential")!, 16, policy));
        Assert.True(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Place, "city")!, 16, policy));
        Assert.False(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Poi, "water")!, 16, policy));
    }

    [Fact]
    public void AllZoomsHaveGeographicalContextButMicroFeaturesWaitForDetailZooms()
    {
        var policy = new MapAnnotationOptions();
        for (var zoom = 1; zoom <= 18; zoom++)
            Assert.Contains(MapAnnotationPolicy.Rules, r => r.Kind == MapAnnotationKind.Place && MapAnnotationPolicy.IsEligible(r, zoom, policy));
        Assert.False(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Poi, "emergency")!, 5, policy));
        Assert.False(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Road, "service")!, 16, policy));
        Assert.True(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Road, "service")!, 18, policy));
        for (var zoom = 16; zoom <= 18; zoom++)
            Assert.True(MapAnnotationPolicy.IsEligible(MapAnnotationPolicy.Find(MapAnnotationKind.Road, "residential")!, zoom, policy));
    }

    [Fact]
    public void PresetAndGroupZoomLimitsAreIndependent()
    {
        var shop = MapAnnotationPolicy.Find(MapAnnotationKind.Poi, "shop")!;
        Assert.False(MapAnnotationPolicy.IsEligible(shop, 18, new()));
        Assert.True(MapAnnotationPolicy.IsEligible(shop, 18, new() { Preset = MapAnnotationPreset.Urban }));
        Assert.False(MapAnnotationPolicy.IsEligible(shop, 18, new() { Preset = MapAnnotationPreset.Urban, PoiZooms = new(12, 16) }));
        Assert.Null(MapAnnotationPolicy.RoadText("人民路", null, 9));
        Assert.Equal("G108", MapAnnotationPolicy.RoadText("人民路", "G108", 9));
        Assert.Equal("人民路", MapAnnotationPolicy.RoadText("人民路", "G108", 16));
    }
}
