using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using TrailMateCenter.Maps.Rendering;
using TrailMateCenter.Places;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Single-operation disk-backed builder. All large sorts/postings live in a private SQLite staging database.</summary>
public sealed class TmapBuilder : IDisposable
{
    private readonly string _work;
    private readonly SqliteConnection _db;
    private readonly Dictionary<int, FileStream> _pixels = new();
    private readonly List<TmapSection> _sections = new();
    private readonly CancellationToken _token;
    private bool _completed, _disposed;
    private long _estimatedBytes;
    private readonly long _maximumBytes;
    private static readonly string[] Categories = new[] { "generic", "admin", "emergency", "food", "landmark", "lodging", "medical", "natural", "outdoor", "settlement", "shop", "transport", "water" }
        .Concat(MapAnnotationPolicy.Rules.Select(x => x.Category)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public TmapBuilder(string temporaryParent, long maximumOutputBytes = long.MaxValue, CancellationToken token = default)
    {
        _token = token; _maximumBytes = maximumOutputBytes;
        _work = Path.Combine(Path.GetFullPath(temporaryParent), ".tmap-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_work, "stage.sqlite"), Pooling = false }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=FILE; PRAGMA cache_size=-16384;
            CREATE TABLE tiles(layer INTEGER,k BLOB,off INTEGER,len INTEGER,codec INTEGER,crc INTEGER,PRIMARY KEY(layer,k)) WITHOUT ROWID;
            CREATE TABLE payloads(layer INTEGER,hash BLOB,codec INTEGER,off INTEGER,len INTEGER,crc INTEGER,PRIMARY KEY(layer,hash,codec)) WITHOUT ROWID;
            CREATE TABLE pois(id BLOB PRIMARY KEY,lat INTEGER,lon INTEGER,category INTEGER,importance INTEGER,kind INTEGER,minzoom INTEGER,spatial INTEGER) WITHOUT ROWID;
            CREATE TABLE names(id BLOB,norm BLOB,display TEXT,preferred INTEGER,PRIMARY KEY(id,norm)) WITHOUT ROWID;
            CREATE TABLE poi_rows(id BLOB PRIMARY KEY,rownum INTEGER) WITHOUT ROWID;
            CREATE TABLE documents(id INTEGER PRIMARY KEY,poi INTEGER,norm BLOB);
            CREATE TABLE grams(k BLOB,id INTEGER,PRIMARY KEY(k,id)) WITHOUT ROWID;
            CREATE TABLE dictionary(kind INTEGER,k BLOB,v BLOB,PRIMARY KEY(kind,k)) WITHOUT ROWID;
            CREATE TABLE annotations(k BLOB,stable BLOB,kind INTEGER,priority INTEGER,lat INTEGER,lon INTEGER,geometry BLOB);
            CREATE INDEX annotation_order ON annotations(k);
            CREATE TABLE spatial(level INTEGER,seq INTEGER,minx INTEGER,miny INTEGER,maxx INTEGER,maxy INTEGER,ref INTEGER,n INTEGER,PRIMARY KEY(level,seq));
            BEGIN;
            """);
    }
    private string Part(string name) => Path.Combine(_work, name);
    private void Check() { _token.ThrowIfCancellationRequested(); if (_completed) throw new InvalidOperationException("Build already completed."); }
    private void Execute(string sql, params (string Key, object Value)[] args)
    {
        using var c = _db.CreateCommand(); c.CommandText = sql;
        foreach (var a in args) c.Parameters.AddWithValue(a.Key, a.Value); c.ExecuteNonQuery();
    }
    private SqliteCommand Query(string sql, params (string Key, object Value)[] args)
    { var c = _db.CreateCommand(); c.CommandText = sql; foreach (var a in args) c.Parameters.AddWithValue(a.Key, a.Value); return c; }
    private long Scalar(string sql) { using var c = Query(sql); return Convert.ToInt64(c.ExecuteScalar()); }

    public void AddTile(int layer, int zoom, int x, int y, ReadOnlySpan<byte> pixels, bool transparent = false)
    {
        Check(); var key = TmapFormat.OrderedKey(TmapFormat.TileKey(zoom, x, y));
        if (layer is not (1 or 2 or 3 or >= 100 and <= 104 or >= 110 and <= 114)) throw new ArgumentOutOfRangeException(nameof(layer));
        var expected = transparent ? 262144 : 131072;
        if (pixels.Length != expected) throw new ArgumentException("A tile must contain exactly 256×256 native pixels.");
        if (!_pixels.TryGetValue(layer, out var stream)) _pixels[layer] = stream = File.Create(Part($"pixels-{layer}"));
        var hash = SHA256.HashData(pixels); var codec = transparent ? 2 : 1;
        long? sharedOffset = null; uint crc = 0;
        using (var command = Query("SELECT off,len,crc FROM payloads WHERE layer=$l AND hash=$h AND codec=$c", ("$l", layer), ("$h", hash), ("$c", codec)))
        using (var row = command.ExecuteReader())
            if (row.Read())
            {
                if (row.GetInt32(1) != pixels.Length) throw new InvalidDataException("Pixel hash collision.");
                var saved = stream.Position; stream.Position = row.GetInt64(0);
                var existing = new byte[pixels.Length]; stream.ReadExactly(existing); stream.Position = saved;
                if (!pixels.SequenceEqual(existing)) throw new InvalidDataException("Pixel hash collision.");
                sharedOffset = row.GetInt64(0); crc = (uint)row.GetInt64(2);
            }
        if (sharedOffset is null && checked(_estimatedBytes + pixels.Length) > _maximumBytes)
            throw new IOException("TMAP exceeds the configured output budget.");
        var offset = sharedOffset ?? stream.Position;
        if (sharedOffset is null) crc = TmapFormat.Crc32C(pixels);
        Execute("INSERT INTO tiles VALUES($l,$k,$o,$n,$c,$crc)", ("$l", layer), ("$k", key), ("$o", offset),
            ("$n", pixels.Length), ("$c", codec), ("$crc", (long)crc));
        if (sharedOffset is null)
        {
            stream.Write(pixels); _estimatedBytes += pixels.Length;
            Execute("INSERT INTO payloads VALUES($l,$h,$c,$o,$n,$crc)", ("$l", layer), ("$h", hash), ("$c", codec),
                ("$o", offset), ("$n", pixels.Length), ("$crc", (long)crc));
        }
    }

    public void AddPlace(TmapPlace place)
    {
        Check();
        if (place.StableId.Length != 16 || place.Kind is < 1 or > 3 || place.MinimumZoom is < 0 or > 29 ||
            !double.IsFinite(place.Latitude) || !double.IsFinite(place.Longitude) || place.Latitude is < -90 or > 90 || place.Longitude is < -180 or >= 180)
            throw new ArgumentException("Invalid TMAP place.");
        var lat = checked((int)Math.Round(place.Latitude * 1e7)); var lon = checked((int)Math.Round(place.Longitude * 1e7));
        var category = Array.IndexOf(Categories, place.Category); if (category < 0) category = Array.IndexOf(Categories, "generic");
        var spatial = TmapFormat.Morton((uint)Math.Clamp((place.Longitude + 180) / 360 * (1L << 29), 0, (1L << 29) - 1),
            (uint)Math.Clamp((place.Latitude + 90) / 180 * (1L << 29), 0, (1L << 29) - 1));
        Execute("INSERT OR IGNORE INTO pois VALUES($id,$lat,$lon,$c,$i,$kind,$z,$s)", ("$id", place.StableId), ("$lat", lat), ("$lon", lon),
            ("$c", category + 1), ("$i", Math.Clamp(place.Importance, 0, ushort.MaxValue)), ("$kind", place.Kind), ("$z", place.MinimumZoom), ("$s", (long)spatial));
        var preferred = 1;
        foreach (var display in place.Names.Distinct(StringComparer.Ordinal))
        {
            if (TmapFormat.Utf8.GetByteCount(display) > 512) throw new InvalidDataException("A POI name exceeds 512 UTF-8 bytes; it was not truncated.");
            var normalized = TmapFormat.NormalizeName(display); if (normalized.Length == 0) continue;
            Execute("INSERT OR IGNORE INTO names VALUES($id,$n,$d,$p)", ("$id", place.StableId), ("$n", TmapFormat.Utf8.GetBytes(normalized)), ("$d", display), ("$p", preferred));
            preferred = 0;
        }
    }

    public void AddAnnotation(int zoom, int x, int y, byte[] stableId, int kind, int priority, double latitude, double longitude,
        IReadOnlyList<(double Latitude, double Longitude)>? path = null)
    {
        Check();
        if (stableId.Length != 16 || kind is < 1 or > 3 || path?.Count > 8) throw new ArgumentException("Invalid annotation.");
        var geometry = new byte[(path?.Count ?? 0) * 8];
        for (var i = 0; i < (path?.Count ?? 0); i++)
        { TmapFormat.PutI32(geometry, i * 8, checked((int)Math.Round(path![i].Latitude * 1e7))); TmapFormat.PutI32(geometry, i * 8 + 4, checked((int)Math.Round(path[i].Longitude * 1e7))); }
        Execute("INSERT INTO annotations VALUES($k,$s,$kind,$p,$lat,$lon,$g)", ("$k", TmapFormat.OrderedKey(TmapFormat.TileKey(zoom, x, y))),
            ("$s", stableId), ("$kind", kind), ("$p", Math.Clamp(priority, 0, 65535)),
            ("$lat", checked((int)Math.Round(latitude * 1e7))), ("$lon", checked((int)Math.Round(longitude * 1e7))), ("$g", geometry));
    }

    public TmapBuildResult Complete(string outputPath, MapPackAreaSelection area, TmapOptions options,
        TmapAdministrativeAreas? administrativeAreas = null)
    {
        if (string.IsNullOrWhiteSpace(options.PackageKey) || string.IsNullOrWhiteSpace(options.Series)) throw new ArgumentException("Stable package and series keys are required.");
        if (!double.IsFinite(area.Bounds.West) || !double.IsFinite(area.Bounds.East) || !double.IsFinite(area.Bounds.South) || !double.IsFinite(area.Bounds.North) ||
            area.Bounds.West < -180 || area.Bounds.East > 180 || area.Bounds.South < -90 || area.Bounds.North > 90 || area.Bounds.West >= area.Bounds.East || area.Bounds.South >= area.Bounds.North)
            throw new ArgumentException("Invalid package coverage.");
        Check(); foreach (var s in _pixels.Values) s.Dispose();
        Execute("COMMIT; CREATE INDEX poi_order ON pois(spatial,id); CREATE INDEX name_order ON documents(norm,id);");
        CompilePlaces(); CompileSpatial(); CompileSearch(); CompileAnnotations(); CompileTiles(area, options);
        var tileCount = Scalar("SELECT COUNT(*) FROM tiles"); var poiCount = Scalar("SELECT COUNT(*) FROM pois"); var nameCount = Scalar("SELECT COUNT(*) FROM documents");
        var metaPath = Part("meta"); WriteMeta(metaPath, area, options, tileCount, poiCount, nameCount);
        _sections.Insert(0, new(1, 1, 0, metaPath, Paged: false));
        var target = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteContainer(temporary, area, options);
            TmapFastLabels.Append(temporary, _token, _maximumBytes, updateIdentity: false);
            if (!string.IsNullOrWhiteSpace(options.AdministrativeBoundaryManifest))
                TmapAdministrativeSections.Upgrade(temporary, administrativeAreas ?? new TmapAdministrativeAreas(options.AdministrativeBoundaryManifest, _token),
                    _token, _maximumBytes, updateIdentity: false);
            if (options.GenerateFontPacks)
                TmapFontSections.Upgrade(temporary, options.FontOutputDirectory ?? TmapLayout.FontRootForPackage(target), _token, _maximumBytes, updateIdentity: false);
            _token.ThrowIfCancellationRequested();
            using (var reader = new TmapReader(temporary)) reader.ValidateAllPages(_token);
            _token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
            if (File.Exists(temporary + ".font-resources.json")) File.Move(temporary + ".font-resources.json", target + ".font-resources.json", true);
            _completed = true;
            return new(target, tileCount, poiCount, nameCount, new FileInfo(target).Length)
            { UniqueTilePayloads = Scalar("SELECT COUNT(*) FROM payloads"), StoredPixelBytes = _estimatedBytes };
        }
        finally
        {
            File.Delete(temporary); File.Delete(temporary + ".extensions-base");
            File.Delete(temporary + ".font-resources.json");
        }
    }

    private void CompilePlaces()
    {
        using var records = new TmapTable(Part("pois"), 9, 96);
        using var names = new TmapTable(Part("names"), 9, 48);
        using var strings = new TmapTable(Part("strings"), 10);
        using var c = Query("SELECT id,lat,lon,category,importance,kind,minzoom FROM pois ORDER BY spatial,id"); using var r = c.ExecuteReader();
        ulong row = 0, name = 0;
        Execute("BEGIN;");
        while (r.Read())
        {
            Check(); row++; var id = (byte[])r[0]; var first = name + 1;
            Execute("INSERT INTO poi_rows VALUES($id,$r)", ("$id", id), ("$r", (long)row));
            using var nc = Query("SELECT display,norm FROM names WHERE id=$id ORDER BY preferred DESC,norm", ("$id", id)); using var nr = nc.ExecuteReader();
            while (nr.Read())
            {
                name++; var display = nr.GetString(0); var norm = (byte[])nr[1]; var normalized = TmapFormat.Utf8.GetString(norm);
                var n = new byte[48]; TmapFormat.Put64(n, 0, row); TmapFormat.Put64(n, 8, strings.String(display));
                TmapFormat.Put64(n, 16, strings.String(normalized)); TmapFormat.Put16(n, 28, name == first ? 1 : 2);
                TmapFormat.Put32(n, 32, (uint)norm.Length); TmapFormat.Put32(n, 36, (uint)TmapFormat.Utf8.GetByteCount(display)); names.Add(n);
                Execute("INSERT INTO documents VALUES($n,$p,$v)", ("$n", (long)name), ("$p", (long)row), ("$v", norm));
                foreach (var gram in TmapFormat.Grams(normalized, true))
                    Execute("INSERT INTO grams VALUES($g,$n)", ("$g", TmapFormat.OrderedKey(gram)), ("$n", (long)name));
            }
            var p = new byte[96]; id.CopyTo(p, 0); TmapFormat.PutI32(p, 16, r.GetInt32(1)); TmapFormat.PutI32(p, 20, r.GetInt32(2));
            TmapFormat.Put32(p, 24, (uint)r.GetInt32(3)); TmapFormat.Put32(p, 28, (uint)r.GetInt32(5));
            if (name >= first) { TmapFormat.Put64(p, 32, first); TmapFormat.Put64(p, 40, first); }
            TmapFormat.Put32(p, 48, (uint)(name + 1 - first)); TmapFormat.Put16(p, 56, r.GetInt32(4)); p[58] = (byte)r.GetInt32(6); p[59] = 29;
            records.Add(p);
        }
        Execute("COMMIT;");
        _sections.Add(new(20, 20, 0, Part("pois"), row)); _sections.Add(new(22, 22, 0, Part("names"), name));
        _sections.Add(new(23, 23, 0, Part("strings"), strings.Count));
        var root = TmapTree.Build(Part("ids"), IdEntries(), (long)row, TmapKeyKind.Id, 16, 5, 16, _token);
        _sections.Add(new(21, 21, 0, Part("ids"), row, root));
    }
    private IEnumerable<TmapTreeEntry> IdEntries()
    {
        using var c = Query("SELECT id,rownum FROM poi_rows ORDER BY id"); using var r = c.ExecuteReader();
        while (r.Read()) { var b = new byte[16]; TmapFormat.Put64(b, 0, (ulong)r.GetInt64(1)); yield return new((byte[])r[0], b); }
    }

    private void CompileSearch()
    {
        Execute("BEGIN;");
        using var postings = File.Create(Part("postings"));
        // offset zero is reserved so PostingRef=0 is always invalid.
        TmapFormat.WritePage(postings, TmapFormat.Page(11, 0, 0, 24));
        for (var kind = 0; kind < 2; kind++)
        {
            var table = kind == 0 ? "documents" : "grams"; var key = kind == 0 ? "norm" : "k";
            using var c = Query($"SELECT {key},COUNT(*) FROM {table} GROUP BY {key} ORDER BY {key}"); using var r = c.ExecuteReader();
            while (r.Read())
            {
                Check(); var k = (byte[])r[0]; var count = r.GetInt64(1);
                using var ids = Query($"SELECT id FROM {table} WHERE {key}=$k ORDER BY id", ("$k", k));
                var reference = WritePosting(postings, ids, count);
                Execute("INSERT INTO dictionary VALUES($kind,$k,$v)", ("$kind", kind), ("$k", k), ("$v", reference));
            }
            var n = Scalar($"SELECT COUNT(*) FROM dictionary WHERE kind={kind}");
            var maxKey = kind == 0 && n > 0 ? (int)Scalar("SELECT MAX(length(k)) FROM dictionary WHERE kind=0") : 8;
            var path = Part(kind == 0 ? "name-dict" : "gram-dict");
            var root = TmapTree.Build(path, DictionaryEntries(kind), n, kind == 0 ? TmapKeyKind.Text : TmapKeyKind.Number, 32,
                kind == 0 ? (ushort)7 : (ushort)8, maxKey, _token);
            _sections.Add(new((uint)(30 + kind), (uint)(30 + kind), 0, path, (ulong)n, root));
        }
        _sections.Add(new(32, 32, 0, Part("postings")));
        Execute("COMMIT;");
    }
    private byte[] WritePosting(FileStream output, SqliteCommand ids, long count)
    {
        var blocks = checked((int)((count + 503) / 504)); var dirPages = (blocks + 167) / 168;
        var start = output.Position; output.SetLength(checked(start + (long)dirPages * 4096)); output.Position = output.Length;
        using var r = ids.ExecuteReader(); var remaining = count;
        for (var d = 0; d < dirPages; d++)
        {
            var blockN = Math.Min(168, blocks - d * 168); var dirOffset = start + (long)d * 4096;
            var directory = TmapFormat.Page(11, (ulong)dirOffset, blockN, 24);
            for (var i = 0; i < blockN; i++)
            {
                Check(); var n = (int)Math.Min(504, remaining); var dataOffset = (ulong)output.Position;
                var data = TmapFormat.Page(12, dataOffset, n, 8); ulong first = 0, last = 0;
                for (var j = 0; j < n; j++) { if (!r.Read()) throw new InvalidDataException("Posting count mismatch."); last = (ulong)r.GetInt64(0); if (j == 0) first = last; TmapFormat.Put64(data, 64 + j * 8, last); }
                remaining -= n; TmapFormat.WritePage(output, data);
                TmapFormat.Put64(directory, 64 + i * 24, first); TmapFormat.Put64(directory, 72 + i * 24, last); TmapFormat.Put64(directory, 80 + i * 24, dataOffset);
            }
            var end = output.Position; output.Position = dirOffset; TmapFormat.WritePage(output, directory); output.Position = end;
        }
        var reference = new byte[32]; TmapFormat.Put64(reference, 0, (ulong)start); TmapFormat.Put64(reference, 8, (ulong)count); TmapFormat.Put32(reference, 16, (uint)blocks); return reference;
    }
    private IEnumerable<TmapTreeEntry> DictionaryEntries(int kind)
    { using var c = Query("SELECT k,v FROM dictionary WHERE kind=$i ORDER BY k", ("$i", kind)); using var r = c.ExecuteReader(); while (r.Read()) yield return new((byte[])r[0], (byte[])r[1]); }

    private void CompileSpatial()
    {
        using var output = File.Create(Part("spatial")); output.Write(new byte[4096]);
        using (var c = Query("SELECT p.lon,p.lat,r.rownum FROM pois p JOIN poi_rows r ON p.id=r.id ORDER BY r.rownum"))
        using (var r = c.ExecuteReader())
        {
            long seq = 0, first = 0; var count = 0; var box = EmptyBox();
            while (r.Read())
            {
                Check(); if (count == 0) first = r.GetInt64(2);
                Include(box, r.GetInt32(0), r.GetInt32(1)); count++;
                if (count == 42) { SpatialRef(0, seq++, box, first, count); count = 0; box = EmptyBox(); }
            }
            if (count > 0) SpatialRef(0, seq, box, first, count);
        }
        var level = 0; ulong root = 0;
        while (true)
        {
            var count = Scalar($"SELECT COUNT(*) FROM spatial WHERE level={level}"); if (count == 0) break;
            var pages = (count + 125) / 126; var size = count / pages; var remainder = count % pages;
            using var c = Query("SELECT minx,miny,maxx,maxy,ref,n FROM spatial WHERE level=$l ORDER BY seq", ("$l", level)); using var r = c.ExecuteReader();
            for (long i = 0; i < pages; i++)
            {
                Check(); var n = (int)(size + (i < remainder ? 1 : 0)); root = (ulong)output.Position;
                var page = TmapFormat.Page(level == 0 ? (ushort)14 : (ushort)13, root, n, 32, level); var box = EmptyBox();
                for (var j = 0; j < n; j++)
                {
                    if (!r.Read()) throw new InvalidDataException("Spatial index count mismatch.");
                    for (var k = 0; k < 4; k++) TmapFormat.PutI32(page, 64 + j * 32 + k * 4, r.GetInt32(k));
                    Include(box, r.GetInt32(0), r.GetInt32(1)); Include(box, r.GetInt32(2), r.GetInt32(3));
                    TmapFormat.Put64(page, 80 + j * 32, (ulong)r.GetInt64(4));
                    if (level == 0) TmapFormat.Put32(page, 88 + j * 32, (uint)r.GetInt32(5));
                    TmapFormat.Put32(page, 64 + j * 32 + (level == 0 ? 28 : 24), uint.MaxValue);
                }
                TmapFormat.WritePage(output, page); SpatialRef(level + 1, i, box, (long)root, 0);
            }
            if (pages == 1) break; level++;
        }
        var descriptor = TmapFormat.Page(1, 0, 0); TmapFormat.Put64(descriptor, 72, (ulong)Scalar("SELECT COUNT(*) FROM pois"));
        TmapFormat.Put64(descriptor, 80, root); TmapFormat.Put32(descriptor, 88, root == 0 ? 0 : (uint)(level + 1)); output.Position = 0; TmapFormat.WritePage(output, descriptor);
        _sections.Add(new(24, 24, 0, Part("spatial"), (ulong)Scalar("SELECT COUNT(*) FROM pois"), root));
    }
    private static int[] EmptyBox() => [int.MaxValue, int.MaxValue, int.MinValue, int.MinValue];
    private static void Include(int[] b, int x, int y) { b[0] = Math.Min(b[0], x); b[1] = Math.Min(b[1], y); b[2] = Math.Max(b[2], x); b[3] = Math.Max(b[3], y); }
    private void SpatialRef(int level, long seq, int[] b, long reference, int n) => Execute("INSERT INTO spatial VALUES($l,$s,$a,$b,$c,$d,$r,$n)",
        ("$l", level), ("$s", seq), ("$a", b[0]), ("$b", b[1]), ("$c", b[2]), ("$d", b[3]), ("$r", reference), ("$n", n));

    private void CompileAnnotations()
    {
        using var data = File.Create(Part("annotation-data")); using var geometry = new TmapTable(Part("geometry"), 16);
        TmapFormat.WritePage(data, TmapFormat.Page(15, 0, 0, 40)); // null reference sentinel
        using var c = Query("SELECT DISTINCT k FROM annotations ORDER BY k"); using var r = c.ExecuteReader(); long tiles = 0, total = 0;
        while (r.Read())
        {
            Check(); var key = (byte[])r[0]; var records = new List<byte[]>(200);
            using var ac = Query("""
                SELECT p.rownum,a.kind,a.priority,a.lat,a.lon,a.geometry
                FROM annotations a JOIN poi_rows p ON a.stable=p.id
                WHERE a.k=$k ORDER BY a.priority DESC,a.stable LIMIT 200
                """, ("$k", key)); using var ar = ac.ExecuteReader();
            while (ar.Read())
            {
                var record = new byte[40]; TmapFormat.Put64(record, 0, (ulong)ar.GetInt64(0)); TmapFormat.Put16(record, 8, ar.GetInt32(1));
                record[10] = (byte)(TmapFormat.FromOrderedKey(key) >> 58); record[11] = record[10]; TmapFormat.Put16(record, 12, ar.GetInt32(2));
                TmapFormat.PutI32(record, 16, ar.GetInt32(3)); TmapFormat.PutI32(record, 20, ar.GetInt32(4));
                var points = (byte[])ar[5];
                if (points.Length > 0)
                {
                    var g = new byte[16 + points.Length]; TmapFormat.Put16(g, 0, 1); TmapFormat.Put32(g, 4, (uint)points.Length); TmapFormat.Put32(g, 8, (uint)(points.Length / 8)); points.CopyTo(g, 16);
                    TmapFormat.Put64(record, 24, geometry.Add(g));
                }
                records.Add(record);
            }
            if (records.Count == 0) continue;
            var start = (ulong)data.Position;
            for (var at = 0; at < records.Count; at += 100)
            {
                var n = Math.Min(100, records.Count - at); var pos = (ulong)data.Position;
                var page = TmapFormat.Page(15, pos, n, 40, next: at + n < records.Count ? pos + 4096 : 0);
                for (var j = 0; j < n; j++) records[at + j].CopyTo(page, 64 + j * 40); TmapFormat.WritePage(data, page);
            }
            var value = new byte[24]; TmapFormat.Put64(value, 0, start); TmapFormat.Put32(value, 8, (uint)(records.Count * 40));
            Execute("INSERT INTO dictionary VALUES(2,$k,$v)", ("$k", key), ("$v", value)); tiles++; total += records.Count;
        }
        var root = TmapTree.Build(Part("annotation-index"), DictionaryEntries(2), tiles, TmapKeyKind.Number, 24, 3, 8, _token);
        _sections.Add(new(40, 40, 0, Part("annotation-index"), (ulong)tiles, root)); _sections.Add(new(41, 41, 0, Part("annotation-data"), (ulong)total));
        _sections.Add(new(42, 42, 0, Part("geometry"), geometry.Count));
    }

    private void CompileTiles(MapPackAreaSelection area, TmapOptions options)
    {
        using var layers = File.Create(Part("layers"));
        foreach (var layer in _pixels.Keys.Order())
        {
            var count = Scalar($"SELECT COUNT(*) FROM tiles WHERE layer={layer}");
            var root = TmapTree.Build(Part($"index-{layer}"), TileEntries(layer), count, TmapKeyKind.Number, 24, 3, 8, _token);
            var indexId = checked((uint)(1000 + layer * 2)); var dataId = indexId + 1;
            _sections.Add(new(10, indexId, (uint)layer, Part($"index-{layer}"), (ulong)count, root));
            _sections.Add(new(11, dataId, (uint)layer, Part($"pixels-{layer}"), (ulong)count, Paged: false));
            var b = new byte[96]; TmapFormat.Put32(b, 0, (uint)layer); TmapFormat.Put16(b, 4, layer); TmapFormat.Put16(b, 6, 256);
            TmapFormat.Put32(b, 8, LayerZoomMask(layer)); TmapFormat.Put16(b, 12, layer >= 100 ? 2 : 1);
            TmapFormat.Put32(b, 16, indexId); TmapFormat.Put32(b, 20, dataId); TmapFormat.Put32(b, 24, 40);
            SHA256.HashData(TmapFormat.Utf8.GetBytes(options.Series)).AsSpan(0, 16).CopyTo(b.AsSpan(32));
            WriteBounds(b.AsSpan(48), area.Bounds); layers.Write(b);
        }
        _sections.Add(new(2, 2, 0, Part("layers"), (ulong)_pixels.Count, Paged: false));
    }
    private uint LayerZoomMask(int layer)
    {
        uint mask = 0; using var c = Query("SELECT k FROM tiles WHERE layer=$l", ("$l", layer)); using var r = c.ExecuteReader();
        while (r.Read()) mask |= 1U << (int)(TmapFormat.FromOrderedKey((byte[])r[0]) >> 58); return mask;
    }
    private uint AnnotationZoomMask()
    {
        uint mask = 0;
        using var command = Query("SELECT DISTINCT substr(k,1,1) FROM annotations"); using var rows = command.ExecuteReader();
        while (rows.Read()) mask |= 1U << (((byte[])rows[0])[0] >> 2);
        return mask;
    }
    private IEnumerable<TmapTreeEntry> TileEntries(int layer)
    {
        using var c = Query("SELECT k,off,len,codec,crc FROM tiles WHERE layer=$l ORDER BY k", ("$l", layer)); using var r = c.ExecuteReader();
        while (r.Read())
        {
            var b = new byte[24]; TmapFormat.Put64(b, 0, (ulong)r.GetInt64(1)); TmapFormat.Put32(b, 8, (uint)r.GetInt32(2)); TmapFormat.Put32(b, 12, (uint)r.GetInt32(2));
            TmapFormat.Put16(b, 16, r.GetInt32(3)); TmapFormat.Put32(b, 20, (uint)r.GetInt64(4)); yield return new((byte[])r[0], b);
        }
    }

    private void WriteMeta(string path, MapPackAreaSelection area, TmapOptions options, long tiles, long pois, long names)
    {
        using var s = File.Create(path);
        void Tag(int tag, byte[] value) { var h = new byte[8]; TmapFormat.Put16(h, 0, tag); TmapFormat.Put32(h, 4, (uint)value.Length); s.Write(h); s.Write(value); TmapFormat.Align(s, 8); }
        void Text(int tag, string value) => Tag(tag, TmapFormat.Utf8.GetBytes(value));
        Text(1, area.Name); Text(2, options.Attribution);
        Text(3, options.CountryCode); Text(4, options.AdminCode);
        var admin = new byte[2]; TmapFormat.Put16(admin, 0, area.AdminLevel ?? 0); Tag(5, admin);
        var specificity = new byte[2]; TmapFormat.Put16(specificity, 0, options.Tier switch { TmapRegionTier.World => 0, TmapRegionTier.LargeCountry => 1, TmapRegionTier.AdministrativeRegion => 2, _ => 3 }); Tag(6, specificity);
        var categories = new MemoryStream();
        for (var i = 0; i < Categories.Length; i++) { var v = TmapFormat.Utf8.GetBytes(Categories[i]); var h = new byte[8]; TmapFormat.Put32(h, 0, (uint)(i + 1)); TmapFormat.Put16(h, 4, v.Length); categories.Write(h); categories.Write(v); TmapFormat.Align(categories, 8); }
        Tag(11, categories.ToArray()); Text(12, "portable-scalar-v1; normalization_profile=2");
        var sourceText = TmapFormat.Utf8.GetBytes(options.SourceNamespaceUri);
        var sourceEntry = new byte[(8 + sourceText.Length + 7) / 8 * 8];
        TmapFormat.Put32(sourceEntry, 0, options.SourceNamespaceId); TmapFormat.Put16(sourceEntry, 4, sourceText.Length); sourceText.CopyTo(sourceEntry, 8); Tag(17, sourceEntry);
        Text(14, JsonSerializer.Serialize(new { tiles, pois, names, routing = false, profile = "raw-rgb565-tmap-v1", source_coverage = options.SourceCoverage, normalization = "profile-2: ASCII/fullwidth/explicit-whitespace; other Unicode preserved" }));
        var coverage = new byte[96];
        uint rasterMask = 0; foreach (var layer in _pixels.Keys) rasterMask |= LayerZoomMask(layer);
        for (var i = 0; i < 3; i++)
        {
            TmapFormat.Put32(coverage, i * 32, 1U << i); TmapFormat.Put32(coverage, i * 32 + 4, 1);
            WriteBounds(coverage.AsSpan(i * 32 + 8), area.Bounds);
            TmapFormat.Put32(coverage, i * 32 + 24, i == 1 ? 0x3fffffffU : i == 2 ? AnnotationZoomMask() : rasterMask);
        }
        Tag(10, coverage);
        if (s.Length > 65536) throw new InvalidDataException("TMAP metadata exceeds profile limit.");
    }
    private static void WriteBounds(Span<byte> b, GeoBounds bounds)
    { TmapFormat.PutI32(b, 0, checked((int)Math.Round(bounds.West * 1e7))); TmapFormat.PutI32(b, 4, checked((int)Math.Round(bounds.South * 1e7))); TmapFormat.PutI32(b, 8, checked((int)Math.Round(bounds.East * 1e7))); TmapFormat.PutI32(b, 12, checked((int)Math.Round(bounds.North * 1e7))); }
    private void WriteContainer(string path, MapPackAreaSelection area, TmapOptions options)
    {
        if (_sections.Count > 256) throw new InvalidDataException("Too many sections.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        output.Write(new byte[4096]); var directory = new byte[_sections.Count * 64]; output.Write(directory); TmapFormat.Align(output);
        ulong metaOffset = 0, metaLength = 0; uint mask = 0;
        for (var i = 0; i < _sections.Count; i++)
        {
            Check(); var section = _sections[i]; TmapFormat.Align(output); var offset = (ulong)output.Position;
            using var source = File.OpenRead(section.Path); var length = (ulong)source.Length;
            if ((ulong)output.Position + length > (ulong)_maximumBytes) throw new IOException("TMAP exceeds configured output budget.");
            var buffer = new byte[65536]; int n; while ((n = source.Read(buffer)) != 0) { Check(); output.Write(buffer, 0, n); }
            var d = directory.AsSpan(i * 64, 64); TmapFormat.Put32(d, 0, section.Type); TmapFormat.Put16(d, 4, 1); TmapFormat.Put16(d, 6, section.Paged ? 3 : 1);
            TmapFormat.Put32(d, 8, section.Id); TmapFormat.Put32(d, 12, section.Owner); TmapFormat.Put64(d, 16, offset); TmapFormat.Put64(d, 24, length);
            TmapFormat.Put64(d, 32, section.Count); TmapFormat.Put64(d, 40, section.Root);
            if (section.Type == 1) { metaOffset = offset; metaLength = length; }
        }
        foreach (var layer in _pixels.Keys) mask |= LayerZoomMask(layer);
        mask |= AnnotationZoomMask();
        var header = new byte[256]; TmapFormat.Magic.CopyTo(header); TmapFormat.Put16(header, 8, 1); TmapFormat.Put32(header, 12, 256);
        TmapFormat.Put32(header, 16, 0x01020304); TmapFormat.Put32(header, 20, 4096); TmapFormat.Put64(header, 24, 6UL | (_pixels.Count > 0 ? 1UL : 0));
        TmapFormat.Put64(header, 32, (ulong)output.Length); TmapFormat.Put64(header, 40, 4096); TmapFormat.Put32(header, 48, (uint)_sections.Count); TmapFormat.Put32(header, 52, 64);
        SHA256.HashData(TmapFormat.Utf8.GetBytes(options.PackageKey)).AsSpan(0, 16).CopyTo(header.AsSpan(56)); Guid.NewGuid().TryWriteBytes(header.AsSpan(72, 16));
        SHA256.HashData(TmapFormat.Utf8.GetBytes(options.Series)).AsSpan(0, 16).CopyTo(header.AsSpan(88)); TmapFormat.Put64(header, 104, options.Revision);
        WriteBounds(header.AsSpan(120), area.Bounds); TmapFormat.Put32(header, 136, mask); TmapFormat.Put32(header, 140, 6U | (_pixels.Count > 0 ? 1U : 0));
        TmapFormat.Put64(header, 144, metaOffset); TmapFormat.Put64(header, 152, metaLength); TmapFormat.Put32(header, 164, TmapFormat.Crc32C(directory));
        TmapFormat.Put32(header, 168, 1); TmapFormat.Put32(header, 172, TmapFormat.NormalizationProfile); TmapFormat.Put32(header, 160, TmapFormat.Crc32C(header));
        output.Position = 0; output.Write(header); output.Position = 4096; output.Write(directory); output.Flush(flushToDisk: true);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var stream in _pixels.Values) stream.Dispose(); _db.Dispose();
        // Only this instance's GUID directory is owned and removed.
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
