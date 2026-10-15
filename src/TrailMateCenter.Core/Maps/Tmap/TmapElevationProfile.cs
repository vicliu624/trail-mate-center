namespace TrailMateCenter.Maps.Tmap;

public readonly record struct TmapElevationProfileSample(double DistanceMeters, double? ElevationMeters);
public sealed record TmapElevationProfile(IReadOnlyList<TmapElevationProfileSample> Samples,
    double DistanceMeters, double AscentMeters, double DescentMeters, double? MinimumMeters,
    double? MaximumMeters, double ValidDistanceMeters, uint ResolutionMeters, uint VerticalDatum,
    double SpacingMeters, double NoiseThresholdMeters);

public static partial class TmapOutdoorPreviewReader
{
    /// <summary>Samples the supplied path, not an implicitly planned route. Missing heights stay gaps.</summary>
    public static TmapElevationProfile ReadElevationProfile(string path, IReadOnlyList<TmapPreviewPoint> points,
        double spacingMeters = 30, double noiseThresholdMeters = 3, int maximumSamples = 4096,
        CancellationToken token = default)
    {
        if (points.Count is < 2 or > 4096 || !double.IsFinite(spacingMeters) || spacingMeters <= 0 ||
            !double.IsFinite(noiseThresholdMeters) || noiseThresholdMeters < 0 || maximumSamples is < 2 or > 100000)
            throw new ArgumentException("Invalid elevation profile options.");
        foreach (var point in points)
            if (!double.IsFinite(point.Longitude) || !double.IsFinite(point.Latitude) ||
                point.Longitude is < -180 or > 180 || point.Latitude is < -90 or > 90)
                throw new ArgumentException("Profile point is outside WGS84.");
        using var source = new PagedSource(path, 64 * 1024 * 1024, token);
        if (!source.Has(1000)) throw new InvalidDataException("This map has no numeric elevation data.");
        var terrain = source.Read(1000, 0, 64);
        long west = TmapFormat.I32(terrain, 0), south = TmapFormat.I32(terrain, 4);
        long stepX = TmapFormat.I32(terrain, 8), stepY = TmapFormat.I32(terrain, 12);
        var width = TmapFormat.U32(terrain, 16); var height = TmapFormat.U32(terrain, 20);
        var side = TmapFormat.U16(terrain, 24); var resolution = TmapFormat.U32(terrain, 28);
        var datum = TmapFormat.U32(terrain, 32);
        if (west is < -1800000000 or > 1800000000 || south is < -900000000 or > 900000000 ||
            stepX <= 0 || stepY <= 0 || width < 2 || height < 2 || side is < 2 or > 64 ||
            TmapFormat.U16(terrain, 26) != 1 || resolution == 0 || TmapFormat.U32(terrain, 36) != 0 ||
            TmapFormat.U64(terrain, 40) != 0 || west + (width - 1L) * stepX > 1800000000 ||
            south + (height - 1L) * stepY > 900000000)
            throw new InvalidDataException("Invalid elevation grid.");
        ulong nx = ((ulong)width + side - 1) / side, ny = ((ulong)height + side - 1) / side;
        if (source.Count(1001) != nx * ny || source.Length(1001) != nx * ny * 32 || !source.Has(1002))
            throw new InvalidDataException("Invalid elevation block index.");
        var samples = new List<TmapElevationProfileSample>();
        double distance = 0, ascent = 0, descent = 0, validDistance = 0;
        double? minimum = null, maximum = null, accepted = null;
        for (var segment = 0; segment + 1 < points.Count; segment++)
        {
            token.ThrowIfCancellationRequested();
            var a = points[segment]; var b = points[segment + 1];
            if (Math.Abs(a.Longitude - b.Longitude) > 180)
                throw new NotSupportedException("Profile crossing the date line is not supported.");
            var length = Distance(a, b);
            var divisions = Math.Max(1d, Math.Ceiling(length / spacingMeters));
            if (divisions > maximumSamples || samples.Count + divisions + (segment == 0 ? 1 : 0) > maximumSamples)
                throw new InvalidDataException("Elevation profile sample budget exceeded; increase sampling distance.");
            for (var i = segment == 0 ? 0 : 1; i <= (int)divisions; i++)
            {
                token.ThrowIfCancellationRequested();
                var fraction = i / divisions;
                var elevation = Elevation(a.Longitude + (b.Longitude - a.Longitude) * fraction,
                    a.Latitude + (b.Latitude - a.Latitude) * fraction);
                var at = distance + length * fraction;
                if (elevation is { } z)
                {
                    minimum = minimum is { } low ? Math.Min(low, z) : z;
                    maximum = maximum is { } high ? Math.Max(high, z) : z;
                    if (samples.Count > 0 && samples[^1].ElevationMeters is not null)
                        validDistance += at - samples[^1].DistanceMeters;
                    if (accepted is { } previous && Math.Abs(z - previous) >= noiseThresholdMeters)
                    {
                        ascent += Math.Max(0, z - previous); descent += Math.Max(0, previous - z); accepted = z;
                    }
                    else accepted ??= z;
                }
                else accepted = null;
                samples.Add(new(at, elevation));
            }
            distance += length;
        }
        return new(samples, distance, ascent, descent, minimum, maximum, validDistance, resolution, datum,
            spacingMeters, noiseThresholdMeters);

        double? Elevation(double longitude, double latitude)
        {
            long dx = (long)Math.Round(longitude * 1e7, MidpointRounding.AwayFromZero) - west;
            long dy = (long)Math.Round(latitude * 1e7, MidpointRounding.AwayFromZero) - south;
            if (dx < 0 || dy < 0 || dx > (width - 1L) * stepX || dy > (height - 1L) * stepY) return null;
            uint x = (uint)Math.Min(dx / stepX, width - 2L), y = (uint)Math.Min(dy / stepY, height - 2L);
            var fx = (dx - x * stepX) / (double)stepX; var fy = (dy - y * stepY) / (double)stepY;
            double value = 0;
            for (uint corner = 0; corner < 4; corner++)
            {
                var weight = (corner % 2 == 0 ? 1 - fx : fx) * (corner / 2 == 0 ? 1 - fy : fy);
                if (weight == 0) continue;
                var sx = x + corner % 2; var sy = y + corner / 2;
                var record = source.Read(1001, (sy / side * nx + sx / side) * 32, 32);
                var col = TmapFormat.U32(record, 0); var row = TmapFormat.U32(record, 4);
                var w = TmapFormat.U16(record, 8); var h = TmapFormat.U16(record, 10);
                var offset = TmapFormat.U64(record, 16); var bytes = TmapFormat.U32(record, 24);
                if (col != sx / side * side || row != sy / side * side || w != Math.Min(side, width - col) ||
                    h != Math.Min(side, height - row) || TmapFormat.U32(record, 12) != 0 ||
                    TmapFormat.U32(record, 28) != 0 || bytes != (uint)w * h * 2 ||
                    offset > source.Length(1002) || bytes > source.Length(1002) - offset)
                    throw new InvalidDataException("Invalid elevation block.");
                var raw = source.Read(1002, offset + ((ulong)(sy - row) * w + sx - col) * 2, 2);
                var z = unchecked((short)TmapFormat.U16(raw, 0));
                if (z == short.MinValue) return null;
                value += z * weight;
            }
            return value;
        }
    }
    private static double Distance(TmapPreviewPoint a, TmapPreviewPoint b)
    {
        const double radians = Math.PI / 180;
        var dlat = (b.Latitude - a.Latitude) * radians; var dlon = (b.Longitude - a.Longitude) * radians;
        var h = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(a.Latitude * radians) *
            Math.Cos(b.Latitude * radians) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
}
