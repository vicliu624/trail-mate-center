using System;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class GeoReferenceMappingResult
{
    public RasterBounds TerrainBounds { get; set; }
    public bool UsedFallback { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class GeoReferenceMapper
{
    public static GeoReferenceMappingResult MapToTerrain(
        RasterBounds sourceBounds,
        string sourceCrs,
        GeoReferenceConfig geoConfig,
        Terrain terrain)
    {
        var terrainWorldBounds = GetTerrainWorldBounds(terrain);
        var normalizedSource = NormalizeBounds(sourceBounds);
        if (!normalizedSource.IsValid)
        {
            return new GeoReferenceMappingResult
            {
                TerrainBounds = terrainWorldBounds,
                UsedFallback = true,
                Message = "Invalid source bounds, fallback to full terrain bounds."
            };
        }

        var sourceCrsKey = NormalizeCrs(sourceCrs);
        var terrainCrsKey = NormalizeCrs(geoConfig.TerrainCrs);
        if (AreBoundsClose(normalizedSource, terrainWorldBounds))
        {
            return new GeoReferenceMappingResult
            {
                TerrainBounds = normalizedSource,
                UsedFallback = false,
                Message = "Raster bounds already in terrain world range."
            };
        }

        if (!TryResolveSourceDomain(geoConfig, out var sourceDomain))
        {
            if (string.IsNullOrWhiteSpace(sourceCrsKey) || IsLocalCrs(sourceCrsKey))
            {
                return new GeoReferenceMappingResult
                {
                    TerrainBounds = normalizedSource,
                    UsedFallback = false,
                    Message = "Geo domain missing, keep local source bounds as terrain coordinates."
                };
            }

            return new GeoReferenceMappingResult
            {
                TerrainBounds = terrainWorldBounds,
                UsedFallback = true,
                Message = "Geo domain invalid, fallback to terrain bounds."
            };
        }

        var txMin = LerpByDomain(normalizedSource.MinX, sourceDomain.MinX, sourceDomain.MaxX, terrainWorldBounds.MinX, terrainWorldBounds.MaxX);
        var txMax = LerpByDomain(normalizedSource.MaxX, sourceDomain.MinX, sourceDomain.MaxX, terrainWorldBounds.MinX, terrainWorldBounds.MaxX);
        var tzMin = LerpByDomain(normalizedSource.MinZ, sourceDomain.MinZ, sourceDomain.MaxZ, terrainWorldBounds.MinZ, terrainWorldBounds.MaxZ);
        var tzMax = LerpByDomain(normalizedSource.MaxZ, sourceDomain.MinZ, sourceDomain.MaxZ, terrainWorldBounds.MinZ, terrainWorldBounds.MaxZ);

        var mapped = NormalizeBounds(new RasterBounds(txMin, tzMin, txMax, tzMax));
        if (!mapped.IsValid)
        {
            return new GeoReferenceMappingResult
            {
                TerrainBounds = terrainWorldBounds,
                UsedFallback = true,
                Message = "Mapped bounds invalid, fallback to terrain bounds."
            };
        }

        return new GeoReferenceMappingResult
        {
            TerrainBounds = mapped,
            UsedFallback = false,
            Message = $"Mapped source CRS {sourceCrsKey} to terrain world using domain CRS {terrainCrsKey}."
        };
    }

    public static RasterBounds GetTerrainWorldBounds(Terrain terrain)
    {
        var origin = terrain.GetPosition();
        var size = terrain.terrainData.size;
        return new RasterBounds(origin.x, origin.z, origin.x + size.x, origin.z + size.z);
    }

    private static string NormalizeCrs(string crs)
    {
        return (crs ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static bool TryResolveSourceDomain(GeoReferenceConfig geoConfig, out RasterBounds bounds)
    {
        bounds = default;
        if (geoConfig.EnableAoiBoundaryMask)
        {
            var aoi = NormalizeBounds(new RasterBounds(geoConfig.AoiMinX, geoConfig.AoiMinZ, geoConfig.AoiMaxX, geoConfig.AoiMaxZ));
            if (aoi.IsValid)
            {
                bounds = aoi;
                return true;
            }
        }

        var terrainDomain = NormalizeBounds(new RasterBounds(geoConfig.TerrainMinX, geoConfig.TerrainMinZ, geoConfig.TerrainMaxX, geoConfig.TerrainMaxZ));
        if (terrainDomain.IsValid)
        {
            bounds = terrainDomain;
            return true;
        }

        return false;
    }

    private static bool AreBoundsClose(RasterBounds left, RasterBounds right)
    {
        const double eps = 1e-3;
        return Math.Abs(left.MinX - right.MinX) <= eps &&
               Math.Abs(left.MaxX - right.MaxX) <= eps &&
               Math.Abs(left.MinZ - right.MinZ) <= eps &&
               Math.Abs(left.MaxZ - right.MaxZ) <= eps;
    }

    private static bool IsLocalCrs(string crs)
    {
        return string.Equals(crs, "LOCAL_TERRAIN_M", StringComparison.OrdinalIgnoreCase);
    }

    private static RasterBounds NormalizeBounds(RasterBounds bounds)
    {
        var minX = Math.Min(bounds.MinX, bounds.MaxX);
        var maxX = Math.Max(bounds.MinX, bounds.MaxX);
        var minZ = Math.Min(bounds.MinZ, bounds.MaxZ);
        var maxZ = Math.Max(bounds.MinZ, bounds.MaxZ);
        return new RasterBounds(minX, minZ, maxX, maxZ);
    }

    private static double LerpByDomain(double value, double sourceMin, double sourceMax, double targetMin, double targetMax)
    {
        var denom = sourceMax - sourceMin;
        if (Math.Abs(denom) < 1e-9)
            return targetMin;
        var t = (value - sourceMin) / denom;
        return targetMin + (targetMax - targetMin) * t;
    }
}
}

