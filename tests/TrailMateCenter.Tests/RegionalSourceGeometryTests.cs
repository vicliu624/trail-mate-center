using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class RegionalSourceGeometryTests
{
    [Fact]
    public void InnerOnlyRelationRequiresExplicitOmissionAndIsRecorded()
    {
        var root = Path.Combine(Path.GetTempPath(), "center-region-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pbf = Path.Combine(root, "source.osm.pbf");
            using (var stream = File.Create(pbf))
            {
                var writer = new PBFOsmStreamTarget(stream, true);
                writer.Initialize();
                foreach (var node in new[] { new Node { Id = 1, Latitude = 0.1, Longitude = 0.1 },
                    new Node { Id = 2, Latitude = 0.1, Longitude = 0.2 },
                    new Node { Id = 3, Latitude = 0.2, Longitude = 0.2 },
                    new Node { Id = 4, Latitude = 0.2, Longitude = 0.1 } })
                {
                    node.Version = 1; node.Visible = true; node.ChangeSetId = 1; node.UserId = 1;
                    node.UserName = "fixture"; node.TimeStamp = DateTime.UnixEpoch; node.Tags = new TagsCollection();
                    writer.AddNode(node);
                }
                writer.AddWay(new Way { Id = 10, Version = 1, Nodes = [1, 2, 3, 4, 1] });
                writer.AddRelation(new Relation
                {
                    Id = 20,
                    Version = 1,
                    Tags = new TagsCollection { { "type", "multipolygon" }, { "landuse", "grass" } },
                    Members = [new RelationMember { Id = 10, Type = OsmGeoType.Way, Role = "inner" }]
                });
                writer.Close();
            }
            using (var strict = new PbfBasemapStore(Path.Combine(root, "strict.sqlite")))
                Assert.Throws<InvalidDataException>(() => strict.Import(pbf, null, default, new GeoBounds(0, 0, 1, 1)));
            using var allowed = new PbfBasemapStore(Path.Combine(root, "allowed.sqlite"));
            allowed.Import(pbf, null, default, new GeoBounds(0, 0, 1, 1), skipDegenerateAreaRelations: true);
            Assert.Equal(1, allowed.SourceIssueCount);
            Assert.Equal(20, Assert.Single(allowed.SourceIssues).RelationId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
