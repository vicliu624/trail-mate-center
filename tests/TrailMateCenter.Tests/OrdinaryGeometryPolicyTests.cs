using System.Text.Json;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Tests;

public sealed class OrdinaryGeometryPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Center.GeometryPolicy", Guid.NewGuid().ToString("N"));
    public OrdinaryGeometryPolicyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MissingReferences_AreReportedWithoutFabricatingGeometryOrDiscardingValidRoad()
    {
        var pbf = Path.Combine(_root, "incomplete.osm.pbf");
        using (var output = File.Create(pbf))
        {
            var writer = new PBFOsmStreamTarget(output, true);
            writer.Initialize();
            for (var i = 1; i <= 2; i++) writer.AddNode(new Node
            {
                Id = i,
                Longitude = i * .01,
                Latitude = .01,
                Version = 1,
                Visible = true,
                ChangeSetId = 1,
                UserId = 1,
                UserName = "fixture",
                TimeStamp = DateTime.UnixEpoch,
                Tags = new TagsCollection(),
            });
            writer.AddWay(new Way { Id = 100, Version = 1, Nodes = [1, 2], Tags = new TagsCollection { { "highway", "primary" } } });
            writer.AddWay(new Way { Id = 300, Version = 1, Nodes = [1, 999], Tags = new TagsCollection { { "natural", "water" } } });
            writer.AddRelation(new Relation
            {
                Id = 200,
                Version = 1,
                Members = [new RelationMember { Id = 9999, Type = OsmGeoType.Way, Role = "outer" }],
                Tags = new TagsCollection { { "type", "multipolygon" }, { "landuse", "forest" } }
            });
            writer.Close();
        }
        using var store = new PbfBasemapStore(Path.Combine(_root, "geometry.sqlite"));
        store.Import(pbf, null, default, new GeoBounds(-1, -1, 1, 1), strictGeometry: false);
        Assert.Equal("road", Assert.Single(store.Query(new Envelope(0, 1, 0, 1), 18)).Kind);
        Assert.Equal(0, store.RepairCount);
        Assert.Equal(2, store.SourceIssueCount);
        Assert.Contains(store.SourceIssues, issue => issue.WayId == 300 && issue.KnownMemberBounds is null);
        Assert.Contains(store.SourceIssues, issue => issue.RelationId == 200 && issue.KnownMemberBounds is null);
    }

    [Fact]
    public async Task TmapDefaultExport_UsesTheSameRepairPolicyAndReport()
    {
        var plan = new MapPackExportPlan
        {
            OutputDirectory = _root,
            Area = new MapPackAreaSelection { Name = "Forêt de Loudéac", Bounds = new(-2.681, 48.239, -2.679, 48.241) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = 12, MaximumZoom = 12 },
            Poi = new MapPackPoiSelection
            {
                EnablePoiSeparation = true,
                PbfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OsmMultipolygons", "relation-6730991-v8.osm.pbf"),
                SelectedPoiTypes = ["water"]
            },
            Annotations = new MapAnnotationOptions(),
            Tmap = new TmapOptions { PackageKey = "test:geometry-policy", GenerateFontPacks = false },
        };
        await new TmapPackExporter().ExportAsync(plan);
        var reports = Directory.GetFiles(TmapLayout.Root(_root), "*.source-geometry.json");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Assert.Single(reports)));
        Assert.False(report.RootElement.GetProperty("StrictGeometry").GetBoolean());
        Assert.Equal(1, report.RootElement.GetProperty("RepairCount").GetInt32());
        Assert.Equal(6730991, report.RootElement.GetProperty("Repairs")[0].GetProperty("RelationId").GetInt64());
        Assert.NotEmpty(Directory.GetFiles(TmapLayout.Root(_root), "*.tmap", SearchOption.AllDirectories));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
