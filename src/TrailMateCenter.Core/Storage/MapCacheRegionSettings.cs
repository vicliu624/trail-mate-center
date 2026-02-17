namespace TrailMateCenter.Storage;

public sealed record MapCacheRegionSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Area";
    public double West { get; init; }
    public double South { get; init; }
    public double East { get; init; }
    public double North { get; init; }
    public bool IncludeOsm { get; init; } = true;
    public bool IncludeTerrain { get; init; } = true;
    public bool IncludeSatellite { get; init; } = true;
    public bool IncludeContours { get; init; } = true;
    public bool IncludeUltraFineContours { get; init; }
}
