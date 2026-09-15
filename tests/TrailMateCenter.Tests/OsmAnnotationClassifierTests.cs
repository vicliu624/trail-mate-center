using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.Tests;

public sealed class OsmAnnotationClassifierTests
{
    [Fact]
    public void StreetIdentityIsDistinctFromFacilityPoi()
    {
        var street = OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("highway", "residential"), new Tag("name", "人民路")));
        Assert.NotNull(street);
        Assert.Equal(MapAnnotationKind.Road, street.Rule.Kind);
        Assert.Equal("人民路", street.Name);
        Assert.Equal(15, street.Rule.MinimumZoom);
        var toilet = OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("amenity", "toilets")));
        Assert.NotNull(toilet);
        Assert.Equal(MapAnnotationKind.Poi, toilet.Rule.Kind);
        Assert.Empty(toilet.Name);
    }

    [Fact]
    public void PlaceAndBuildingNamesAreNotLostWhenTheyAreNotInTheOldPoiList()
    {
        var city = OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("place", "city"), new Tag("name", "昆明市")), regionFocus: true);
        Assert.Equal("region_focus", city!.Rule.Category);
        Assert.Equal(1, city.Rule.MinimumZoom);
        var building = OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("building", "yes"), new Tag("name", "图书馆")));
        Assert.Equal(MapAnnotationKind.Place, building!.Rule.Kind);
        Assert.Equal("building", building.Rule.Category);
    }

    [Fact]
    public void UnknownOrUnnamedRoadDoesNotBecomeAnInventedLabel()
    {
        Assert.Null(OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("highway", "residential"))));
        Assert.Null(OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("name", "unclassified object"))));
        var road = OsmAnnotationClassifier.Classify(new TagsCollection(new Tag("highway", "motorway_link"), new Tag("ref", "G56")));
        Assert.Equal("motorway", road!.Rule.Category);
        Assert.Equal("G56", road.Reference);
    }
}
