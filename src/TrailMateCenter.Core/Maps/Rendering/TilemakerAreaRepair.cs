using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;

namespace TrailMateCenter.Maps.Rendering;

internal sealed record AddedClosingEdge(double FromLongitude, double FromLatitude, double ToLongitude, double ToLatitude, double LengthMeters);
internal sealed record BasemapGeometryRepair(long? RelationId, string Reason, IReadOnlyList<AddedClosingEdge> AddedEdges,
    double OmittedLineworkMeters, double ResultAreaSquareMeters);

/// <summary>Fallback for display maps: join endpoints, close remaining chains and repair each polygon.</summary>
internal static class TilemakerAreaRepair
{
    public static (Geometry Geometry, BasemapGeometryRepair Report) Assemble(long? relationId, string reason,
        IReadOnlyCollection<Geometry> outerLines, IReadOnlyCollection<Geometry> innerLines, CancellationToken token)
    {
        var factory = new GeometryFactory();
        var added = new List<AddedClosingEdge>();
        var outers = Close(Join(outerLines, token), added, token);
        var inners = Close(Join(innerLines, token), added, token);
        if (outers.Count == 0) throw new InvalidDataException("No usable outer boundary remains after repair.");
        var polygons = new List<Geometry>();
        foreach (var outer in outers)
        {
            token.ThrowIfCancellationRequested();
            var shell = factory.CreatePolygon(outer);
            var shellSurface = GeometryFixer.Fix(shell);
            var holes = inners.Where(inner => outers.Count == 1 || shellSurface.Covers(factory.CreatePolygon(inner))).ToArray();
            var repaired = GeometryFixer.Fix(factory.CreatePolygon(outer, holes));
            if (!repaired.IsEmpty && repaired.Area > 0) polygons.Add(repaired);
        }
        var geometry = factory.BuildGeometry(polygons).Union();
        if (geometry.IsEmpty || geometry.Area <= 0 || !geometry.IsValid)
            throw new InvalidDataException("No valid area remains after polygon repair.");
        var source = UnaryUnionOp.Union(outerLines.Concat(innerLines).ToArray());
        var latitude = Latitude(geometry.EnvelopeInternal.Centre.Y);
        var scale = 40075016.68557849 * Math.Cos(latitude * Math.PI / 180);
        var omitted = source.Difference(geometry.Boundary.Buffer(1e-12)).Length * scale;
        return (geometry, new(relationId, reason, added, omitted, geometry.Area * scale * scale));
    }

    private static List<List<Coordinate>> Join(IReadOnlyCollection<Geometry> lines, CancellationToken token)
    {
        var chains = new List<List<Coordinate>>();
        var remaining = new List<Coordinate[]>();
        foreach (var line in lines)
        {
            var coordinates = line.Coordinates;
            if (coordinates.Length < 2) continue;
            if (chains.Count == 0 || coordinates[0].Equals2D(coordinates[^1])) chains.Add(coordinates.ToList());
            else remaining.Add(coordinates);
        }
        while (remaining.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var joined = false;
            foreach (var chain in chains)
            {
                while (!chain[0].Equals2D(chain[^1]))
                {
                    token.ThrowIfCancellationRequested();
                    // Same endpoint-match preference as Tilemaker; choose the last
                    // matching member, preserving the original relation member order.
                    var match = -1;
                    var operation = 0;
                    for (; operation < 4 && match < 0; operation++)
                        match = remaining.FindLastIndex(line => operation switch
                        {
                            0 => line[0].Equals2D(chain[^1]),
                            1 => line[^1].Equals2D(chain[^1]),
                            2 => line[^1].Equals2D(chain[0]),
                            _ => line[0].Equals2D(chain[0]),
                        });
                    if (match < 0) break;
                    var line = remaining[match];
                    remaining.RemoveAt(match);
                    if (operation is 2 or 4) Array.Reverse(line);
                    if (operation <= 2) chain.AddRange(line);
                    else chain.InsertRange(0, line);
                    joined = true;
                }
            }
            if (!joined && remaining.Count > 0)
            {
                chains.Add(remaining[0].ToList());
                remaining.RemoveAt(0);
            }
        }
        return chains;
    }

    private static List<LinearRing> Close(List<List<Coordinate>> chains, List<AddedClosingEdge> added, CancellationToken token)
    {
        var factory = new GeometryFactory();
        var result = new List<LinearRing>();
        foreach (var chain in chains)
        {
            token.ThrowIfCancellationRequested();
            var points = new List<Coordinate>();
            foreach (var point in chain)
                if (points.Count == 0 || !point.Equals2D(points[^1])) points.Add(point);
            if (points.Count < 3) continue;
            if (!points[0].Equals2D(points[^1]))
            {
                var from = points[^1];
                var to = points[0];
                var fromLon = from.X * 360 - 180; var fromLat = Latitude(from.Y);
                var toLon = to.X * 360 - 180; var toLat = Latitude(to.Y);
                var a = Math.Pow(Math.Sin((toLat - fromLat) * Math.PI / 360), 2) +
                    Math.Cos(fromLat * Math.PI / 180) * Math.Cos(toLat * Math.PI / 180) *
                    Math.Pow(Math.Sin((toLon - fromLon) * Math.PI / 360), 2);
                added.Add(new(fromLon, fromLat, toLon, toLat, 12742017.6 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)))));
                points.Add(to);
            }
            if (points.Count >= 4) result.Add(factory.CreateLinearRing(points.ToArray()));
        }
        return result;
    }

    private static double Latitude(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI;
}
