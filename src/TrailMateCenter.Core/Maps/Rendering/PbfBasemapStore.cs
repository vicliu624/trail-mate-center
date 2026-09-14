using Microsoft.Data.Sqlite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using OsmSharp;
using OsmSharp.Streams;

namespace TrailMateCenter.Maps.Rendering;

internal sealed record BasemapSourceIssue(long? RelationId, string Reason, GeoBounds KnownMemberBounds);

/// <summary>Disk-backed geometry staging. No labels or point symbols enter this store.</summary>
internal sealed class PbfBasemapStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly GeometryFactory _factory = new();
    private readonly WKBWriter _writer = new();
    private readonly WKBReader _reader = new();
    private long _featureId;
    private readonly List<BasemapSourceIssue> _sourceIssues = new();
    public IReadOnlyList<BasemapSourceIssue> SourceIssues => _sourceIssues;
    public int SourceIssueCount { get; private set; }

    public PbfBasemapStore(string path)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        using var command = _db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=OFF;
            PRAGMA synchronous=OFF;
            PRAGMA cache_size=-32768;
            CREATE TABLE nodes(id INTEGER PRIMARY KEY, x REAL NOT NULL, y REAL NOT NULL);
            CREATE TABLE ways(id INTEGER PRIMARY KEY, geometry BLOB NOT NULL);
            CREATE TABLE features(id INTEGER PRIMARY KEY, kind TEXT NOT NULL, geometry BLOB NOT NULL, minzoom INTEGER NOT NULL, way_id INTEGER, draw_order INTEGER NOT NULL);
            CREATE INDEX feature_way ON features(way_id,kind);
            CREATE VIRTUAL TABLE feature_bounds USING rtree(id,minx,maxx,miny,maxy);
            """;
        command.ExecuteNonQuery();
    }

    public void Import(string pbfPath, Action<string, long>? progress, CancellationToken token, GeoBounds? requiredArea = null)
    {
        Envelope? requiredEnvelope = null;
        if (requiredArea is { } area)
        {
            var (left, top) = Project(area.West, area.North);
            var (right, bottom) = Project(area.East, area.South);
            requiredEnvelope = new Envelope(left, right, top, bottom);
        }
        // Separate scans also support PBFs whose elements aren't node/way/relation ordered.
        Scan(pbfPath, "nodes", geo =>
        {
            if (geo is not Node { Id: not null, Latitude: not null, Longitude: not null } n) return;
            var (x, y) = Project(n.Longitude.Value, n.Latitude.Value);
            Execute("INSERT OR REPLACE INTO nodes VALUES($id,$x,$y)", ("$id", n.Id.GetValueOrDefault()), ("$x", x), ("$y", y));
        }, progress, token);

        Scan(pbfPath, "ways", geo =>
        {
            if (geo is not Way { Id: not null, Nodes: not null } way || way.Nodes.Length < 2) return;
            using var lookup = _db.CreateCommand();
            lookup.CommandText = "SELECT x,y FROM nodes WHERE id=$id";
            lookup.Parameters.Add("$id", SqliteType.Integer);
            lookup.Prepare();
            var coordinates = new Coordinate[way.Nodes.Length];
            for (var i = 0; i < way.Nodes.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                lookup.Parameters[0].Value = way.Nodes[i];
                using var row = lookup.ExecuteReader();
                if (!row.Read())
                    throw new InvalidDataException($"PBF way {way.Id} references missing node {way.Nodes[i]}. Use a complete regional PBF.");
                coordinates[i] = new Coordinate(row.GetDouble(0), row.GetDouble(1));
            }
            var line = _factory.CreateLineString(coordinates);
            Execute("INSERT OR REPLACE INTO ways VALUES($id,$g)", ("$id", way.Id.GetValueOrDefault()), ("$g", _writer.Write(line)));
            var kind = Classify(way);
            if (kind is null) return;
            Geometry geometry = line;
            if (IsArea(kind))
            {
                if (coordinates.Length < 4 || way.Nodes[0] != way.Nodes[^1]) return;
                geometry = _factory.CreatePolygon(coordinates);
                if (!geometry.IsValid) geometry = geometry.Buffer(0);
            }
            AddFeature(kind, geometry, way.Id);
        }, progress, token);

        Scan(pbfPath, "relations", geo =>
        {
            if (geo is not Relation { Members: not null } relation || Tag(relation, "type") != "multipolygon") return;
            var kind = Classify(relation);
            if (kind is null || !IsArea(kind)) return;
            var outerLines = new List<Geometry>();
            var innerLines = new List<Geometry>();
            string? missingMember = null;
            foreach (var member in relation.Members)
            {
                token.ThrowIfCancellationRequested();
                if (member.Type != OsmGeoType.Way) continue;
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT geometry FROM ways WHERE id=$id";
                command.Parameters.AddWithValue("$id", member.Id);
                var bytes = command.ExecuteScalar() as byte[];
                if (bytes is null)
                {
                    missingMember = $"PBF relation {relation.Id} has a missing way {member.Id}.";
                    continue;
                }
                (member.Role == "inner" ? innerLines : outerLines).Add(_reader.Read(bytes));
            }
            var memberEnvelope = new Envelope();
            foreach (var line in outerLines.Concat(innerLines)) memberEnvelope.ExpandToInclude(line.EnvelopeInternal);
            try
            {
            if (missingMember is not null) throw new InvalidDataException(missingMember);
            // Check source closure before dissolving retraced edges. A closed
            // boundary can leave non-area cut edges after noding (e.g. two
            // forest lobes joined by a segment traversed in both directions).
            // Those are not evidence of missing source geometry.
            if (!HasClosedLinework(outerLines) || !HasClosedLinework(innerLines))
                throw new InvalidDataException($"PBF relation {relation.Id} has incomplete polygon rings.");
            var outer = PolygonizeRings(outerLines);
            var inner = PolygonizeRings(innerLines);
            if (outer.GetInvalidRingLines().Count > 0 || inner.GetInvalidRingLines().Count > 0)
                throw new InvalidDataException($"PBF relation {relation.Id} has invalid polygon rings after noding.");
            var shells = outer.GetPolygons().Cast<Geometry>().ToArray();
            if (shells.Length == 0)
                throw new InvalidDataException($"PBF relation {relation.Id} has no outer polygon area.");
            var geometry = _factory.BuildGeometry(shells).Union();
            var holes = inner.GetPolygons().Cast<Geometry>().ToArray();
            if (holes.Length > 0) geometry = geometry.Difference(_factory.BuildGeometry(holes).Union());
            // A tagged outer way must not fill a relation's holes underneath the relation.
            foreach (var member in relation.Members.Where(m => m.Type == OsmGeoType.Way))
            {
                Execute("DELETE FROM feature_bounds WHERE id IN (SELECT id FROM features WHERE way_id=$way AND kind=$kind)",
                    ("$way", member.Id), ("$kind", kind));
                Execute("DELETE FROM features WHERE way_id=$way AND kind=$kind", ("$way", member.Id), ("$kind", kind));
            }
            AddFeature(kind, geometry);
            }
            catch (Exception ex) when (ex is InvalidDataException or TopologyException)
            {
                // A regional PBF includes unrelated areas. Only geometries whose
                // known, complete members are disjoint from the requested area
                // may be omitted. Unknown extents and missing members still fail.
                if (requiredEnvelope is null || memberEnvelope.IsNull || missingMember is not null || memberEnvelope.Intersects(requiredEnvelope))
                    throw;
                SourceIssueCount++;
                if (_sourceIssues.Count < 100)
                {
                    static double Latitude(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI;
                    _sourceIssues.Add(new BasemapSourceIssue(relation.Id, ex.Message,
                        new GeoBounds(memberEnvelope.MinX * 360 - 180, Latitude(memberEnvelope.MaxY),
                            memberEnvelope.MaxX * 360 - 180, Latitude(memberEnvelope.MinY))));
                }
            }
        }, progress, token);
    }

    public IEnumerable<(string Kind, Geometry Geometry)> Query(Envelope bounds, int zoom)
    {
        using var command = _db.CreateCommand();
        command.CommandText = """
            SELECT f.kind,f.geometry FROM feature_bounds b JOIN features f ON f.id=b.id
            WHERE b.maxx >= $left AND b.minx <= $right AND b.maxy >= $top AND b.miny <= $bottom
                AND f.minzoom <= $zoom ORDER BY f.draw_order,f.id
            """;
        command.Parameters.AddWithValue("$left", bounds.MinX);
        command.Parameters.AddWithValue("$right", bounds.MaxX);
        command.Parameters.AddWithValue("$top", bounds.MinY);
        command.Parameters.AddWithValue("$bottom", bounds.MaxY);
        command.Parameters.AddWithValue("$zoom", zoom);
        using var rows = command.ExecuteReader();
        while (rows.Read()) yield return (rows.GetString(0), _reader.Read((byte[])rows[1]));
    }

    private static bool HasClosedLinework(IReadOnlyCollection<Geometry> lines)
    {
        // Every endpoint must have even degree for the source ways to form
        // closed walks. This accepts split ways and touching closed rings, but
        // never snaps endpoints or supplies a missing closing segment.
        var unmatched = new HashSet<(double X, double Y)>();
        foreach (var geometry in lines)
        {
            if (geometry is not LineString line || line.NumPoints < 2) return false;
            var first = line.GetCoordinateN(0);
            var last = line.GetCoordinateN(line.NumPoints - 1);
            if (!unmatched.Add((first.X, first.Y))) unmatched.Remove((first.X, first.Y));
            if (!unmatched.Add((last.X, last.Y))) unmatched.Remove((last.X, last.Y));
        }
        return unmatched.Count == 0;
    }

    private static Polygonizer PolygonizeRings(IReadOnlyCollection<Geometry> lines)
    {
        var polygonizer = new Polygonizer();
        if (lines.Count > 0)
        {
            // OSM ways can share vertices and coincident edges inside a LineString.
            // Polygonizer requires noded linework (edges meet only at endpoints).
            // UnaryUnion splits at intersections and dissolves duplicate segments,
            // without snapping coordinates or inventing closures for open rings.
            polygonizer.Add(UnaryUnionOp.Union(lines));
        }
        return polygonizer;
    }

    public static (double X, double Y) Project(double longitude, double latitude)
    {
        var radians = Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return ((longitude + 180) / 360, (1 - Math.Log(Math.Tan(radians) + 1 / Math.Cos(radians)) / Math.PI) / 2);
    }

    private void Scan(string path, string stage, Action<OsmGeo> accept, Action<string, long>? progress, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        using var source = new PBFOsmStreamSource(input);
        using var transaction = _db.BeginTransaction();
        long count = 0;
        progress?.Invoke(stage, count);
        foreach (var geo in source)
        {
            token.ThrowIfCancellationRequested();
            accept(geo);
            if (++count % 25000 == 0) progress?.Invoke(stage, count);
        }
        transaction.Commit();
        progress?.Invoke(stage, count);
    }

    private void AddFeature(string kind, Geometry geometry, long? wayId = null)
    {
        if (geometry.IsEmpty) return;
        var bounds = geometry.EnvelopeInternal;
        var id = ++_featureId;
        Execute("INSERT INTO features VALUES($id,$kind,$g,$z,$way,$order)", ("$id", id), ("$kind", kind),
            ("$g", _writer.Write(geometry)), ("$z", MinimumZoom(kind)), ("$way", (object?)wayId ?? DBNull.Value), ("$order", DrawOrder(kind)));
        Execute("INSERT INTO feature_bounds VALUES($id,$a,$b,$c,$d)", ("$id", id),
            ("$a", bounds.MinX), ("$b", bounds.MaxX), ("$c", bounds.MinY), ("$d", bounds.MaxY));
    }

    private void Execute(string sql, params (string Name, object Value)[] values)
    {
        using var command = _db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static string? Tag(OsmGeo geo, string key) => geo.Tags?.TryGetValue(key, out var value) == true ? value : null;

    private static string? Classify(OsmGeo geo)
    {
        // Intentional allow-list: amenity/tourism/shop/name and every node are excluded.
        var highway = Tag(geo, "highway");
        if (highway is "motorway" or "motorway_link" or "trunk" or "trunk_link") return "highway";
        if (highway is "primary" or "primary_link" or "secondary" or "secondary_link" or "tertiary" or "tertiary_link") return "road";
        if (highway is "residential" or "unclassified" or "living_street" or "service") return "street";
        if (highway is "path" or "footway" or "track" or "cycleway" or "bridleway" or "steps") return "path";
        if (Tag(geo, "railway") is "rail" or "light_rail" or "tram") return "rail";
        if (Tag(geo, "waterway") is "river" or "stream" or "canal" or "drain") return "river";
        if (Tag(geo, "natural") == "coastline") return "coastline";
        if (Tag(geo, "natural") == "water" || Tag(geo, "waterway") == "riverbank" || Tag(geo, "landuse") == "reservoir") return "water";
        if (Tag(geo, "building") is { } building && building != "no") return "building";
        if (Tag(geo, "landuse") == "forest" || Tag(geo, "natural") == "wood") return "forest";
        if (Tag(geo, "landuse") is "grass" or "meadow" or "recreation_ground" || Tag(geo, "natural") is "grassland" or "scrub" || Tag(geo, "leisure") == "park") return "green";
        if (Tag(geo, "landuse") is "farmland" or "orchard" or "vineyard") return "farmland";
        if (Tag(geo, "landuse") is "residential" or "commercial" or "industrial") return "builtup";
        return null;
    }

    internal static bool IsArea(string kind) => kind is "water" or "building" or "forest" or "green" or "farmland" or "builtup";
    private static int DrawOrder(string kind) => kind switch
    {
        "builtup" => 0, "farmland" => 1, "forest" => 2, "green" => 3, "water" => 4,
        "building" => 5, "river" or "coastline" => 6, "path" => 7, "rail" => 8,
        "street" => 9, "road" => 10, "highway" => 11, _ => 0,
    };
    private static int MinimumZoom(string kind) => kind switch
    {
        "building" => 15, "street" => 12, "path" => 13, "rail" => 10,
        "river" => 10, "road" => 8, "builtup" => 9, _ => 0,
    };

    public void Dispose() => _db.Dispose();
}
