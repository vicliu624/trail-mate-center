using Microsoft.Data.Sqlite;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;
using Xunit;

namespace TrailMateCenter.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task UpsertMessage_Preserves_TeamChat_Fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "store.db");

        try
        {
            var store = new SqliteStore(dbPath);
            await store.InitializeAsync(CancellationToken.None);

            var message = new MessageEntry
            {
                Direction = MessageDirection.Outgoing,
                From = "PC",
                To = "broadcast",
                ChannelId = 1,
                Channel = "1",
                Text = "team",
                Status = MessageDeliveryStatus.Succeeded,
                IsTeamChat = true,
                TeamConversationKey = "1122334455667788:11223344",
            };

            await store.UpsertMessageAsync(message, CancellationToken.None);

            var loaded = await store.LoadMessagesAsync(CancellationToken.None);
            var restored = Assert.Single(loaded);
            Assert.True(restored.IsTeamChat);
            Assert.Equal("1122334455667788:11223344", restored.TeamConversationKey);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapCacheRegions_Preserve_ZoomRange()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "store.db");

        try
        {
            var store = new SqliteStore(dbPath);
            await store.InitializeAsync(CancellationToken.None);

            var regions = new[]
            {
                new MapCacheRegionSettings
                {
                    Id = "region-1",
                    Name = "Kunming",
                    West = 102.0,
                    South = 24.0,
                    East = 103.0,
                    North = 25.0,
                    IncludeOsm = true,
                    IncludeTerrain = false,
                    IncludeSatellite = true,
                    IncludeContours = true,
                    IncludeUltraFineContours = true,
                    MinimumZoom = 6,
                    MaximumZoom = 14,
                },
            };

            await store.SaveMapCacheRegionsAsync(regions, CancellationToken.None);

            var loaded = await store.LoadMapCacheRegionsAsync(CancellationToken.None);
            var region = Assert.Single(loaded);
            Assert.Equal(6, region.MinimumZoom);
            Assert.Equal(14, region.MaximumZoom);
            Assert.True(region.IncludeOsm);
            Assert.False(region.IncludeTerrain);
            Assert.True(region.IncludeUltraFineContours);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapCacheRegions_Preserve_PoiExportSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "store.db");

        try
        {
            var store = new SqliteStore(dbPath);
            await store.InitializeAsync(CancellationToken.None);

            await store.SaveMapCacheRegionsAsync(
                new[]
                {
                    new MapCacheRegionSettings
                    {
                        Id = "poi-region",
                        Name = "Sweden POI",
                        West = 10,
                        South = 55,
                        East = 25,
                        North = 69.5,
                        AdminLevel = 2,
                        BoundaryGeoJson = """{"type":"Polygon","coordinates":[[[10,55],[25,55],[25,69.5],[10,69.5],[10,55]]]}""",
                        IncludeOsm = false,
                        IncludeTerrain = false,
                        IncludeSatellite = false,
                        IncludeContours = false,
                        EnablePoiSeparation = true,
                        PoiPbfPath = @"D:\osm\sweden-latest.osm.pbf",
                        PoiSourceProvider = "geofabrik",
                        PoiSourceDownloadUrl = "https://download.geofabrik.de/europe/sweden-latest.osm.pbf",
                        GenerateFullPoisJsonl = true,
                        GenerateTileIndexedPoiFiles = true,
                        PoiIndexMinimumZoom = 10,
                        PoiIndexMaximumZoom = 17,
                        MaxPoiPerTile = 150,
                        IncludePoiLabels = true,
                        IncludeOriginalOsmTags = true,
                        PoiOutputFormat = "compact",
                        SelectedPoiTypes = ["water", "camp", "shelter"],
                    },
                },
                CancellationToken.None);

            var loaded = await store.LoadMapCacheRegionsAsync(CancellationToken.None);
            var region = Assert.Single(loaded);
            Assert.Equal(2, region.AdminLevel);
            Assert.Contains("\"Polygon\"", region.BoundaryGeoJson);
            Assert.True(region.EnablePoiSeparation);
            Assert.Equal(@"D:\osm\sweden-latest.osm.pbf", region.PoiPbfPath);
            Assert.Equal("geofabrik", region.PoiSourceProvider);
            Assert.Equal("https://download.geofabrik.de/europe/sweden-latest.osm.pbf", region.PoiSourceDownloadUrl);
            Assert.False(region.IncludeOsm);
            Assert.Equal(10, region.PoiIndexMinimumZoom);
            Assert.Equal(17, region.PoiIndexMaximumZoom);
            Assert.Equal(150, region.MaxPoiPerTile);
            Assert.True(region.IncludeOriginalOsmTags);
            Assert.Equal("compact", region.PoiOutputFormat);
            Assert.Equal(["camp", "shelter", "water"], region.SelectedPoiTypes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapCacheRegions_Preserve_ExportTaskSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "store.db");

        try
        {
            var store = new SqliteStore(dbPath);
            await store.InitializeAsync(CancellationToken.None);

            await store.SaveMapCacheRegionsAsync(
                new[]
                {
                    new MapCacheRegionSettings
                    {
                        Id = "export-region",
                        Name = "Kunming",
                        West = 102.16798,
                        South = 24.38890,
                        East = 103.66897,
                        North = 26.54848,
                        ExportOutputDirectory = @"C:\maps\trail_mate_maps",
                        ExportState = "partial",
                        ExportProcessedTiles = 980,
                        ExportExpectedTiles = 1000,
                        ExportSourceTiles = 900,
                        ExportCopiedTiles = 850,
                        ExportSkippedTiles = 40,
                        ExportMissingTiles = 100,
                        ExportUnreadableEntries = 2,
                        ExportLastError = "network timeout",
                        ExportUpdatedAtUnixTime = 1_788_888_888,
                    },
                },
                CancellationToken.None);

            var loaded = await store.LoadMapCacheRegionsAsync(CancellationToken.None);
            var region = Assert.Single(loaded);
            Assert.Equal(@"C:\maps\trail_mate_maps", region.ExportOutputDirectory);
            Assert.Equal("partial", region.ExportState);
            Assert.Equal(980, region.ExportProcessedTiles);
            Assert.Equal(1000, region.ExportExpectedTiles);
            Assert.Equal(900, region.ExportSourceTiles);
            Assert.Equal(850, region.ExportCopiedTiles);
            Assert.Equal(40, region.ExportSkippedTiles);
            Assert.Equal(100, region.ExportMissingTiles);
            Assert.Equal(2, region.ExportUnreadableEntries);
            Assert.Equal("network timeout", region.ExportLastError);
            Assert.Equal(1_788_888_888, region.ExportUpdatedAtUnixTime);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
