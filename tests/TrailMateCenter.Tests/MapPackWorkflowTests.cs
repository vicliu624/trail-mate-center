using System.Net;
using System.Text;
using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.Tests;

public sealed class MapPackWorkflowTests
{
    [Fact]
    public void GeoJsonBoundaryImporter_ComputesBoundsFromPolygon()
    {
        const string geoJson = """
            {
              "type": "Feature",
              "properties": { "name": "Test Region" },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[10,55],[25,55],[25,69.5],[10,69.5],[10,55]]]
              }
            }
            """;

        var result = new GeoJsonBoundaryImporter().Import(geoJson);

        Assert.True(result.Success);
        Assert.Equal("Test Region", result.Name);
        Assert.Equal(10, result.Bounds.West);
        Assert.Equal(55, result.Bounds.South);
        Assert.Equal(25, result.Bounds.East);
        Assert.Equal(69.5, result.Bounds.North);
    }

    [Fact]
    public void GeoJsonPointInPolygonFilter_RespectsPolygonAndHoles()
    {
        const string geoJson = """
            {
              "type": "Polygon",
              "coordinates": [
                [[0,0],[10,0],[10,10],[0,10],[0,0]],
                [[4,4],[6,4],[6,6],[4,6],[4,4]]
              ]
            }
            """;

        var filter = GeoJsonPointInPolygonFilter.TryCreate(geoJson);

        Assert.NotNull(filter);
        Assert.True(filter.Contains(2, 2));
        Assert.False(filter.Contains(5, 5));
        Assert.False(filter.Contains(12, 12));
    }

    [Fact]
    public void GeofabrikCatalogProvider_ParsesCatalogFeatureAndPbfUrl()
    {
        const string catalog = """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": {
                    "id": "europe",
                    "name": "Europe",
                    "urls": { "pbf": "https://download.geofabrik.de/europe-latest.osm.pbf" }
                  },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[0,40],[30,40],[30,70],[0,70],[0,40]]]
                  }
                },
                {
                  "type": "Feature",
                  "properties": {
                    "id": "sweden",
                    "parent": "europe",
                    "name": "Sweden",
                    "urls": { "pbf": "https://download.geofabrik.de/europe/sweden-latest.osm.pbf" }
                  },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[10,55],[25,55],[25,69.5],[10,69.5],[10,55]]]
                  }
                }
              ]
            }
            """;

        using var document = JsonDocument.Parse(catalog);
        var regions = new GeofabrikCatalogProvider().ParseCatalog(document.RootElement);

        var sweden = Assert.Single(regions.Where(static r => r.Id == "sweden"));
        Assert.Equal("Europe / Sweden", sweden.DisplayName);
        Assert.Equal("https://download.geofabrik.de/europe/sweden-latest.osm.pbf", sweden.PbfUrl);
        Assert.Equal(10, sweden.Bounds.West);
    }

    [Fact]
    public async Task NominatimProvider_UsesCacheAfterFirstSearch()
    {
        var root = CreateTempRoot();
        try
        {
            var handler = new FakeHttpHandler("""
                [
                  {
                    "place_id": 1,
                    "osm_type": "relation",
                    "osm_id": 52822,
                    "name": "Sweden",
                    "display_name": "Sweden",
                    "boundingbox": ["55.0","69.5","10.0","25.0"],
                    "address": { "country_code": "se" },
                    "extratags": { "admin_level": "2" },
                    "geojson": { "type": "Polygon", "coordinates": [[[10,55],[25,55],[25,69.5],[10,69.5],[10,55]]] }
                  }
                ]
                """);
            var provider = new CachedNominatimAdminAreaProvider(
                new HttpClient(handler),
                root,
                new Uri("https://example.test/search"));

            var first = await provider.SearchAsync(new AdminAreaQuery { Text = "Sweden" });
            var second = await provider.SearchAsync(new AdminAreaQuery { Text = "Sweden" });

            Assert.False(first.FromCache);
            Assert.True(second.FromCache);
            Assert.Equal(1, handler.RequestCount);
            var area = Assert.Single(second.Areas);
            Assert.Equal(2, area.AdminLevel);
            Assert.Equal("se", area.CountryCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeofabrikDownloadService_WritesPbfAndMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            var handler = new FakeHttpHandler("pbf-bytes");
            var service = new GeofabrikPbfDownloadService(new HttpClient(handler), root);
            var entry = await service.DownloadAsync(new GeofabrikRegionRecord
            {
                Id = "sweden",
                DisplayName = "Europe / Sweden",
                PbfUrl = "https://download.example/europe/sweden-latest.osm.pbf",
            });

            Assert.True(File.Exists(entry.LocalPath));
            Assert.True(File.Exists($"{entry.LocalPath}.json"));
            Assert.Equal("Europe / Sweden", entry.RegionName);
            Assert.Equal(Encoding.UTF8.GetByteCount("pbf-bytes"), entry.SizeBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExportEstimator_CountsTilesAcrossZoomRange()
    {
        var estimate = new ExportEstimator().Estimate(new MapPackExportPlan
        {
            Area = new MapPackAreaSelection
            {
                Bounds = new GeoBounds(10, 55, 25, 69.5),
            },
            BaseLayers = new MapPackBaseLayerSelection
            {
                IncludeOsm = true,
                IncludeTerrain = true,
                MinimumZoom = 10,
                MaximumZoom = 12,
            },
        });

        Assert.True(estimate.TotalTileCount > 0);
        Assert.Contains(estimate.Layers, static l => l.Name == "OSM raster");
        Assert.Contains(estimate.Layers, static l => l.Name == "Terrain raster");
    }

    [Fact]
    public async Task PoiIndexWriter_StreamingWriterProducesIndexAndManifest()
    {
        var root = CreateTempRoot();
        try
        {
            var mapsRoot = Path.Combine(root, "maps");
            var writer = new PoiIndexWriter();
            var summary = await writer.WriteStreamingAsync(
                mapsRoot,
                new PoiManifestInput
                {
                    Source = new PoiSourceInfo { Name = "test.osm.pbf", Provider = "geofabrik" },
                    Area = new PoiAreaInfo { Name = "Sweden", AdminLevel = 2, Bounds = new GeoBounds(10, 55, 25, 69.5) },
                    Index = new PoiIndexOptions { MinZoom = 12, MaxZoom = 12, GenerateFullPoisJsonl = true, GenerateTileIndex = true },
                    PoiTypes = ["water"],
                },
                async onPoi =>
                {
                    await onPoi(new PoiRecord
                    {
                        Id = "osm-node-1",
                        Type = "water",
                        Name = "\u6C34",
                        Latitude = 59.123,
                        Longitude = 18.123,
                        Priority = 90,
                    });
                });

            Assert.Equal(1, summary.SourcePoiCount);
            Assert.Equal(1, summary.FullPoiRowsWritten);
            Assert.True(summary.TileFilesWritten > 0);
            Assert.True(File.Exists(Path.Combine(mapsRoot, "poi", "manifest.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _content;

        public FakeHttpHandler(string content)
        {
            _content = content;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
