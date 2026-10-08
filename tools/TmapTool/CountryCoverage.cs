using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using TrailMateCenter.Maps;

internal sealed class CountryCoverage
{
    private readonly GeometryFactory _factory = new();
    private readonly IPreparedGeometry _prepared;
    public GeoBounds Bounds { get; }
    public string[] SourceFiles { get; }
    public CountryCoverage(string directory)
    {
        SourceFiles = [Path.Combine(directory, "china.poly"), Path.Combine(directory, "taiwan.poly")];
        var polygons = new List<Polygon>(); var geographic = new Envelope();
        foreach (var path in SourceFiles)
        {
            using var file = File.OpenText(path); _ = file.ReadLine(); string? line;
            while ((line = file.ReadLine()) is not null)
            {
                line = line.Trim(); if (line == "END") break;
                if (line.Length == 0) continue;
                if (line.StartsWith('!')) throw new InvalidDataException("Coverage polygons with hole rings require explicit handling.");
                var coordinates = new List<Coordinate>();
                while ((line = file.ReadLine()) is not null && line.Trim() != "END")
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) throw new InvalidDataException("Invalid .poly coordinate.");
                    var lon = double.Parse(parts[0], CultureInfo.InvariantCulture); var lat = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    geographic.ExpandToInclude(lon, lat); coordinates.Add(Project(lon, lat));
                }
                if (coordinates.Count < 4) throw new InvalidDataException("Invalid coverage ring.");
                if (!coordinates[0].Equals2D(coordinates[^1])) coordinates.Add(coordinates[0].Copy());
                var polygon = _factory.CreatePolygon(coordinates.ToArray());
                if (!polygon.IsValid) throw new InvalidDataException("Invalid coverage polygon."); polygons.Add(polygon);
            }
        }
        if (polygons.Count == 0) throw new InvalidDataException("Empty coverage.");
        _prepared = PreparedGeometryFactory.Prepare(_factory.BuildGeometry(polygons).Union());
        Bounds = new(geographic.MinX, geographic.MinY, geographic.MaxX, geographic.MaxY);
    }
    private static Coordinate Project(double lon, double lat) => new((lon + 180) / 360,
        (1 - Math.Asinh(Math.Tan(lat * Math.PI / 180)) / Math.PI) / 2);
    public bool Contains(double latitude, double longitude) => _prepared.Covers(_factory.CreatePoint(Project(longitude, latitude)));
    public IEnumerable<TileCoordinate> Tiles(int zoom)
    {
        var range = TileMath.BoundsToTileRange(Bounds, zoom); var side = (double)(1 << zoom);
        for (var y = range.MinY; y <= range.MaxY; y++)
            for (var x = range.MinX; x <= range.MaxX; x++)
                if (_prepared.Intersects(_factory.ToGeometry(new Envelope(x / side, (x + 1) / side, y / side, (y + 1) / side))))
                    yield return new(zoom, x, y);
    }
    public object Estimate()
    {
        var counts = Enumerable.Range(8, 5).ToDictionary(z => z, z => Tiles(z).LongCount());
        return new { bounds = Bounds, counts, tileCount = counts.Values.Sum(), rawPixelBytesWithoutDeduplication = counts.Values.Sum() * 131072L, sourceFiles = SourceFiles };
    }
}
