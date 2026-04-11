namespace TrailMateCenter.Services;

public enum PropagationSimulationMode
{
    CoverageMap = 0,
    InterferenceAnalysis = 1,
    RelayOptimization = 2,
    AdvancedModeling = 3,
}

public enum PropagationJobState
{
    Queued = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5,
}

public sealed class PropagationSimulationRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public PropagationSimulationMode Mode { get; init; } = PropagationSimulationMode.CoverageMap;
    public string? SourceRunId { get; init; }
    public string OptimizationAlgorithm { get; init; } = "Greedy";
    public string ScenarioPresetName { get; init; } = "Mountain Rescue";
    public IReadOnlyList<PropagationSiteInput> Sites { get; init; } = Array.Empty<PropagationSiteInput>();
    public PropagationTerrainInput? TerrainInput { get; init; }

    public double FrequencyMHz { get; init; } = 915;
    public double TxPowerDbm { get; init; } = 20;
    public string UplinkSpreadingFactor { get; init; } = "SF10";
    public string DownlinkSpreadingFactor { get; init; } = "SF10";
    public double EnvironmentLossDb { get; init; } = 6;
    public double VegetationAlphaSparse { get; init; } = 0.3;
    public double VegetationAlphaDense { get; init; } = 0.8;
    public double ShadowSigmaDb { get; init; } = 8;
    public double ReflectionCoeff { get; init; } = 0.2;
    public bool EnableMonteCarlo { get; init; }
    public int MonteCarloIterations { get; init; } = 120;

    public string DemVersion { get; init; } = "dem_20260220_v1";
    public string LandcoverVersion { get; init; } = "lc_20260218_v3";
    public string SurfaceVersion { get; init; } = "surface_default_v1";
}

public enum PropagationSiteRole
{
    BaseStation = 0,
    TargetNode = 1,
}

public enum PropagationLandcoverClass
{
    BareGround = 0,
    SparseForest = 1,
    DenseForest = 2,
    Water = 3,
}

public sealed class PropagationSiteInput
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public PropagationSiteRole Role { get; init; } = PropagationSiteRole.TargetNode;
    public string ColorHex { get; init; } = string.Empty;
    public double X { get; init; }
    public double Z { get; init; }
    public double? ElevationM { get; init; }
    public double AntennaHeightM { get; init; } = 14;
    public double FrequencyMHz { get; init; }
    public double TxPowerDbm { get; init; }
    public string SpreadingFactor { get; init; } = string.Empty;
}

public sealed class PropagationTerrainInput
{
    public string AoiId { get; init; } = string.Empty;
    public string Crs { get; init; } = "EPSG:3857";
    public double MinX { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; }
    public double MaxZ { get; init; }
    public double SampleStepM { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public IReadOnlyList<double> ElevationSamples { get; init; } = Array.Empty<double>();
    public IReadOnlyList<PropagationLandcoverClass> LandcoverSamples { get; init; } = Array.Empty<PropagationLandcoverClass>();
}

public sealed class PropagationRunHandle
{
    public string RunId { get; init; } = string.Empty;
    public PropagationJobState InitialState { get; init; } = PropagationJobState.Queued;
}

public sealed class PropagationSimulationUpdate
{
    public string RunId { get; init; } = string.Empty;
    public PropagationJobState State { get; init; } = PropagationJobState.Queued;
    public double ProgressPercent { get; init; }
    public string Stage { get; init; } = string.Empty;
    public bool CacheHit { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PropagationSimulationResult
{
    public PropagationRunMeta RunMeta { get; init; } = new();
    public PropagationInputBundle InputBundle { get; init; } = new();
    public PropagationModelOutputs ModelOutputs { get; init; } = new();
    public PropagationAnalysisOutputs AnalysisOutputs { get; init; } = new();
    public PropagationProvenance Provenance { get; init; } = new();
    public PropagationQualityFlags QualityFlags { get; init; } = new();
    public PropagationSceneGeometry SceneGeometry { get; init; } = new();
}

public sealed class PropagationRunMeta
{
    public string RunId { get; init; } = string.Empty;
    public PropagationJobState Status { get; init; } = PropagationJobState.Completed;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public double ProgressPercent { get; init; }
    public bool CacheHit { get; init; }
}

public sealed class PropagationInputBundle
{
    public string AoiId { get; init; } = "aoi_default";
    public string NodeSetVersion { get; init; } = "nodeset_default";
    public string ParameterSetVersion { get; init; } = "paramset_default";
}

public sealed class PropagationModelOutputs
{
    public string MeanCoverageRasterUri { get; init; } = string.Empty;
    public string Reliability95RasterUri { get; init; } = string.Empty;
    public string Reliability80RasterUri { get; init; } = string.Empty;
    public string InterferenceRasterUri { get; init; } = string.Empty;
    public string CapacityRasterUri { get; init; } = string.Empty;
    public IReadOnlyList<PropagationRasterLayerMetadata> RasterLayers { get; init; } = Array.Empty<PropagationRasterLayerMetadata>();
}

public sealed class PropagationRasterLayerMetadata
{
    public string LayerId { get; init; } = string.Empty;
    public string RasterUri { get; init; } = string.Empty;
    public string TileTemplateUri { get; init; } = string.Empty;
    public int? MinZoom { get; init; }
    public int? MaxZoom { get; init; }
    public int? TileSize { get; init; }
    public PropagationRasterBounds Bounds { get; init; } = new();
    public string Crs { get; init; } = string.Empty;
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public double? NoDataValue { get; init; }
    public double? ValueScale { get; init; }
    public double? ValueOffset { get; init; }
    public IReadOnlyList<double> ClassBreaks { get; init; } = Array.Empty<double>();
    public string Unit { get; init; } = string.Empty;
    public string Palette { get; init; } = string.Empty;
}

public sealed class PropagationRasterBounds
{
    public double MinX { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; }
    public double MaxZ { get; init; }
}

public sealed class PropagationAnalysisOutputs
{
    public PropagationLinkOutput Link { get; init; } = new();
    public PropagationTerrainOutput Terrain { get; init; } = new();
    public PropagationTerrainMapOutput TerrainMap { get; init; } = new();
    public PropagationReliabilityOutput Reliability { get; init; } = new();
    public PropagationLossBreakdownOutput LossBreakdown { get; init; } = new();
    public PropagationFresnelOutput Fresnel { get; init; } = new();
    public PropagationCoverageProbabilityOutput CoverageProbability { get; init; } = new();
    public PropagationNetworkOutput Network { get; init; } = new();
    public PropagationProfileOutput Profile { get; init; } = new();
    public PropagationOptimizationOutput Optimization { get; init; } = new();
    public PropagationReflectionOutput Reflection { get; init; } = new();
    public PropagationUncertaintyOutput Uncertainty { get; init; } = new();
    public PropagationCalibrationOutput Calibration { get; init; } = new();
    public PropagationSpatialAlignmentOutput SpatialAlignment { get; init; } = new();
}

public sealed class PropagationTerrainMapOutput
{
    public string Crs { get; init; } = string.Empty;
    public double MinX { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; }
    public double MaxZ { get; init; }
    public double WidthM { get; init; }
    public double HeightM { get; init; }
    public double SampleStepM { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public double MinElevationM { get; init; }
    public double MaxElevationM { get; init; }
    public IReadOnlyList<double> ElevationSamples { get; init; } = Array.Empty<double>();
    public IReadOnlyList<PropagationLandcoverClass> LandcoverSamples { get; init; } = Array.Empty<PropagationLandcoverClass>();
    public IReadOnlyList<PropagationScenePolyline> ContourLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePoint> Sites { get; init; } = Array.Empty<PropagationScenePoint>();
}

public sealed class PropagationLinkOutput
{
    public double DownlinkRssiDbm { get; init; }
    public double UplinkRssiDbm { get; init; }
    public double DownlinkMarginDb { get; init; }
    public double UplinkMarginDb { get; init; }
    public bool LinkFeasible { get; init; }
    public string MarginGuardrail { get; init; } = string.Empty;
}

public sealed class PropagationTerrainOutput
{
    public bool IsLineOfSight { get; init; }
    public string PathState { get; init; } = string.Empty;
    public string DominantObstructionLabel { get; init; } = string.Empty;
    public double DominantObstructionDistanceKm { get; init; }
    public double DominantObstructionHeightM { get; init; }
    public double ObstructionAboveLosM { get; init; }
    public double SampleStepM { get; init; }
}

public sealed class PropagationReliabilityOutput
{
    public double P95 { get; init; }
    public double P80 { get; init; }
    public string ConfidenceNote { get; init; } = string.Empty;
}

public sealed class PropagationLossBreakdownOutput
{
    public double FsplDb { get; init; }
    public double DiffractionDb { get; init; }
    public double FresnelDb { get; init; }
    public double VegetationDb { get; init; }
    public double ReflectionDb { get; init; }
    public double ShadowDb { get; init; }
    public double EnvironmentDb { get; init; }
    public double TotalDb { get; init; }
}

public sealed class PropagationFresnelOutput
{
    public double RadiusM { get; init; }
    public double ClearanceRatio { get; init; }
    public double MinimumClearanceM { get; init; }
    public double AdditionalLossDb { get; init; }
    public string RiskLevel { get; init; } = string.Empty;
}

public sealed class PropagationCoverageProbabilityOutput
{
    public double AreaP95Km2 { get; init; }
    public double AreaP80Km2 { get; init; }
    public double ThresholdRssiDbm { get; init; }
    public double ReliableReachKm { get; init; }
}

public sealed class PropagationNetworkOutput
{
    public double SinrDb { get; init; }
    public double ConflictRate { get; init; }
    public int MaxCapacityNodes { get; init; }
    public double AlohaLoadPercent { get; init; }
    public double ChannelOccupancyPercent { get; init; }
    public double AirtimeMs { get; init; }
}

public sealed class PropagationProfileOutput
{
    public double DistanceKm { get; init; }
    public double FresnelRadiusM { get; init; }
    public double MarginDb { get; init; }
    public PropagationMainObstacle MainObstacle { get; init; } = new();
    public IReadOnlyList<PropagationProfileSample> Samples { get; init; } = Array.Empty<PropagationProfileSample>();
}

public sealed class PropagationMainObstacle
{
    public string Label { get; init; } = string.Empty;
    public double V { get; init; }
    public double LdDb { get; init; }
}

public sealed class PropagationOptimizationOutput
{
    public string Algorithm { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public string ConstraintSummary { get; init; } = string.Empty;
    public string RecommendedPlanId { get; init; } = string.Empty;
    public IReadOnlyList<PropagationRelayPlan> TopPlans { get; init; } = Array.Empty<PropagationRelayPlan>();
}

public sealed class PropagationRelayPlan
{
    public string PlanId { get; init; } = string.Empty;
    public double Score { get; init; }
    public double CoverageGain { get; init; }
    public double ReliabilityGain { get; init; }
    public double BlindAreaPenalty { get; init; }
    public double InterferencePenalty { get; init; }
    public double CostPenalty { get; init; }
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<string> SiteIds { get; init; } = Array.Empty<string>();
}

public sealed class PropagationReflectionOutput
{
    public bool Enabled { get; init; }
    public double ReflectionCoefficient { get; init; }
    public double RelativeGainDb { get; init; }
    public double ExcessDelayNs { get; init; }
    public string MultipathRisk { get; init; } = string.Empty;
}

public sealed class PropagationUncertaintyOutput
{
    public int Iterations { get; init; }
    public double CiLower { get; init; }
    public double CiUpper { get; init; }
    public double StabilityIndex { get; init; }
    public double MarginP10Db { get; init; }
    public double MarginP50Db { get; init; }
    public double MarginP90Db { get; init; }
    public string SensitivitySummary { get; init; } = string.Empty;
}

public sealed class PropagationCalibrationOutput
{
    public int TrainingSampleCount { get; init; }
    public int ValidationSampleCount { get; init; }
    public double MaeBefore { get; init; }
    public double MaeAfter { get; init; }
    public double MaeDelta { get; init; }
    public double RmseBefore { get; init; }
    public double RmseAfter { get; init; }
    public double RmseDelta { get; init; }
    public double ValidationMaeAfter { get; init; }
    public double ValidationRmseAfter { get; init; }
    public IReadOnlyList<PropagationParameterAdjustment> ParameterAdjustments { get; init; } = Array.Empty<PropagationParameterAdjustment>();
    public string CalibrationRunId { get; init; } = string.Empty;
}

public sealed class PropagationSpatialAlignmentOutput
{
    public string TargetCrs { get; init; } = string.Empty;
    public string DemResamplingMethod { get; init; } = string.Empty;
    public string LandcoverResamplingMethod { get; init; } = string.Empty;
    public double DemResolutionM { get; init; }
    public double LandcoverResolutionM { get; init; }
    public double HorizontalOffsetM { get; init; }
    public double VerticalOffsetM { get; init; }
    public double AlignmentScore { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class PropagationParameterAdjustment
{
    public string Name { get; init; } = string.Empty;
    public double Before { get; init; }
    public double After { get; init; }
    public string Unit { get; init; } = string.Empty;
}

public sealed class PropagationProfileSample
{
    public int Index { get; init; }
    public double DistanceKm { get; init; }
    public double TerrainElevationM { get; init; }
    public double LosHeightM { get; init; }
    public double FresnelRadiusM { get; init; }
    public bool IsBlocked { get; init; }
    public string SurfaceType { get; init; } = string.Empty;
}

public sealed class PropagationProvenance
{
    public PropagationDatasetBundle DatasetBundle { get; init; } = new();
    public string ModelVersion { get; init; } = "prop_core_0.3.0";
    public string GitCommit { get; init; } = "local-dev";
    public string ParameterHash { get; init; } = string.Empty;
}

public sealed class PropagationDatasetBundle
{
    public string DemVersion { get; init; } = string.Empty;
    public string LandcoverVersion { get; init; } = string.Empty;
    public string SurfaceVersion { get; init; } = string.Empty;
}

public sealed class PropagationQualityFlags
{
    public IReadOnlyList<string> AssumptionFlags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidityWarnings { get; init; } = Array.Empty<string>();
}

public sealed class PropagationSceneGeometry
{
    public IReadOnlyList<PropagationScenePoint> RelayCandidates { get; init; } = Array.Empty<PropagationScenePoint>();
    public IReadOnlyList<PropagationScenePoint> RelayRecommendations { get; init; } = Array.Empty<PropagationScenePoint>();
    public IReadOnlyList<PropagationScenePoint> ProfileObstacles { get; init; } = Array.Empty<PropagationScenePoint>();
    public IReadOnlyList<PropagationScenePolyline> ProfileLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePolyline> RidgeLines { get; init; } = Array.Empty<PropagationScenePolyline>();
}

public sealed class PropagationScenePoint
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string ColorHex { get; init; } = string.Empty;
    public double X { get; init; }
    public double Z { get; init; }
    public double? Y { get; init; }
    public double? Score { get; init; }
}

public sealed class PropagationScenePolyline
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<PropagationScenePolylinePoint> Points { get; init; } = Array.Empty<PropagationScenePolylinePoint>();
}

public sealed class PropagationScenePolylinePoint
{
    public double X { get; init; }
    public double Z { get; init; }
    public double? Y { get; init; }
}

public sealed class PropagationExportResult
{
    public string RunId { get; init; } = string.Empty;
    public string ExportPath { get; init; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
