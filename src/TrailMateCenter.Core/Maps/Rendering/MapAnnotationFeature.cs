using NetTopologySuite.Geometries;

namespace TrailMateCenter.Maps.Rendering;

/// <summary>Disk-staged source geometry, not an already selected screen label.</summary>
internal sealed record MapAnnotationFeature(
    string Id,
    MapAnnotationRule Rule,
    string Name,
    string? Reference,
    Geometry Geometry);
