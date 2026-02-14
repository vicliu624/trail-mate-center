namespace TrailMateCenter.Services;

internal enum ContourLineKind
{
    Major,
    Minor,
}

internal readonly record struct ContourKey(ContourLineKind Kind, int Interval);

internal static class ContourTileMath
{
    public static int LonToTileX(double lon, int zoom)
    {
        var n = 1 << zoom;
        var x = (lon + 180.0) / 360.0 * n;
        return ClampTile((int)Math.Floor(x), n);
    }

    public static int LatToTileY(double lat, int zoom)
    {
        var latRad = lat * (Math.PI / 180.0);
        var n = 1 << zoom;
        var y = (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
        return ClampTile((int)Math.Floor(y), n);
    }

    public static (double West, double South, double East, double North) TileToBounds(int x, int y, int zoom)
    {
        var n = Math.Pow(2.0, zoom);
        var west = x / n * 360.0 - 180.0;
        var east = (x + 1) / n * 360.0 - 180.0;

        var north = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n))) * 180.0 / Math.PI;
        var south = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * (y + 1) / n))) * 180.0 / Math.PI;

        return (west, south, east, north);
    }

    private static int ClampTile(int value, int n)
    {
        if (value < 0)
            return 0;
        var max = n - 1;
        return value > max ? max : value;
    }
}
