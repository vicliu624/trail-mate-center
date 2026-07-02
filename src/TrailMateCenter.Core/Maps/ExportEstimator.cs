using System.Globalization;

namespace TrailMateCenter.Maps;

public sealed class ExportEstimator
{
    private const long OsmTileBytes = 18_000;
    private const long TerrainTileBytes = 35_000;
    private const long SatelliteTileBytes = 55_000;
    private const long ContourTileBytes = 12_000;

    public MapPackEstimate Estimate(MapPackExportPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var area = plan.Area.Bounds.Normalize();
        var layers = new List<MapLayerEstimate>();
        var minZoom = Math.Clamp(plan.BaseLayers.MinimumZoom, TileMath.MinimumZoom, 18);
        var maxZoom = Math.Clamp(plan.BaseLayers.MaximumZoom, TileMath.MinimumZoom, 18);
        if (maxZoom < minZoom)
            (minZoom, maxZoom) = (maxZoom, minZoom);

        if (plan.BaseLayers.IncludeOsm)
            layers.Add(EstimateLayer("OSM raster", area, minZoom, maxZoom, OsmTileBytes));
        if (plan.BaseLayers.IncludeTerrain)
            layers.Add(EstimateLayer("Terrain raster", area, minZoom, Math.Min(maxZoom, 17), TerrainTileBytes));
        if (plan.BaseLayers.IncludeSatellite)
            layers.Add(EstimateLayer("Satellite raster", area, minZoom, maxZoom, SatelliteTileBytes));
        if (plan.BaseLayers.IncludeContours)
            layers.Add(EstimateLayer("Contour raster", area, minZoom, maxZoom, ContourTileBytes));

        var totalTiles = layers.Sum(static l => l.TileCount);
        var estimatedBytes = layers.Sum(static l => l.EstimatedBytes);
        var poiRows = EstimatePoiRows(plan.Poi);
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{totalTiles:N0} tiles, approx {FormatBytes(estimatedBytes)}");

        return new MapPackEstimate
        {
            Layers = layers,
            TotalTileCount = totalTiles,
            EstimatedTileBytes = estimatedBytes,
            EstimatedPoiCount = plan.Poi.EnablePoiSeparation ? -1 : 0,
            EstimatedPoiIndexRows = poiRows,
            Summary = summary,
        };
    }

    public static long CountTiles(GeoBounds bounds, int minZoom, int maxZoom)
    {
        var normalized = bounds.Normalize();
        var min = Math.Clamp(minZoom, TileMath.MinimumZoom, TileMath.MaximumZoom);
        var max = Math.Clamp(maxZoom, TileMath.MinimumZoom, TileMath.MaximumZoom);
        if (max < min)
            (min, max) = (max, min);

        var total = 0L;
        for (var zoom = min; zoom <= max; zoom++)
        {
            total += TileMath.BoundsToTileRange(normalized, zoom).TileCount;
        }

        return total;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        var units = new[] { "KB", "MB", "GB", "TB" };
        var value = bytes / 1024.0;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024.0;
            unit++;
        }

        return value >= 100
            ? string.Create(CultureInfo.InvariantCulture, $"{value:F0} {units[unit]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:F1} {units[unit]}");
    }

    private static MapLayerEstimate EstimateLayer(
        string name,
        GeoBounds bounds,
        int minZoom,
        int maxZoom,
        long bytesPerTile)
    {
        var tileCount = maxZoom < minZoom
            ? 0
            : CountTiles(bounds, minZoom, maxZoom);
        return new MapLayerEstimate(name, tileCount, tileCount * bytesPerTile);
    }

    private static long EstimatePoiRows(MapPackPoiSelection poi)
    {
        if (!poi.EnablePoiSeparation || !poi.GenerateTileIndex)
            return 0;

        var options = poi.IndexOptions.Normalize();
        var zoomCount = Math.Max(0, options.MaxZoom - options.MinZoom + 1);
        return zoomCount == 0 ? 0 : -1;
    }
}
