using System.Text.Json;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using SkiaSharp;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Rendering;

namespace TrailMateCenter.Tests;

public sealed class LocalMapPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
    private static readonly TileCoordinate Tile = new(16, 36064, 19335);

    public LocalMapPackTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Export_RealPbfProducesPngAndIndependentUtf8PoiInSdLayout()
    {
        var plan = Plan("first", "饮水点");
        var result = await new LocalMapPackExporter().ExportAsync(plan);
        Assert.Equal(1, result.TileCount);
        Assert.True(result.Poi.Success);
        using var bitmap = SKBitmap.Decode(TilePath(result.MapsRoot));
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        Assert.NotEqual(bitmap.GetPixel(5, 5), bitmap.GetPixel(128, 80)); // crossing road
        Assert.Equal(SKColor.Parse("#91c9e5"), bitmap.GetPixel(40, 140)); // lake
        Assert.Equal(bitmap.GetPixel(5, 5), bitmap.GetPixel(80, 175)); // relation's island/hole
        Assert.Contains("饮水点", await File.ReadAllTextAsync(Path.Combine(result.MapsRoot, "poi", "pois.jsonl")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(result.MapsRoot, "poi", "index"), "*.jsonl", SearchOption.AllDirectories));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.MapsRoot, "map-pack.json")));
        Assert.False(manifest.RootElement.GetProperty("text_in_basemap").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("poi_symbols_in_basemap").GetBoolean());
        Assert.Contains("OpenStreetMap", manifest.RootElement.GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task LabelsAndPoiSymbols_NeverAffectRasterPixels()
    {
        var first = await new LocalMapPackExporter().ExportAsync(Plan("first", "Alpha road and water"));
        var second = await new LocalMapPackExporter().ExportAsync(Plan("second", "道路名称与水源"));
        Assert.Equal(await File.ReadAllBytesAsync(TilePath(first.MapsRoot)), await File.ReadAllBytesAsync(TilePath(second.MapsRoot)));
        Assert.NotEqual(await File.ReadAllTextAsync(Path.Combine(first.MapsRoot, "poi", "pois.jsonl")),
            await File.ReadAllTextAsync(Path.Combine(second.MapsRoot, "poi", "pois.jsonl")));
    }

    [Fact]
    public async Task ReplacePack_RemovesStaleTilesAndRetainsRecoverablePreviousPack()
    {
        var plan = Plan("replace", "Water");
        var old = Path.Combine(plan.OutputDirectory, "maps");
        Directory.CreateDirectory(Path.Combine(old, "base", "terrain"));
        await File.WriteAllTextAsync(Path.Combine(old, "base", "terrain", "old-labelled.png"), "old data");
        var result = await new LocalMapPackExporter().ExportAsync(plan);
        Assert.False(Directory.Exists(Path.Combine(result.MapsRoot, "base", "terrain")));
        Assert.True(File.Exists(Path.Combine(result.PreviousMapsRoot!, "base", "terrain", "old-labelled.png")));
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".map-pack-*"));
    }

    [Fact]
    public async Task FailureAfterRendering_DoesNotPublishPartialPack()
    {
        var original = Plan("failure", "Water");
        var plan = original with { BaseLayers = original.BaseLayers with { IncludeContours = true } };
        var old = Path.Combine(plan.OutputDirectory, "maps");
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "keep.txt"), "old map");
        await Assert.ThrowsAsync<IOException>(() => new LocalMapPackExporter().ExportAsync(plan,
            addContours: (_, _) => throw new IOException("missing contours")));
        Assert.Equal("old map", await File.ReadAllTextAsync(Path.Combine(old, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(old, "map-pack.json")));
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".map-pack-*"));
    }

    [Fact]
    public async Task CancelDuringRendering_DoesNotReplaceExistingPack()
    {
        var plan = Plan("cancel", "Water");
        var old = Path.Combine(plan.OutputDirectory, "maps");
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "keep.txt"), "old map");
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(p => { if (p.Stage == "tiles") cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalMapPackExporter().ExportAsync(plan, progress, cancellationToken: cts.Token));
        Assert.True(File.Exists(Path.Combine(old, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(plan.OutputDirectory, ".map-pack-*"));
    }

    [Fact]
    public async Task InvalidInputs_FailBeforeTouchingDestination()
    {
        var valid = Plan("invalid", "Water");
        var satellite = valid with { BaseLayers = valid.BaseLayers with { IncludeSatellite = true } };
        await Assert.ThrowsAsync<ArgumentException>(() => new LocalMapPackExporter().ExportAsync(satellite));
        var missing = valid with { Poi = valid.Poi with { PbfPath = "does-not-exist.pbf" } };
        await Assert.ThrowsAsync<FileNotFoundException>(() => new LocalMapPackExporter().ExportAsync(missing));
        var noIndex = valid with { Poi = valid.Poi with { GenerateTileIndex = false } };
        await Assert.ThrowsAsync<ArgumentException>(() => new LocalMapPackExporter().ExportAsync(noIndex));
        Assert.False(Directory.Exists(valid.OutputDirectory));
    }

    [Fact]
    public async Task MapsDirectoryAsDestination_DoesNotNestMapsTwice()
    {
        var valid = Plan("direct", "Water");
        var result = await new LocalMapPackExporter().ExportAsync(valid with { OutputDirectory = Path.Combine(valid.OutputDirectory, "maps") });
        Assert.True(File.Exists(TilePath(result.MapsRoot)));
        Assert.False(Directory.Exists(Path.Combine(result.MapsRoot, "maps")));
    }

    internal MapPackExportPlan Plan(string name, string label)
    {
        var pbf = Path.Combine(_root, name + ".osm.pbf");
        WriteFixture(pbf, label);
        var b = TileMath.TileToBounds(Tile.X, Tile.Y, Tile.Z);
        return new MapPackExportPlan
        {
            OutputDirectory = Path.Combine(_root, name),
            Area = new MapPackAreaSelection { Name = name, Bounds = new(b.West + 0.000001, b.South + 0.000001, b.East - 0.000001, b.North - 0.000001) },
            BaseLayers = new MapPackBaseLayerSelection { IncludeOsm = true, MinimumZoom = Tile.Z, MaximumZoom = Tile.Z },
            Poi = new MapPackPoiSelection
            {
                EnablePoiSeparation = true, PbfPath = pbf, SelectedPoiTypes = ["water"],
                IndexOptions = new PoiIndexOptions { MinZoom = Tile.Z, MaxZoom = Tile.Z },
            },
        };
    }

    private static string TilePath(string maps) => Path.Combine(maps, "base", "osm", Tile.Z.ToString(), Tile.X.ToString(), $"{Tile.Y}.png");

    private static void WriteFixture(string path, string label)
    {
        using var output = File.Create(path);
        var target = new PBFOsmStreamTarget(output, true);
        target.Initialize();
        var pixels = new (double X, double Y)[]
        {
            (-20,80), (276,80), (30,130), (150,130), (150,225), (30,225),
            (60,160), (100,160), (100,190), (60,190), (200,50),
        };
        for (var i = 0; i < pixels.Length; i++)
        {
            var x = (Tile.X + pixels[i].X / 256) / (1 << Tile.Z);
            var y = (Tile.Y + pixels[i].Y / 256) / (1 << Tile.Z);
            target.AddNode(new Node
            {
                Id = i + 1, Longitude = x * 360 - 180,
                Latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI,
                Version = 1, Visible = true, ChangeSetId = 1, UserId = 1, UserName = "fixture", TimeStamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Tags = i == 10 ? new TagsCollection(new Tag("amenity", "drinking_water"), new Tag("name", label)) : new TagsCollection(),
            });
        }
        target.AddWay(new Way { Id = 100, Nodes = [1,2], Version = 1, Tags = new TagsCollection(new Tag("highway", "primary"), new Tag("name", label)) });
        target.AddWay(new Way { Id = 101, Nodes = [3,4,5,6,3], Version = 1, Tags = new TagsCollection(new Tag("natural", "water")) });
        target.AddWay(new Way { Id = 102, Nodes = [7,8,9,10,7], Version = 1, Tags = new TagsCollection() });
        target.AddRelation(new Relation
        {
            Id = 200, Version = 1,
            Tags = new TagsCollection(new Tag("type", "multipolygon"), new Tag("natural", "water"), new Tag("name", label)),
            Members = [new RelationMember { Id = 101, Type = OsmGeoType.Way, Role = "outer" }, new RelationMember { Id = 102, Type = OsmGeoType.Way, Role = "inner" }],
        });
        target.Flush();
        target.Close();
    }

    private sealed class InlineProgress(Action<LocalMapPackProgress> report) : IProgress<LocalMapPackProgress>
    {
        public void Report(LocalMapPackProgress value) => report(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
