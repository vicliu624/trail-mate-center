using System;
using System.Collections.Generic;
namespace TrailMateCenter.Unity.Rendering
{
public readonly struct RasterBounds
{
    public RasterBounds(double minX, double minZ, double maxX, double maxZ)
    {
        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
    }

    public double MinX { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxZ { get; }

    public bool IsValid =>
        !double.IsNaN(MinX) &&
        !double.IsNaN(MinZ) &&
        !double.IsNaN(MaxX) &&
        !double.IsNaN(MaxZ) &&
        MaxX > MinX &&
        MaxZ > MinZ;
}

public sealed class RasterMetadata
{
    public RasterBounds? Bounds { get; set; }
    public string Crs { get; set; } = string.Empty;
    public string TileTemplateUri { get; set; } = string.Empty;
    public int? MinZoom { get; set; }
    public int? MaxZoom { get; set; }
    public int? TileSize { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? NoDataValue { get; set; }
    public double? ValueScale { get; set; }
    public double? ValueOffset { get; set; }
    public IReadOnlyList<double> ClassBreaks { get; set; } = Array.Empty<double>();
    public string Unit { get; set; } = string.Empty;
    public string Palette { get; set; } = string.Empty;

    public bool HasTileSource => !string.IsNullOrWhiteSpace(TileTemplateUri) && Bounds != null && Bounds.Value.IsValid;
}

public sealed class RasterLoadResult
{
    public string SourcePath { get; set; }
    public string TexturePath { get; set; }
    public UnityEngine.Texture2D Texture { get; set; }
    public RasterMetadata Metadata { get; set; } = new();
}
}

