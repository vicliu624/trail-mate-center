namespace TrailMateCenter.Storage;

public sealed record MapCacheRegionSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Area";
    public double West { get; init; }
    public double South { get; init; }
    public double East { get; init; }
    public double North { get; init; }
    public int? AdminLevel { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public bool IncludeOsm { get; init; } = true;
    public bool IncludeTerrain { get; init; } = true;
    public bool IncludeSatellite { get; init; } = true;
    public bool IncludeContours { get; init; } = true;
    public bool IncludeUltraFineContours { get; init; }
    public int MinimumZoom { get; init; } = 0;
    public int MaximumZoom { get; init; } = 18;
    public bool EnablePoiSeparation { get; init; }
    public string PoiPbfPath { get; init; } = string.Empty;
    public string PoiSourceProvider { get; init; } = "local";
    public string PoiSourceDownloadUrl { get; init; } = string.Empty;
    public bool GenerateFullPoisJsonl { get; init; } = true;
    public bool GenerateTileIndexedPoiFiles { get; init; } = true;
    public int PoiIndexMinimumZoom { get; init; } = 10;
    public int PoiIndexMaximumZoom { get; init; } = 17;
    public int MaxPoiPerTile { get; init; } = 200;
    public bool IncludePoiLabels { get; init; } = true;
    public bool IncludeOriginalOsmTags { get; init; }
    public string PoiOutputFormat { get; init; } = "readable";
    public IReadOnlyList<string> SelectedPoiTypes { get; init; } = Array.Empty<string>();
    public string ExportOutputDirectory { get; init; } = string.Empty;
    public string ExportState { get; init; } = "none";
    public long ExportProcessedTiles { get; init; }
    public long ExportExpectedTiles { get; init; }
    public long ExportSourceTiles { get; init; }
    public long ExportCopiedTiles { get; init; }
    public long ExportSkippedTiles { get; init; }
    public long ExportMissingTiles { get; init; }
    public long ExportUnreadableEntries { get; init; }
    public string ExportLastError { get; init; } = string.Empty;
    public long ExportUpdatedAtUnixTime { get; init; }
}
