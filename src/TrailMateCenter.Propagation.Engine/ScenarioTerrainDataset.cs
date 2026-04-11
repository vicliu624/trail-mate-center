using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

internal sealed record class ScenarioTerrainDataset
{
    public string DatasetId { get; init; } = string.Empty;
    public string NodeSetVersion { get; init; } = string.Empty;
    public string TargetCrs { get; init; } = "LOCAL_TERRAIN_M";
    public double ResolutionM { get; init; } = 10;
    public double MinX { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; } = 3200;
    public double MaxZ { get; init; } = 2200;
    public double WidthM => Math.Max(0d, MaxX - MinX);
    public double HeightM => Math.Max(0d, MaxZ - MinZ);
    public double MinElevationM { get; init; }
    public double MaxElevationM { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public IReadOnlyList<double> ElevationSamples { get; init; } = Array.Empty<double>();
    public IReadOnlyList<PropagationLandcoverClass> LandcoverSamples { get; init; } = Array.Empty<PropagationLandcoverClass>();
    public IReadOnlyList<ScenarioSite> RelaySeedSites { get; init; } = Array.Empty<ScenarioSite>();
    public IReadOnlyList<PropagationScenePolyline> RidgeLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<double> ContourBreaks { get; init; } = Array.Empty<double>();

    public bool HasRuntimeGrid => Columns > 0 && Rows > 0 && ElevationSamples.Count >= Columns * Rows;
    public bool HasLandcoverGrid => Columns > 0 && Rows > 0 && LandcoverSamples.Count >= Columns * Rows;

    public static ScenarioTerrainDataset Create(PropagationSimulationRequest request)
    {
        if (request.TerrainInput is null)
            throw new InvalidOperationException("Propagation terrain input is missing. The solver now requires a real DEM-backed terrain grid from the current map viewport.");

        var terrainInput = request.TerrainInput;
        if (terrainInput.Columns <= 0 || terrainInput.Rows <= 0)
            throw new InvalidOperationException("Propagation terrain input is invalid. The DEM grid dimensions must be greater than zero.");

        var expectedSampleCount = terrainInput.Columns * terrainInput.Rows;
        if (terrainInput.ElevationSamples.Count < expectedSampleCount)
            throw new InvalidOperationException("Propagation terrain input is incomplete. DEM samples do not match the declared grid dimensions.");

        var minX = Math.Min(terrainInput.MinX, terrainInput.MaxX);
        var minZ = Math.Min(terrainInput.MinZ, terrainInput.MaxZ);
        var maxX = Math.Max(terrainInput.MinX, terrainInput.MaxX);
        var maxZ = Math.Max(terrainInput.MinZ, terrainInput.MaxZ);
        if (maxX <= minX || maxZ <= minZ)
            throw new InvalidOperationException("Propagation terrain input is invalid. AOI bounds are empty.");

        var finiteElevations = terrainInput.ElevationSamples
            .Where(static value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        if (finiteElevations.Length == 0)
            throw new InvalidOperationException("Propagation terrain input does not contain usable elevation samples.");

        return new ScenarioTerrainDataset
        {
            DatasetId = string.IsNullOrWhiteSpace(terrainInput.AoiId)
                ? $"aoi_runtime_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}"
                : terrainInput.AoiId,
            NodeSetVersion = "nodeset_runtime_viewport_v1",
            TargetCrs = string.IsNullOrWhiteSpace(terrainInput.Crs) ? "EPSG:3857" : terrainInput.Crs,
            ResolutionM = terrainInput.SampleStepM > 0
                ? terrainInput.SampleStepM
                : Math.Max((maxX - minX) / Math.Max(1, terrainInput.Columns), (maxZ - minZ) / Math.Max(1, terrainInput.Rows)),
            MinX = minX,
            MinZ = minZ,
            MaxX = maxX,
            MaxZ = maxZ,
            MinElevationM = finiteElevations.Min(),
            MaxElevationM = finiteElevations.Max(),
            Columns = terrainInput.Columns,
            Rows = terrainInput.Rows,
            ElevationSamples = terrainInput.ElevationSamples.ToArray(),
            LandcoverSamples = terrainInput.LandcoverSamples.ToArray(),
            RelaySeedSites = Array.Empty<ScenarioSite>(),
            RidgeLines = Array.Empty<PropagationScenePolyline>(),
            ContourBreaks = Array.Empty<double>(),
        };
    }

    public static ScenarioTerrainDataset Create(string presetName)
    {
        var normalized = string.IsNullOrWhiteSpace(presetName) ? "Mountain Rescue" : presetName.Trim();
        return normalized switch
        {
            "Forest Patrol" => CreateForestPatrol(),
            "Canyon Relay" => CreateCanyonRelay(),
            _ => CreateMountainRescue(),
        };
    }

    public double ElevationAt(double x, double z)
    {
        if (HasRuntimeGrid)
            return SampleCellCenteredElevation(x, z);

        var ridge = 1180
                    + 160 * Math.Exp(-Math.Pow((x - 980) / 420, 2) - Math.Pow((z - 620) / 280, 2))
                    + 120 * Math.Exp(-Math.Pow((x - 1680) / 500, 2) - Math.Pow((z - 1080) / 320, 2))
                    + 90 * Math.Exp(-Math.Pow((x - 2360) / 380, 2) - Math.Pow((z - 980) / 260, 2));
        var valley = -85 * Math.Exp(-Math.Pow((x - 1960) / 540, 2) - Math.Pow((z - 1520) / 240, 2));
        var waves = Math.Sin(x / 170) * 14 + Math.Cos(z / 160) * 11;
        return ridge + valley + waves;
    }

    public PropagationLandcoverClass LandcoverAt(double x, double z)
    {
        if (HasLandcoverGrid)
            return SampleCellCenteredLandcover(x, z);

        if (z > 1320 && x > 1720)
            return PropagationLandcoverClass.Water;
        if (x > 1100 && x < 2300 && z > 720 && z < 1320)
            return PropagationLandcoverClass.DenseForest;
        if (x > 600 && x < 2600 && z > 520 && z < 1700)
            return PropagationLandcoverClass.SparseForest;
        return PropagationLandcoverClass.BareGround;
    }

    public double ClampX(double x) => Math.Clamp(x, MinX, MaxX);

    public double ClampZ(double z) => Math.Clamp(z, MinZ, MaxZ);

    private double SampleCellCenteredElevation(double x, double z)
    {
        if (Columns <= 0 || Rows <= 0)
            return MinElevationM;

        var normalizedX = ResolveCellCenteredIndex(x, MinX, MaxX, Columns);
        var normalizedZ = ResolveCellCenteredIndex(z, MinZ, MaxZ, Rows);
        var x0 = Math.Clamp((int)Math.Floor(normalizedX), 0, Columns - 1);
        var z0 = Math.Clamp((int)Math.Floor(normalizedZ), 0, Rows - 1);
        var x1 = Math.Clamp(x0 + 1, 0, Columns - 1);
        var z1 = Math.Clamp(z0 + 1, 0, Rows - 1);
        var tx = Math.Clamp(normalizedX - x0, 0d, 1d);
        var tz = Math.Clamp(normalizedZ - z0, 0d, 1d);

        var e00 = ElevationSamples[(z0 * Columns) + x0];
        var e10 = ElevationSamples[(z0 * Columns) + x1];
        var e01 = ElevationSamples[(z1 * Columns) + x0];
        var e11 = ElevationSamples[(z1 * Columns) + x1];
        var e0 = Lerp(e00, e10, tx);
        var e1 = Lerp(e01, e11, tx);
        return Lerp(e0, e1, tz);
    }

    private PropagationLandcoverClass SampleCellCenteredLandcover(double x, double z)
    {
        if (Columns <= 0 || Rows <= 0 || LandcoverSamples.Count < Columns * Rows)
            return PropagationLandcoverClass.BareGround;

        var normalizedX = ResolveCellCenteredIndex(x, MinX, MaxX, Columns);
        var normalizedZ = ResolveCellCenteredIndex(z, MinZ, MaxZ, Rows);
        var col = Math.Clamp((int)Math.Round(normalizedX), 0, Columns - 1);
        var row = Math.Clamp((int)Math.Round(normalizedZ), 0, Rows - 1);
        return LandcoverSamples[(row * Columns) + col];
    }

    private static double ResolveCellCenteredIndex(double coordinate, double min, double max, int samples)
    {
        if (samples <= 1)
            return 0d;

        var clamped = Math.Clamp(coordinate, min, max);
        var span = Math.Max(1d, max - min);
        return (((clamped - min) / span) * samples) - 0.5d;
    }

    private static ScenarioTerrainDataset CreateMountainRescue()
    {
        return new ScenarioTerrainDataset
        {
            DatasetId = "aoi_mountain_rescue",
            NodeSetVersion = "nodeset_mountain_rescue_v1",
            TargetCrs = "LOCAL_TERRAIN_M",
            ResolutionM = 10,
            MinX = 0,
            MinZ = 0,
            MaxX = 3200,
            MaxZ = 2200,
            MinElevationM = 1120,
            MaxElevationM = 1520,
            RelaySeedSites =
            [
                new ScenarioSite("relay_r1", "Potential Relay Site", "#F0D14A", 1240, 1080, 1320, 12, 915, 17, "SF10", 0.91),
                new ScenarioSite("relay_r2", "Ridge Shoulder", "#F0D14A", 1650, 980, 1372, 12, 915, 17, "SF10", 0.86),
                new ScenarioSite("relay_r3", "East Spur", "#F0D14A", 2140, 860, 1364, 12, 915, 16, "SF11", 0.79),
                new ScenarioSite("relay_r4", "South Overlook", "#F0D14A", 1710, 1220, 1296, 12, 915, 16, "SF11", 0.73),
            ],
            RidgeLines =
            [
                BuildRidgeLine("ridge_main", [(760d, 480d), (1160d, 720d), (1620d, 940d), (2140d, 940d), (2480d, 860d)]),
                BuildRidgeLine("ridge_south", [(1320d, 1140d), (1580d, 1220d), (1830d, 1280d), (2080d, 1300d)]),
            ],
            ContourBreaks = [1180, 1240, 1300, 1360, 1420, 1480],
        };
    }

    private static ScenarioTerrainDataset CreateForestPatrol()
    {
        return CreateMountainRescue() with
        {
            DatasetId = "aoi_forest_patrol",
            NodeSetVersion = "nodeset_forest_patrol_v1",
        };
    }

    private static ScenarioTerrainDataset CreateCanyonRelay()
    {
        return CreateMountainRescue() with
        {
            DatasetId = "aoi_canyon_relay",
            NodeSetVersion = "nodeset_canyon_relay_v1",
        };
    }

    private static PropagationScenePolyline BuildRidgeLine(string id, IReadOnlyList<(double X, double Z)> points)
    {
        return new PropagationScenePolyline
        {
            Id = id,
            Points = points.Select(point => new PropagationScenePolylinePoint { X = point.X, Z = point.Z }).ToArray(),
        };
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}

internal sealed record ScenarioSite(
    string Id,
    string Label,
    string ColorHex,
    double X,
    double Z,
    double ElevationM,
    double AntennaHeightM,
    double FrequencyMHz,
    double TxPowerDbm,
    string SpreadingFactor,
    double PriorScore);
