using System.Globalization;

namespace TrailMateCenter.Maps;

public readonly record struct GeoBounds(double West, double South, double East, double North)
{
    public GeoBounds Normalize()
    {
        const double MaxLat = 85.05112878;
        const double MaxLon = 179.999999;

        var west = Math.Clamp(West, -MaxLon, MaxLon);
        var east = Math.Clamp(East, -MaxLon, MaxLon);
        var south = Math.Clamp(South, -MaxLat, MaxLat);
        var north = Math.Clamp(North, -MaxLat, MaxLat);

        if (west > east)
            (west, east) = (east, west);
        if (south > north)
            (south, north) = (north, south);

        return new GeoBounds(west, south, east, north);
    }

    public bool Contains(double latitude, double longitude)
    {
        var normalized = Normalize();
        return longitude >= normalized.West &&
               longitude <= normalized.East &&
               latitude >= normalized.South &&
               latitude <= normalized.North;
    }

    public string ToInvariantText()
    {
        var normalized = Normalize();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"W {normalized.West:F5}, S {normalized.South:F5}, E {normalized.East:F5}, N {normalized.North:F5}");
    }
}

public readonly record struct TileCoordinate(int Z, int X, int Y);

public readonly record struct TileRange(int MinX, int MaxX, int MinY, int MaxY)
{
    public bool IsEmpty => MaxX < MinX || MaxY < MinY;
    public long TileCount => IsEmpty ? 0 : (long)(MaxX - MinX + 1) * (MaxY - MinY + 1);
}

public static class TileMath
{
    public const int TileSize = 256;
    public const int MinimumZoom = 0;
    public const int MaximumZoom = 24;

    public static TileCoordinate LonLatToTile(double longitude, double latitude, int zoom)
    {
        var z = NormalizeZoom(zoom);
        return new TileCoordinate(
            z,
            LonToTileX(longitude, z),
            LatToTileY(latitude, z));
    }

    public static int LonToTileX(double longitude, int zoom)
    {
        var z = NormalizeZoom(zoom);
        var n = 1 << z;
        var lon = Math.Clamp(longitude, -180.0, 179.999999);
        var x = (lon + 180.0) / 360.0 * n;
        return ClampTile((int)Math.Floor(x), n);
    }

    public static int LatToTileY(double latitude, int zoom)
    {
        var z = NormalizeZoom(zoom);
        var n = 1 << z;
        var lat = Math.Clamp(latitude, -85.05112878, 85.05112878);
        var latRad = lat * (Math.PI / 180.0);
        var y = (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
        return ClampTile((int)Math.Floor(y), n);
    }

    public static GeoBounds TileToBounds(int x, int y, int zoom)
    {
        var z = NormalizeZoom(zoom);
        var n = Math.Pow(2.0, z);
        var max = (1 << z) - 1;
        var clampedX = Math.Clamp(x, 0, max);
        var clampedY = Math.Clamp(y, 0, max);

        var west = clampedX / n * 360.0 - 180.0;
        var east = (clampedX + 1) / n * 360.0 - 180.0;
        var north = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * clampedY / n))) * 180.0 / Math.PI;
        var south = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * (clampedY + 1) / n))) * 180.0 / Math.PI;

        return new GeoBounds(west, south, east, north).Normalize();
    }

    public static TileRange BoundsToTileRange(GeoBounds bounds, int zoom)
    {
        var normalized = bounds.Normalize();
        var z = NormalizeZoom(zoom);

        var xMin = LonToTileX(normalized.West, z);
        var xMax = LonToTileX(normalized.East, z);
        var yMin = LatToTileY(normalized.North, z);
        var yMax = LatToTileY(normalized.South, z);

        if (xMin > xMax)
            (xMin, xMax) = (xMax, xMin);
        if (yMin > yMax)
            (yMin, yMax) = (yMax, yMin);

        return new TileRange(xMin, xMax, yMin, yMax);
    }

    public static double SquaredDistanceToTileCenter(double latitude, double longitude, TileCoordinate tile)
    {
        var bounds = TileToBounds(tile.X, tile.Y, tile.Z);
        var centerLat = (bounds.South + bounds.North) * 0.5;
        var centerLon = (bounds.West + bounds.East) * 0.5;
        var latDelta = latitude - centerLat;
        var lonDelta = longitude - centerLon;
        return latDelta * latDelta + lonDelta * lonDelta;
    }

    private static int NormalizeZoom(int zoom)
    {
        return Math.Clamp(zoom, MinimumZoom, MaximumZoom);
    }

    private static int ClampTile(int value, int n)
    {
        if (value < 0)
            return 0;
        var max = n - 1;
        return value > max ? max : value;
    }
}
