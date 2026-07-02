namespace TrailMateCenter.Maps;

public enum MapPackBaseLayer
{
    Osm = 0,
    Terrain = 1,
    Satellite = 2,
}

public sealed record MapPackBaseLayerSelection
{
    public bool IncludeOsm { get; init; } = true;
    public bool IncludeTerrain { get; init; }
    public bool IncludeSatellite { get; init; }
    public bool IncludeContours { get; init; }
    public bool IncludeUltraFineContours { get; init; }
    public int MinimumZoom { get; init; } = 0;
    public int MaximumZoom { get; init; } = 18;
}

public sealed record MapPackPoiSelection
{
    public bool EnablePoiSeparation { get; init; }
    public string PbfPath { get; init; } = string.Empty;
    public string SourceProvider { get; init; } = "local";
    public string SourceDownloadUrl { get; init; } = string.Empty;
    public bool GenerateFullPoisJsonl { get; init; } = true;
    public bool GenerateTileIndex { get; init; } = true;
    public IReadOnlyCollection<string> SelectedPoiTypes { get; init; } = Array.Empty<string>();
    public PoiIndexOptions IndexOptions { get; init; } = new();
}

public sealed record MapPackAreaSelection
{
    public string Name { get; init; } = "Selection";
    public int? AdminLevel { get; init; }
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
}

public sealed record MapPackExportPlan
{
    public string Name { get; init; } = "Map Pack";
    public MapPackAreaSelection Area { get; init; } = new();
    public MapPackBaseLayerSelection BaseLayers { get; init; } = new();
    public MapPackPoiSelection Poi { get; init; } = new();
    public string OutputDirectory { get; init; } = string.Empty;
}

public sealed record MapLayerEstimate(string Name, long TileCount, long EstimatedBytes);

public sealed record MapPackEstimate
{
    public IReadOnlyList<MapLayerEstimate> Layers { get; init; } = Array.Empty<MapLayerEstimate>();
    public long TotalTileCount { get; init; }
    public long EstimatedTileBytes { get; init; }
    public long EstimatedPoiCount { get; init; } = -1;
    public long EstimatedPoiIndexRows { get; init; } = -1;
    public string Summary { get; init; } = string.Empty;
}
