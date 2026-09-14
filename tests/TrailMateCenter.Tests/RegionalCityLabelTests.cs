using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class RegionalCityLabelTests
{
    [Fact]
    public async Task ActualCityInsideRequestedAreaLabelsEveryLowZoom()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pbf = Path.Combine(root, "city.osm.pbf");
            using (var stream = File.Create(pbf))
            {
                var target = new PBFOsmStreamTarget(stream, true);
                target.Initialize();
                target.AddNode(new Node { Id = 1, Version = 1, Latitude = 25.04, Longitude = 102.71,
                    Visible = true, ChangeSetId = 1, UserId = 1, UserName = "fixture", TimeStamp = DateTime.UnixEpoch,
                    Tags = new TagsCollection(new Tag("name", "昆明市"), new Tag("place", "city"), new Tag("amenity", "drinking_water")) });
                target.AddNode(new Node { Id = 2, Version = 1, Latitude = 25.041, Longitude = 102.711,
                    Visible = true, ChangeSetId = 1, UserId = 1, UserName = "fixture", TimeStamp = DateTime.UnixEpoch });
                target.AddWay(new Way { Id = 1, Version = 1, Nodes = [1, 2], Tags = new TagsCollection(new Tag("highway", "motorway")) });
                target.Flush();
                target.Close();
            }
            var plan = new MapPackExportPlan
            {
                OutputDirectory = Path.Combine(root, "labelled"),
                Area = new MapPackAreaSelection { Bounds = new(102.709, 25.039, 102.712, 25.042) },
                BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 1, MaximumZoom = 3 },
                Poi = new MapPackPoiSelection { EnablePoiSeparation = true, PbfPath = pbf, SelectedPoiTypes = ["water"],
                    IndexOptions = new PoiIndexOptions { EnabledZoomLevels = [] } },
            };
            var labelled = await new LocalMapPackExporter().ExportAsync(plan);
            var clean = await new LocalMapPackExporter().ExportAsync(plan with
            {
                OutputDirectory = Path.Combine(root, "clean"),
                Poi = plan.Poi with { IndexOptions = plan.Poi.IndexOptions with { EnabledZoomLevels = [1, 2, 3] } },
            });
            for (var z = 1; z <= 3; z++)
            {
                var tile = Assert.Single(Directory.GetFiles(Path.Combine(labelled.MapsRoot, "base", "osm", z.ToString()), "*.png", SearchOption.AllDirectories));
                var unlabelled = Path.Combine(clean.MapsRoot, Path.GetRelativePath(labelled.MapsRoot, tile));
                Assert.NotEqual(await File.ReadAllBytesAsync(tile), await File.ReadAllBytesAsync(unlabelled));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
