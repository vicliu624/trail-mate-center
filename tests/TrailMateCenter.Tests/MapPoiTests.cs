using System.Text;
using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Osm;

namespace TrailMateCenter.Tests;

public sealed class MapPoiTests
{
    [Fact]
    public void LonLatToTile_UsesXyzWebMercator()
    {
        var tile = TileMath.LonLatToTile(18.12345, 59.12345, 12);

        Assert.Equal(12, tile.Z);
        Assert.Equal(2254, tile.X);
        Assert.Equal(1209, tile.Y);
    }

    [Fact]
    public void OsmTagMapper_MapsOutdoorPoiTags()
    {
        var mapper = new OsmTagMapper();
        var result = mapper.Map(new Dictionary<string, string>
        {
            ["amenity"] = "drinking_water",
        });

        Assert.NotNull(result);
        Assert.Equal("water", result.Type);
        Assert.Equal(90, result.Priority);
    }

    [Fact]
    public void GeoBounds_ContainsFiltersByBbox()
    {
        var bounds = new GeoBounds(10, 55, 25, 69.5);

        Assert.True(bounds.Contains(59.12345, 18.12345));
        Assert.False(bounds.Contains(52.52, 13.405));
    }

    [Fact]
    public void PoiPriorityRules_FilterLowZoomByPriority()
    {
        var rules = PoiPriorityRules.Default;
        var water = new PoiRecord { Id = "a", Type = "water", Priority = 90, Latitude = 59, Longitude = 18 };
        var toilet = new PoiRecord { Id = "b", Type = "toilet", Priority = 60, Latitude = 59, Longitude = 18 };

        Assert.True(rules.ShouldIncludeAtZoom(water, 10));
        Assert.False(rules.ShouldIncludeAtZoom(toilet, 10));
        Assert.True(rules.ShouldIncludeAtZoom(toilet, 15));
    }

    [Fact]
    public async Task PoiIndexWriter_WritesManifestFullJsonlAndTileIndex()
    {
        var root = CreateTempRoot();
        try
        {
            var mapsRoot = Path.Combine(root, "maps");
            var writer = new PoiIndexWriter();
            var summary = await writer.WriteAsync(
                mapsRoot,
                new[]
                {
                    new PoiRecord
                    {
                        Id = "osm-node-123",
                        Type = "water",
                        Name = "Spring",
                        Latitude = 59.12345,
                        Longitude = 18.12345,
                        Priority = 90,
                        Source = "osm",
                        Tags = new Dictionary<string, string> { ["amenity"] = "drinking_water" },
                    },
                },
                BuildManifestInput());

            Assert.Equal(1, summary.SourcePoiCount);
            Assert.Equal(1, summary.FullPoiRowsWritten);
            Assert.True(summary.TileFilesWritten > 0);
            Assert.True(File.Exists(Path.Combine(mapsRoot, "poi", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(mapsRoot, "poi", "pois.jsonl")));

            var tile = TileMath.LonLatToTile(18.12345, 59.12345, 12);
            var indexPath = Path.Combine(
                mapsRoot,
                "poi",
                "index",
                "12",
                tile.X.ToString(),
                $"{tile.Y}.jsonl");
            Assert.True(File.Exists(indexPath));

            var line = File.ReadLines(indexPath, Encoding.UTF8).Single();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("osm-node-123", doc.RootElement.GetProperty("id").GetString());
            Assert.Equal("water", doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PoiIndexWriter_PreservesNonAsciiNamesAsUtf8()
    {
        var root = CreateTempRoot();
        var poiName = "\u0413\u043E\u0440\u0430 \u00C5reskutan \u5C71";
        try
        {
            var mapsRoot = Path.Combine(root, "maps");
            var writer = new PoiIndexWriter();
            await writer.WriteAsync(
                mapsRoot,
                new[]
                {
                    new PoiRecord
                    {
                        Id = "osm-node-ru",
                        Type = "peak",
                        Name = poiName,
                        Latitude = 63.43,
                        Longitude = 13.08,
                        Priority = 70,
                    },
                },
                BuildManifestInput(types: ["peak"], minZoom: 15, maxZoom: 15));

            var jsonl = File.ReadAllText(Path.Combine(mapsRoot, "poi", "pois.jsonl"), Encoding.UTF8);
            Assert.Contains(poiName, jsonl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PoiIndexWriter_EmptyPoiSetWritesManifestButNoIndexFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var mapsRoot = Path.Combine(root, "maps");
            var writer = new PoiIndexWriter();
            var summary = await writer.WriteAsync(
                mapsRoot,
                Array.Empty<PoiRecord>(),
                BuildManifestInput());

            Assert.Equal(0, summary.SourcePoiCount);
            Assert.True(File.Exists(Path.Combine(mapsRoot, "poi", "manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(mapsRoot, "poi", "index")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PoiIndexWriter_ClipsLargeTileByPriority()
    {
        var root = CreateTempRoot();
        try
        {
            var mapsRoot = Path.Combine(root, "maps");
            var pois = Enumerable.Range(0, 5)
                .Select(i => new PoiRecord
                {
                    Id = $"poi-{i}",
                    Type = "water",
                    Name = $"Poi {i}",
                    Latitude = 59.12345 + i * 0.000001,
                    Longitude = 18.12345 + i * 0.000001,
                    Priority = 90 - i,
                })
                .ToArray();

            var writer = new PoiIndexWriter();
            var summary = await writer.WriteAsync(
                mapsRoot,
                pois,
                BuildManifestInput(maxPerTile: 2, minZoom: 15, maxZoom: 15));

            Assert.True(summary.WasAnyTileClipped);
            Assert.Equal(1, summary.TileFilesWritten);

            var tile = TileMath.LonLatToTile(18.12345, 59.12345, 15);
            var indexFile = Path.Combine(
                mapsRoot,
                "poi",
                "index",
                "15",
                tile.X.ToString(),
                $"{tile.Y}.jsonl");
            var lines = File.ReadAllLines(indexFile, Encoding.UTF8);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"poi-0\"", lines[0]);
            Assert.Contains("\"poi-1\"", lines[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PoiIndexWriter_SerializesCompactOutput()
    {
        var writer = new PoiIndexWriter();
        var line = writer.SerializePoi(
            new PoiRecord
            {
                Id = "n123",
                Type = "water",
                Name = "Spring",
                Latitude = 59.12345,
                Longitude = 18.12345,
                Priority = 90,
            },
            new PoiIndexOptions { OutputFormat = PoiOutputFormat.Compact },
            forIndex: true);

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("water", doc.RootElement.GetProperty("t").GetString());
        Assert.Equal("Spring", doc.RootElement.GetProperty("n").GetString());
        Assert.Equal(90, doc.RootElement.GetProperty("p").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("type", out _));
    }

    [Fact]
    public void BuildManifest_ContainsIndexAndAreaMetadata()
    {
        var writer = new PoiIndexWriter();
        var manifest = writer.BuildManifest(
            BuildManifestInput(types: ["water", "camp"], minZoom: 10, maxZoom: 17),
            new PoiIndexWriteSummary
            {
                SourcePoiCount = 2,
                TileFilesWritten = 1,
                WasAnyTileClipped = true,
                ClippedTileCountByZoom = new Dictionary<int, long> { [12] = 1 },
            },
            DateTimeOffset.Parse("2026-06-12T00:00:00Z"));

        Assert.Equal(1, manifest.Version);
        Assert.Equal("TrailMateCenter", manifest.Generator);
        Assert.Equal("web-mercator-xyz", manifest.Index.Scheme);
        Assert.Equal(10, manifest.Index.MinZoom);
        Assert.Equal(17, manifest.Index.MaxZoom);
        Assert.True(manifest.Index.ClippedTiles);
        Assert.Contains("water", manifest.PoiTypes);
        Assert.Equal("Sweden", manifest.Area.Name);
    }

    private static PoiManifestInput BuildManifestInput(
        IReadOnlyCollection<string>? types = null,
        int minZoom = 12,
        int maxZoom = 12,
        int maxPerTile = 200)
    {
        return new PoiManifestInput
        {
            Source = new PoiSourceInfo
            {
                Name = "sweden-latest.osm.pbf",
                Provider = "local",
            },
            Area = new PoiAreaInfo
            {
                Name = "Sweden",
                AdminLevel = 2,
                Bounds = new GeoBounds(10, 55, 25, 69.5),
            },
            Index = new PoiIndexOptions
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom,
                MaxPoiPerTile = maxPerTile,
                IncludeOriginalTags = true,
                GenerateFullPoisJsonl = true,
                GenerateTileIndex = true,
            },
            PoiTypes = types ?? new[] { "water" },
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
