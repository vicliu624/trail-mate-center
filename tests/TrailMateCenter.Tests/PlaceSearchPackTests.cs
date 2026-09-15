using System.Text;
using System.Text.Json;
using TrailMateCenter.Maps;
using TrailMateCenter.Places;

namespace TrailMateCenter.Tests;

public sealed class PlaceSearchPackTests
{
    [Fact]
    public void PlaceNameNormalizer_CollectsPreferredNamesAndAliases()
    {
        var names = PlaceNameNormalizer.CollectNames(
            new Dictionary<string, string>
            {
                ["name"] = "Kunming",
                ["name:zh"] = "\u6606\u660E",
                ["alt_name"] = "Spring City; City of Eternal Spring",
                ["old_name"] = "Yunnanfu",
            },
            "zh");

        Assert.Equal(
            [
                "\u6606\u660E",
                "Kunming",
                "Spring City",
                "City of Eternal Spring",
                "Yunnanfu",
            ],
            names);
    }

    [Fact]
    public void PlaceNameNormalizer_NormalizesSearchText()
    {
        Assert.Equal("sao paulo cafe", PlaceNameNormalizer.NormalizeForSearch("S\u00E3o-Paulo Caf\u00E9"));
        Assert.Equal("\u6606\u660E", PlaceNameNormalizer.NormalizeForSearch("\u6606\u660E"));
    }

    [Fact]
    public void OsmPlaceTagMapper_MapsSettlementsAdminAndPoiFamilies()
    {
        var mapper = new OsmPlaceTagMapper();

        var city = mapper.Map(new Dictionary<string, string> { ["place"] = "city" });
        var admin = mapper.Map(new Dictionary<string, string>
        {
            ["boundary"] = "administrative",
            ["admin_level"] = "4",
        });
        var hospital = mapper.Map(new Dictionary<string, string> { ["amenity"] = "hospital" });
        var station = mapper.Map(new Dictionary<string, string> { ["railway"] = "station" });

        Assert.NotNull(city);
        Assert.Equal(PlaceCategories.Settlement, city.Category);
        Assert.Equal(900, city.Rank);

        Assert.NotNull(admin);
        Assert.Equal(PlaceCategories.Admin, admin.Category);
        Assert.Equal(920, admin.Rank);

        Assert.NotNull(hospital);
        Assert.Equal(PlaceCategories.Medical, hospital.Category);

        Assert.NotNull(station);
        Assert.Equal(PlaceCategories.Transport, station.Category);
    }

    [Fact]
    public async Task PlaceSearchPackWriter_WritesManifestPlacesNamesAndLicenses()
    {
        var root = CreateTempRoot();
        try
        {
            var writer = new PlaceSearchPackWriter();
            var summary = await writer.WriteStreamingAsync(
                root,
                new PlaceSearchPackManifestInput
                {
                    Area = new PlaceAreaInfo
                    {
                        Name = "Yunnan Field Area",
                        AdminLevel = 4,
                        Bounds = new GeoBounds(97, 21, 107, 30),
                    },
                    Source = new PlaceSourceInfo
                    {
                        Name = "yunnan-latest.osm.pbf",
                        Provider = "geofabrik",
                        DownloadUrl = "https://download.example/yunnan-latest.osm.pbf",
                    },
                    NameLanguage = "zh",
                },
                async onPlace =>
                {
                    await onPlace(new PlaceRecord
                    {
                        Id = "osm-node-1",
                        Category = PlaceCategories.Settlement,
                        PrimaryName = "\u6606\u660E",
                        Names = ["\u6606\u660E", "Kunming"],
                        Latitude = 25.0389,
                        Longitude = 102.7183,
                        Rank = 900,
                        Source = "osm",
                        OsmType = "node",
                        OsmId = 1,
                    });
                });

            Assert.StartsWith("yunnan-field-area-", summary.PackId, StringComparison.Ordinal);
            Assert.Equal(1, summary.PlaceCount);
            Assert.Equal(2, summary.NameRowsWritten);
            Assert.Equal(1, summary.CategoryCounts[PlaceCategories.Settlement]);

            var packRoot = Path.Combine(root, "places", "packs", summary.PackId);
            Assert.True(File.Exists(Path.Combine(packRoot, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(packRoot, "places.bin")));
            Assert.True(File.Exists(Path.Combine(packRoot, "names.bin")));
            Assert.True(File.Exists(Path.Combine(packRoot, "licenses.json")));
            Assert.True(File.Exists(Path.Combine(root, "places", "catalog.json")));

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packRoot, "manifest.json"), Encoding.UTF8));
            Assert.Equal("place-search-binary-v1", manifest.RootElement.GetProperty("format").GetString());
            Assert.Equal("places.bin", manifest.RootElement.GetProperty("files").GetProperty("places").GetString());
            Assert.Equal("names.bin", manifest.RootElement.GetProperty("files").GetProperty("names").GetString());
            Assert.Equal(512, manifest.RootElement.GetProperty("binary").GetProperty("max_string_bytes").GetInt32());
            Assert.True(manifest.RootElement.GetProperty("categories").GetArrayLength() > 0);
            Assert.Equal(1, manifest.RootElement.GetProperty("records").GetProperty("place_count").GetInt64());

            var placeHeader = ReadBinaryHeader(Path.Combine(packRoot, "places.bin"));
            Assert.Equal("TMPLREC1", placeHeader.Magic);
            Assert.Equal(1, placeHeader.Version);
            Assert.Equal(1UL, placeHeader.Count);

            var nameHeader = ReadBinaryHeader(Path.Combine(packRoot, "names.bin"));
            Assert.Equal("TMPLNAM1", nameHeader.Magic);
            Assert.Equal(1, nameHeader.Version);
            Assert.Equal(2UL, nameHeader.Count);

            var names = ReadNames(Path.Combine(packRoot, "names.bin"));
            Assert.Contains(names, static row => row.Normalized == "kunming");
            Assert.Contains(names, static row => row.DisplayName == "\u6606\u660E");

            using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "places", "catalog.json"), Encoding.UTF8));
            var catalogPack = catalog.RootElement.GetProperty("packs").EnumerateArray().Single();
            Assert.Equal(summary.PackId, catalogPack.GetProperty("pack_id").GetString());
            Assert.Equal($"packs/{summary.PackId}", catalogPack.GetProperty("path").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Magic, int Version, ulong Count) ReadBinaryHeader(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        var version = reader.ReadInt32();
        var count = reader.ReadUInt64();
        return (magic, version, count);
    }

    private static IReadOnlyList<(string DisplayName, string Normalized)> ReadNames(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        _ = Encoding.ASCII.GetString(reader.ReadBytes(8));
        _ = reader.ReadInt32();
        var count = reader.ReadUInt64();
        var rows = new List<(string DisplayName, string Normalized)>();

        for (ulong i = 0; i < count; i++)
        {
            _ = reader.ReadUInt64();
            _ = reader.ReadUInt64();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var displayName = ReadUtf8String(reader);
            var normalized = ReadUtf8String(reader);
            rows.Add((displayName, normalized));
        }

        return rows;
    }

    private static string ReadUtf8String(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
