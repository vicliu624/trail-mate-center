using System.Text.Json;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class MultipolygonRoleInferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Center.RoleInference", Guid.NewGuid().ToString("N"));

    public MultipolygonRoleInferenceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("inner", "inner", "inner", false)]
    [InlineData("inner", "outer", "inner", false)]
    [InlineData("outer", "inner", "outer", false)]
    [InlineData("outer", "inner", "outer", true)]
    public void NestedIslandAndHole_AreDefinedByContainmentEvenWithWrongOrSplitRoles(string shellRole, string holeRole, string islandRole, bool split)
    {
        var pbf = Path.Combine(_root, "nested.osm.pbf");
        using (var output = File.Create(pbf))
        {
            var writer = new PBFOsmStreamTarget(output, true);
            writer.Initialize();
            var coordinates = new[] { (0d, 0d), (10d, 0d), (10d, 10d), (0d, 10d), (2d, 2d), (8d, 2d), (8d, 8d), (2d, 8d), (4d, 4d), (6d, 4d), (6d, 6d), (4d, 6d) };
            for (var i = 0; i < coordinates.Length; i++)
                writer.AddNode(new Node
                {
                    Id = i + 1,
                    Longitude = coordinates[i].Item1,
                    Latitude = coordinates[i].Item2,
                    Version = 1,
                    Visible = true,
                    ChangeSetId = 1,
                    UserId = 1,
                    UserName = "fixture",
                    TimeStamp = DateTime.UnixEpoch,
                    Tags = new TagsCollection()
                });
            writer.AddWay(new Way { Id = 100, Nodes = split ? [1, 2, 3] : [1, 2, 3, 4, 1], Version = 1 });
            writer.AddWay(new Way { Id = 101, Nodes = [5, 6, 7, 8, 5], Version = 1 });
            writer.AddWay(new Way { Id = 102, Nodes = [9, 10, 11, 12, 9], Version = 1 });
            var members = new List<RelationMember>
            {
                new() { Id = 100, Type = OsmGeoType.Way, Role = shellRole },
                new() { Id = 101, Type = OsmGeoType.Way, Role = holeRole },
                new() { Id = 102, Type = OsmGeoType.Way, Role = islandRole },
            };
            if (split)
            {
                writer.AddWay(new Way { Id = 103, Nodes = [3, 4, 1], Version = 1 });
                members.Add(new RelationMember { Id = 103, Type = OsmGeoType.Way, Role = "inner" });
            }
            writer.AddRelation(new Relation { Id = 200, Version = 1, Members = members.ToArray(), Tags = new TagsCollection { { "type", "multipolygon" }, { "natural", "water" } } });
            writer.Close();
        }
        using var store = new PbfBasemapStore(Path.Combine(_root, "geometry.sqlite"));
        store.Import(pbf, null, default, new GeoBounds(-1, -1, 11, 11));
        var water = Assert.Single(store.Query(new Envelope(0, 1, 0, 1), 18)).Geometry;
        Assert.True(water.IsValid);
        Assert.Equal(2, water.NumGeometries);
        Assert.Equal(1, Enumerable.Range(0, water.NumGeometries).Sum(i => ((Polygon)water.GetGeometryN(i)).NumInteriorRings));
        foreach (var (longitude, latitude, expected) in new[] { (1d, 1d, true), (3d, 3d, false), (5d, 5d, true), (11d, 5d, false) })
        {
            var (x, y) = PbfBasemapStore.Project(longitude, latitude);
            Assert.Equal(expected, water.Covers(new Point(x, y)));
        }
        Assert.Equal(0, store.SourceIssueCount);
    }

    [Fact]
    public async Task RecoveredRealRelation_IsReportedAsRoleCorrectionRatherThanOmission()
    {
        var result = await new LocalMapPackExporter().ExportAsync(new MapPackExportPlan
        {
            OutputDirectory = Path.Combine(_root, "export"),
            Area = new MapPackAreaSelection { Name = "Étang du Bois d'Amour", Bounds = new(-2.8445955, 47.8885384, -2.8443522, 47.8887292) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 16, MaximumZoom = 16 },
            Poi = new MapPackPoiSelection { EnablePoiSeparation = true, PbfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", "relation-7815642-v4.osm.pbf"), SelectedPoiTypes = ["water"] },
            Annotations = new MapAnnotationOptions(),
        });
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.MapsRoot, "source-role-corrections.json")));
        Assert.Equal(1, report.RootElement.GetProperty("corrected_relations").GetInt32());
        Assert.Equal(7815642, report.RootElement.GetProperty("examples")[0].GetProperty("RelationId").GetInt64());
        Assert.Equal(1, report.RootElement.GetProperty("examples")[0].GetProperty("MemberCount").GetInt32());
        Assert.False(File.Exists(Path.Combine(result.MapsRoot, "source-geometry-warnings.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrokenRealBoundary_IsRejectedEvenWhenDegenerateOmissionIsExplicitlyEnabled(bool skipDegenerate)
    {
        using var store = new PbfBasemapStore(Path.Combine(_root, "strict.sqlite"));
        var error = Assert.Throws<InvalidDataException>(() => store.Import(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", "relation-6730991-v8.osm.pbf"),
            null, default, new GeoBounds(-5.1440329, 47.277755, -1.01569, 49.1133044), skipDegenerateAreaRelations: skipDegenerate));
        Assert.Contains("6730991 has incomplete polygon rings", error.Message);
        Assert.Equal(0, store.SourceIssueCount);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
