using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using TrailMateCenter.Propagation.Grpc;

namespace TrailMateCenter.Services;

public sealed class GrpcPropagationSimulationService : IPropagationSimulationService, IDisposable
{
    private readonly ILogger<GrpcPropagationSimulationService> _logger;
    private readonly GrpcChannel _channel;
    private readonly PropagationService.PropagationServiceClient _client;

    public GrpcPropagationSimulationService(ILogger<GrpcPropagationSimulationService> logger)
    {
        _logger = logger;
        var endpoint = ResolveEndpoint();
        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }
        _channel = GrpcChannel.ForAddress(endpoint);
        _client = new PropagationService.PropagationServiceClient(_channel);
        _logger.LogInformation("Propagation gRPC client configured. Endpoint={Endpoint}", endpoint);
    }

    public async Task<PropagationRunHandle> StartSimulationAsync(PropagationSimulationRequest request, CancellationToken cancellationToken)
    {
        var reply = await _client.StartSimulationAsync(MapStartRequest(request), cancellationToken: cancellationToken);
        return new PropagationRunHandle
        {
            RunId = reply.RunId,
            InitialState = MapJobState(reply.InitialState),
        };
    }

    public async IAsyncEnumerable<PropagationSimulationUpdate> StreamSimulationUpdatesAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.StreamSimulationUpdates(new StreamSimulationUpdatesRequest { RunId = runId }, cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var update = call.ResponseStream.Current;
            yield return new PropagationSimulationUpdate
            {
                RunId = update.RunId,
                State = MapJobState(update.State),
                ProgressPercent = update.ProgressPct,
                Stage = update.Stage,
                CacheHit = update.CacheHit,
                Message = update.Message,
                TimestampUtc = update.TimestampUtc?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            };
        }
    }

    public async Task<PropagationSimulationResult> GetSimulationResultAsync(string runId, CancellationToken cancellationToken)
    {
        var response = await _client.GetSimulationResultAsync(new GetSimulationResultRequest { RunId = runId }, cancellationToken: cancellationToken);
        return MapResult(response);
    }

    public async Task PauseSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        await _client.PauseSimulationAsync(new PauseSimulationRequest { RunId = runId }, cancellationToken: cancellationToken);
    }

    public async Task ResumeSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        await _client.ResumeSimulationAsync(new ResumeSimulationRequest { RunId = runId }, cancellationToken: cancellationToken);
    }

    public async Task CancelSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        await _client.CancelJobAsync(new CancelJobRequest { JobId = runId }, cancellationToken: cancellationToken);
    }

    public async Task<PropagationExportResult> ExportResultAsync(string runId, string outputDirectory, CancellationToken cancellationToken)
    {
        var response = await _client.ExportResultAsync(new ExportResultRequest
        {
            RunId = runId,
            OutputDirectory = outputDirectory ?? string.Empty,
        }, cancellationToken: cancellationToken);

        return new PropagationExportResult
        {
            RunId = response.RunId,
            ExportPath = response.ExportPath,
            ExportedAtUtc = response.ExportedAtUtc?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
        };
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    private static string ResolveEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_GRPC_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint))
            return endpoint.Trim();
        return "http://127.0.0.1:51051";
    }

    private static StartSimulationRequest MapStartRequest(PropagationSimulationRequest request)
    {
        return new StartSimulationRequest
        {
            RequestId = request.RequestId,
            SourceRunId = request.SourceRunId ?? string.Empty,
            Mode = MapMode(request.Mode),
            OptimizationAlgorithm = request.OptimizationAlgorithm ?? string.Empty,
            ScenarioPresetName = request.ScenarioPresetName ?? string.Empty,
            RadioProfile = new RadioProfile
            {
                FrequencyMhz = request.FrequencyMHz,
                TxPowerDbm = request.TxPowerDbm,
                UplinkSf = request.UplinkSpreadingFactor,
                DownlinkSf = request.DownlinkSpreadingFactor,
            },
            ModelProfile = new ModelProfile
            {
                EnvironmentLossDb = request.EnvironmentLossDb,
                VegAlphaSparse = request.VegetationAlphaSparse,
                VegAlphaDense = request.VegetationAlphaDense,
                ShadowSigmaDb = request.ShadowSigmaDb,
                ReflectionCoeff = request.ReflectionCoeff,
                EnableMonteCarlo = request.EnableMonteCarlo,
                MonteCarloIterations = request.MonteCarloIterations,
            },
            DatasetSelector = new DatasetSelector
            {
                DemVersion = request.DemVersion,
                LandcoverVersion = request.LandcoverVersion,
                SurfaceVersion = request.SurfaceVersion,
            },
        };
    }

    private static PropagationSimulationResult MapResult(SimulationResultResponse response)
    {
        return new PropagationSimulationResult
        {
            RunMeta = new PropagationRunMeta
            {
                RunId = response.RunMeta?.RunId ?? string.Empty,
                Status = MapJobState(response.RunMeta?.Status ?? JobState.Unspecified),
                StartedAtUtc = response.RunMeta?.StartedAtUtc?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
                FinishedAtUtc = response.RunMeta?.FinishedAtUtc?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
                DurationMs = response.RunMeta?.DurationMs ?? 0,
                ProgressPercent = response.RunMeta?.ProgressPct ?? 0,
                CacheHit = response.RunMeta?.CacheHit ?? false,
            },
            InputBundle = new PropagationInputBundle
            {
                AoiId = response.InputBundle?.AoiId ?? string.Empty,
                NodeSetVersion = response.InputBundle?.NodeSetVersion ?? string.Empty,
                ParameterSetVersion = response.InputBundle?.ParameterSetVersion ?? string.Empty,
            },
            ModelOutputs = new PropagationModelOutputs
            {
                MeanCoverageRasterUri = response.ModelOutputs?.MeanCoverageRasterUri ?? string.Empty,
                Reliability95RasterUri = response.ModelOutputs?.Reliability95RasterUri ?? string.Empty,
                Reliability80RasterUri = response.ModelOutputs?.Reliability80RasterUri ?? string.Empty,
                InterferenceRasterUri = response.ModelOutputs?.InterferenceRasterUri ?? string.Empty,
                CapacityRasterUri = response.ModelOutputs?.CapacityRasterUri ?? string.Empty,
                RasterLayers = response.ModelOutputs?.RasterLayers?.Select(layer => new PropagationRasterLayerMetadata
                {
                    LayerId = layer.LayerId,
                    RasterUri = layer.RasterUri,
                    TileTemplateUri = layer.HasTileSchema ? layer.TileTemplateUri : string.Empty,
                    MinZoom = layer.HasTileSchema ? layer.MinZoom : null,
                    MaxZoom = layer.HasTileSchema ? layer.MaxZoom : null,
                    TileSize = layer.HasTileSchema ? layer.TileSize : null,
                    Bounds = new PropagationRasterBounds
                    {
                        MinX = layer.Bounds?.MinX ?? 0,
                        MinZ = layer.Bounds?.MinZ ?? 0,
                        MaxX = layer.Bounds?.MaxX ?? 0,
                        MaxZ = layer.Bounds?.MaxZ ?? 0,
                    },
                    Crs = layer.Crs,
                    MinValue = layer.HasMinMax ? layer.MinValue : null,
                    MaxValue = layer.HasMinMax ? layer.MaxValue : null,
                    NoDataValue = layer.HasNoData ? layer.NoDataValue : null,
                    ValueScale = layer.HasValueTransform ? layer.ValueScale : null,
                    ValueOffset = layer.HasValueTransform ? layer.ValueOffset : null,
                    ClassBreaks = layer.ClassBreaks.ToList(),
                    Unit = layer.Unit,
                    Palette = layer.Palette,
                }).ToList() ?? new List<PropagationRasterLayerMetadata>(),
            },
            AnalysisOutputs = new PropagationAnalysisOutputs
            {
                Link = new PropagationLinkOutput
                {
                    DownlinkRssiDbm = response.AnalysisOutputs?.Link?.DownlinkRssiDbm ?? 0,
                    UplinkRssiDbm = response.AnalysisOutputs?.Link?.UplinkRssiDbm ?? 0,
                    DownlinkMarginDb = response.AnalysisOutputs?.Link?.DownlinkMarginDb ?? 0,
                    UplinkMarginDb = response.AnalysisOutputs?.Link?.UplinkMarginDb ?? 0,
                    LinkFeasible = response.AnalysisOutputs?.Link?.LinkFeasible ?? false,
                    MarginGuardrail = response.AnalysisOutputs?.Link?.MarginGuardrail ?? string.Empty,
                },
                Terrain = new PropagationTerrainOutput
                {
                    IsLineOfSight = response.AnalysisOutputs?.Terrain?.IsLineOfSight ?? false,
                    PathState = response.AnalysisOutputs?.Terrain?.PathState ?? string.Empty,
                    DominantObstructionLabel = response.AnalysisOutputs?.Terrain?.DominantObstructionLabel ?? string.Empty,
                    DominantObstructionDistanceKm = response.AnalysisOutputs?.Terrain?.DominantObstructionDistanceKm ?? 0,
                    DominantObstructionHeightM = response.AnalysisOutputs?.Terrain?.DominantObstructionHeightM ?? 0,
                    ObstructionAboveLosM = response.AnalysisOutputs?.Terrain?.ObstructionAboveLosM ?? 0,
                    SampleStepM = response.AnalysisOutputs?.Terrain?.SampleStepM ?? 0,
                },
                TerrainMap = new PropagationTerrainMapOutput
                {
                    Crs = response.AnalysisOutputs?.TerrainMap?.Crs ?? string.Empty,
                    WidthM = response.AnalysisOutputs?.TerrainMap?.WidthM ?? 0,
                    HeightM = response.AnalysisOutputs?.TerrainMap?.HeightM ?? 0,
                    SampleStepM = response.AnalysisOutputs?.TerrainMap?.SampleStepM ?? 0,
                    Columns = response.AnalysisOutputs?.TerrainMap?.Columns ?? 0,
                    Rows = response.AnalysisOutputs?.TerrainMap?.Rows ?? 0,
                    MinElevationM = response.AnalysisOutputs?.TerrainMap?.MinElevationM ?? 0,
                    MaxElevationM = response.AnalysisOutputs?.TerrainMap?.MaxElevationM ?? 0,
                    ElevationSamples = response.AnalysisOutputs?.TerrainMap?.ElevationSamples?.ToList() ?? new List<double>(),
                    LandcoverSamples = response.AnalysisOutputs?.TerrainMap?.LandcoverSamples?.Select(MapLandcoverClass).ToList() ?? new List<PropagationLandcoverClass>(),
                    ContourLines = response.AnalysisOutputs?.TerrainMap?.ContourLines?.Select(line => new PropagationScenePolyline
                    {
                        Id = line.Id,
                        Points = line.Points.Select(point => new PropagationScenePolylinePoint
                        {
                            X = point.X,
                            Z = point.Z,
                            Y = point.HasY ? point.Y : null,
                        }).ToList(),
                    }).ToList() ?? new List<PropagationScenePolyline>(),
                    Sites = response.AnalysisOutputs?.TerrainMap?.Sites?.Select(MapScenePoint).ToList() ?? new List<PropagationScenePoint>(),
                },
                Reliability = new PropagationReliabilityOutput
                {
                    P95 = response.AnalysisOutputs?.Reliability?.P95 ?? 0,
                    P80 = response.AnalysisOutputs?.Reliability?.P80 ?? 0,
                    ConfidenceNote = response.AnalysisOutputs?.Reliability?.ConfidenceNote ?? string.Empty,
                },
                LossBreakdown = new PropagationLossBreakdownOutput
                {
                    FsplDb = response.AnalysisOutputs?.LossBreakdown?.FsplDb ?? 0,
                    DiffractionDb = response.AnalysisOutputs?.LossBreakdown?.DiffractionDb ?? 0,
                    FresnelDb = response.AnalysisOutputs?.LossBreakdown?.FresnelDb ?? 0,
                    VegetationDb = response.AnalysisOutputs?.LossBreakdown?.VegetationDb ?? 0,
                    ReflectionDb = response.AnalysisOutputs?.LossBreakdown?.ReflectionDb ?? 0,
                    ShadowDb = response.AnalysisOutputs?.LossBreakdown?.ShadowDb ?? 0,
                    EnvironmentDb = response.AnalysisOutputs?.LossBreakdown?.EnvironmentDb ?? 0,
                    TotalDb = response.AnalysisOutputs?.LossBreakdown?.TotalDb ?? 0,
                },
                Fresnel = new PropagationFresnelOutput
                {
                    RadiusM = response.AnalysisOutputs?.Fresnel?.RadiusM ?? 0,
                    ClearanceRatio = response.AnalysisOutputs?.Fresnel?.ClearanceRatio ?? 0,
                    MinimumClearanceM = response.AnalysisOutputs?.Fresnel?.MinimumClearanceM ?? 0,
                    AdditionalLossDb = response.AnalysisOutputs?.Fresnel?.AdditionalLossDb ?? 0,
                    RiskLevel = response.AnalysisOutputs?.Fresnel?.RiskLevel ?? string.Empty,
                },
                CoverageProbability = new PropagationCoverageProbabilityOutput
                {
                    AreaP95Km2 = response.AnalysisOutputs?.CoverageProbability?.AreaP95Km2 ?? 0,
                    AreaP80Km2 = response.AnalysisOutputs?.CoverageProbability?.AreaP80Km2 ?? 0,
                    ThresholdRssiDbm = response.AnalysisOutputs?.CoverageProbability?.ThresholdRssiDbm ?? 0,
                    ReliableReachKm = response.AnalysisOutputs?.CoverageProbability?.ReliableReachKm ?? 0,
                },
                Network = new PropagationNetworkOutput
                {
                    SinrDb = response.AnalysisOutputs?.Network?.SinrDb ?? 0,
                    ConflictRate = response.AnalysisOutputs?.Network?.ConflictRate ?? 0,
                    MaxCapacityNodes = response.AnalysisOutputs?.Network?.MaxCapacityNodes ?? 0,
                    AlohaLoadPercent = response.AnalysisOutputs?.Network?.AlohaLoadPercent ?? 0,
                    ChannelOccupancyPercent = response.AnalysisOutputs?.Network?.ChannelOccupancyPercent ?? 0,
                    AirtimeMs = response.AnalysisOutputs?.Network?.AirtimeMs ?? 0,
                },
                Profile = new PropagationProfileOutput
                {
                    DistanceKm = response.AnalysisOutputs?.Profile?.DistanceKm ?? 0,
                    FresnelRadiusM = response.AnalysisOutputs?.Profile?.FresnelRadiusM ?? 0,
                    MarginDb = response.AnalysisOutputs?.Profile?.MarginDb ?? 0,
                    MainObstacle = new PropagationMainObstacle
                    {
                        Label = response.AnalysisOutputs?.Profile?.MainObstacle?.Label ?? string.Empty,
                        V = response.AnalysisOutputs?.Profile?.MainObstacle?.V ?? 0,
                        LdDb = response.AnalysisOutputs?.Profile?.MainObstacle?.LdDb ?? 0,
                    },
                    Samples = response.AnalysisOutputs?.Profile?.Samples?.Select(sample => new PropagationProfileSample
                    {
                        Index = sample.Index,
                        DistanceKm = sample.DistanceKm,
                        TerrainElevationM = sample.TerrainElevationM,
                        LosHeightM = sample.LosHeightM,
                        FresnelRadiusM = sample.FresnelRadiusM,
                        IsBlocked = sample.IsBlocked,
                        SurfaceType = sample.SurfaceType,
                    }).ToList() ?? new List<PropagationProfileSample>(),
                },
                Optimization = new PropagationOptimizationOutput
                {
                    Algorithm = response.AnalysisOutputs?.Optimization?.Algorithm ?? string.Empty,
                    CandidateCount = response.AnalysisOutputs?.Optimization?.CandidateCount ?? 0,
                    ConstraintSummary = response.AnalysisOutputs?.Optimization?.ConstraintSummary ?? string.Empty,
                    RecommendedPlanId = response.AnalysisOutputs?.Optimization?.RecommendedPlanId ?? string.Empty,
                    TopPlans = response.AnalysisOutputs?.Optimization?.TopPlans?.Select(plan => new PropagationRelayPlan
                    {
                        PlanId = plan.PlanId,
                        Score = plan.Score,
                        CoverageGain = plan.CoverageGain,
                        ReliabilityGain = plan.ReliabilityGain,
                        BlindAreaPenalty = plan.BlindAreaPenalty,
                        InterferencePenalty = plan.InterferencePenalty,
                        CostPenalty = plan.CostPenalty,
                        Explanation = plan.Explanation,
                        SiteIds = plan.SiteIds.ToList(),
                    }).ToList() ?? new List<PropagationRelayPlan>(),
                },
                Reflection = new PropagationReflectionOutput
                {
                    Enabled = response.AnalysisOutputs?.Reflection?.Enabled ?? false,
                    ReflectionCoefficient = response.AnalysisOutputs?.Reflection?.ReflectionCoefficient ?? 0,
                    RelativeGainDb = response.AnalysisOutputs?.Reflection?.RelativeGainDb ?? 0,
                    ExcessDelayNs = response.AnalysisOutputs?.Reflection?.ExcessDelayNs ?? 0,
                    MultipathRisk = response.AnalysisOutputs?.Reflection?.MultipathRisk ?? string.Empty,
                },
                Uncertainty = new PropagationUncertaintyOutput
                {
                    Iterations = response.AnalysisOutputs?.Uncertainty?.Iterations ?? 0,
                    CiLower = response.AnalysisOutputs?.Uncertainty?.CiLower ?? 0,
                    CiUpper = response.AnalysisOutputs?.Uncertainty?.CiUpper ?? 0,
                    StabilityIndex = response.AnalysisOutputs?.Uncertainty?.StabilityIndex ?? 0,
                    MarginP10Db = response.AnalysisOutputs?.Uncertainty?.MarginP10Db ?? 0,
                    MarginP50Db = response.AnalysisOutputs?.Uncertainty?.MarginP50Db ?? 0,
                    MarginP90Db = response.AnalysisOutputs?.Uncertainty?.MarginP90Db ?? 0,
                    SensitivitySummary = response.AnalysisOutputs?.Uncertainty?.SensitivitySummary ?? string.Empty,
                },
                Calibration = new PropagationCalibrationOutput
                {
                    TrainingSampleCount = response.AnalysisOutputs?.Calibration?.TrainingSampleCount ?? 0,
                    ValidationSampleCount = response.AnalysisOutputs?.Calibration?.ValidationSampleCount ?? 0,
                    MaeBefore = response.AnalysisOutputs?.Calibration?.MaeBefore ?? 0,
                    MaeAfter = response.AnalysisOutputs?.Calibration?.MaeAfter ?? 0,
                    MaeDelta = response.AnalysisOutputs?.Calibration?.MaeDelta ?? 0,
                    RmseBefore = response.AnalysisOutputs?.Calibration?.RmseBefore ?? 0,
                    RmseAfter = response.AnalysisOutputs?.Calibration?.RmseAfter ?? 0,
                    RmseDelta = response.AnalysisOutputs?.Calibration?.RmseDelta ?? 0,
                    ValidationMaeAfter = response.AnalysisOutputs?.Calibration?.ValidationMaeAfter ?? 0,
                    ValidationRmseAfter = response.AnalysisOutputs?.Calibration?.ValidationRmseAfter ?? 0,
                    ParameterAdjustments = response.AnalysisOutputs?.Calibration?.ParameterAdjustments?.Select(adjustment => new PropagationParameterAdjustment
                    {
                        Name = adjustment.Name,
                        Before = adjustment.Before,
                        After = adjustment.After,
                        Unit = adjustment.Unit,
                    }).ToList() ?? new List<PropagationParameterAdjustment>(),
                    CalibrationRunId = response.AnalysisOutputs?.Calibration?.CalibrationRunId ?? string.Empty,
                },
                SpatialAlignment = new PropagationSpatialAlignmentOutput
                {
                    TargetCrs = response.AnalysisOutputs?.SpatialAlignment?.TargetCrs ?? string.Empty,
                    DemResamplingMethod = response.AnalysisOutputs?.SpatialAlignment?.DemResamplingMethod ?? string.Empty,
                    LandcoverResamplingMethod = response.AnalysisOutputs?.SpatialAlignment?.LandcoverResamplingMethod ?? string.Empty,
                    DemResolutionM = response.AnalysisOutputs?.SpatialAlignment?.DemResolutionM ?? 0,
                    LandcoverResolutionM = response.AnalysisOutputs?.SpatialAlignment?.LandcoverResolutionM ?? 0,
                    HorizontalOffsetM = response.AnalysisOutputs?.SpatialAlignment?.HorizontalOffsetM ?? 0,
                    VerticalOffsetM = response.AnalysisOutputs?.SpatialAlignment?.VerticalOffsetM ?? 0,
                    AlignmentScore = response.AnalysisOutputs?.SpatialAlignment?.AlignmentScore ?? 0,
                    Status = response.AnalysisOutputs?.SpatialAlignment?.Status ?? string.Empty,
                },
            },
            Provenance = new PropagationProvenance
            {
                DatasetBundle = new PropagationDatasetBundle
                {
                    DemVersion = response.Provenance?.DatasetBundle?.DemVersion ?? string.Empty,
                    LandcoverVersion = response.Provenance?.DatasetBundle?.LandcoverVersion ?? string.Empty,
                    SurfaceVersion = response.Provenance?.DatasetBundle?.SurfaceVersion ?? string.Empty,
                },
                ModelVersion = response.Provenance?.ModelVersion ?? string.Empty,
                GitCommit = response.Provenance?.GitCommit ?? string.Empty,
                ParameterHash = response.Provenance?.ParameterHash ?? string.Empty,
            },
            QualityFlags = new PropagationQualityFlags
            {
                AssumptionFlags = response.QualityFlags?.AssumptionFlags?.ToList() ?? new List<string>(),
                ValidityWarnings = response.QualityFlags?.ValidityWarnings?.ToList() ?? new List<string>(),
            },
            SceneGeometry = new PropagationSceneGeometry
            {
                RelayCandidates = response.SceneGeometry?.RelayCandidates?.Select(MapScenePoint).ToList() ?? new List<PropagationScenePoint>(),
                RelayRecommendations = response.SceneGeometry?.RelayRecommendations?.Select(MapScenePoint).ToList() ?? new List<PropagationScenePoint>(),
                ProfileObstacles = response.SceneGeometry?.ProfileObstacles?.Select(MapScenePoint).ToList() ?? new List<PropagationScenePoint>(),
                ProfileLines = response.SceneGeometry?.ProfileLines?.Select(line => new PropagationScenePolyline
                {
                    Id = line.Id,
                    Points = line.Points.Select(point => new PropagationScenePolylinePoint
                    {
                        X = point.X,
                        Z = point.Z,
                        Y = point.HasY ? point.Y : null,
                    }).ToList()
                }).ToList() ?? new List<PropagationScenePolyline>(),
                RidgeLines = response.SceneGeometry?.RidgeLines?.Select(line => new PropagationScenePolyline
                {
                    Id = line.Id,
                    Points = line.Points.Select(point => new PropagationScenePolylinePoint
                    {
                        X = point.X,
                        Z = point.Z,
                        Y = point.HasY ? point.Y : null,
                    }).ToList()
                }).ToList() ?? new List<PropagationScenePolyline>(),
            },
        };
    }

    private static PropagationScenePoint MapScenePoint(ScenePoint point)
    {
        return new PropagationScenePoint
        {
            Id = point.Id,
            Label = point.Label,
            X = point.X,
            Z = point.Z,
            Y = point.HasY ? point.Y : null,
            Score = point.HasScore ? point.Score : null,
        };
    }

    private static PropagationLandcoverClass MapLandcoverClass(LandcoverClass landcoverClass)
    {
        return landcoverClass switch
        {
            LandcoverClass.SparseForest => PropagationLandcoverClass.SparseForest,
            LandcoverClass.DenseForest => PropagationLandcoverClass.DenseForest,
            LandcoverClass.Water => PropagationLandcoverClass.Water,
            _ => PropagationLandcoverClass.BareGround,
        };
    }

    private static PropagationMode MapMode(PropagationSimulationMode mode)
    {
        return mode switch
        {
            PropagationSimulationMode.CoverageMap => PropagationMode.ModeCoverageMap,
            PropagationSimulationMode.InterferenceAnalysis => PropagationMode.ModeInterferenceAnalysis,
            PropagationSimulationMode.RelayOptimization => PropagationMode.ModeRelayOptimization,
            PropagationSimulationMode.AdvancedModeling => PropagationMode.ModeAdvancedModeling,
            _ => PropagationMode.ModeCoverageMap,
        };
    }

    private static PropagationJobState MapJobState(JobState state)
    {
        return state switch
        {
            JobState.Queued => PropagationJobState.Queued,
            JobState.Running => PropagationJobState.Running,
            JobState.Paused => PropagationJobState.Paused,
            JobState.Completed => PropagationJobState.Completed,
            JobState.Failed => PropagationJobState.Failed,
            JobState.Canceled => PropagationJobState.Canceled,
            _ => PropagationJobState.Failed,
        };
    }
}
