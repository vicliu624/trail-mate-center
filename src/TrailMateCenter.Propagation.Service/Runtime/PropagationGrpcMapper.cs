using Google.Protobuf.WellKnownTypes;
using TrailMateCenter.Propagation.Grpc;
using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Service.Runtime;

internal static class PropagationGrpcMapper
{
    public static PropagationSimulationRequest ToContract(StartSimulationRequest request)
    {
        return new PropagationSimulationRequest
        {
            RequestId = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId,
            Mode = request.Mode switch
            {
                PropagationMode.ModeCoverageMap => PropagationSimulationMode.CoverageMap,
                PropagationMode.ModeInterferenceAnalysis => PropagationSimulationMode.InterferenceAnalysis,
                PropagationMode.ModeRelayOptimization => PropagationSimulationMode.RelayOptimization,
                PropagationMode.ModeAdvancedModeling => PropagationSimulationMode.AdvancedModeling,
                _ => PropagationSimulationMode.CoverageMap,
            },
            SourceRunId = string.IsNullOrWhiteSpace(request.SourceRunId) ? null : request.SourceRunId,
            OptimizationAlgorithm = request.OptimizationAlgorithm,
            ScenarioPresetName = request.ScenarioPresetName,
            FrequencyMHz = request.RadioProfile?.FrequencyMhz ?? 915,
            TxPowerDbm = request.RadioProfile?.TxPowerDbm ?? 20,
            UplinkSpreadingFactor = request.RadioProfile?.UplinkSf ?? "SF10",
            DownlinkSpreadingFactor = request.RadioProfile?.DownlinkSf ?? "SF10",
            EnvironmentLossDb = request.ModelProfile?.EnvironmentLossDb ?? 6,
            VegetationAlphaSparse = request.ModelProfile?.VegAlphaSparse ?? 0.3,
            VegetationAlphaDense = request.ModelProfile?.VegAlphaDense ?? 0.8,
            ShadowSigmaDb = request.ModelProfile?.ShadowSigmaDb ?? 8,
            ReflectionCoeff = request.ModelProfile?.ReflectionCoeff ?? 0.2,
            EnableMonteCarlo = request.ModelProfile?.EnableMonteCarlo ?? false,
            MonteCarloIterations = request.ModelProfile?.MonteCarloIterations ?? 120,
            DemVersion = request.DatasetSelector?.DemVersion ?? "dem_reference_v1",
            LandcoverVersion = request.DatasetSelector?.LandcoverVersion ?? "landcover_reference_v1",
            SurfaceVersion = request.DatasetSelector?.SurfaceVersion ?? "surface_reference_v1",
        };
    }

    public static SimulationUpdateEvent ToGrpc(PropagationSimulationUpdate update)
    {
        return new SimulationUpdateEvent
        {
            RunId = update.RunId,
            State = ToGrpcState(update.State),
            ProgressPct = update.ProgressPercent,
            Stage = update.Stage,
            CacheHit = update.CacheHit,
            Message = update.Message,
            TimestampUtc = Timestamp.FromDateTimeOffset(update.TimestampUtc),
        };
    }

    public static SimulationResultResponse ToGrpc(PropagationSimulationResult result)
    {
        var response = new SimulationResultResponse
        {
            RunMeta = new RunMeta
            {
                RunId = result.RunMeta.RunId,
                Status = ToGrpcState(result.RunMeta.Status),
                StartedAtUtc = Timestamp.FromDateTimeOffset(result.RunMeta.StartedAtUtc),
                FinishedAtUtc = Timestamp.FromDateTimeOffset(result.RunMeta.FinishedAtUtc),
                DurationMs = result.RunMeta.DurationMs,
                ProgressPct = result.RunMeta.ProgressPercent,
                CacheHit = result.RunMeta.CacheHit,
            },
            InputBundle = new InputBundle
            {
                AoiId = result.InputBundle.AoiId,
                NodeSetVersion = result.InputBundle.NodeSetVersion,
                ParameterSetVersion = result.InputBundle.ParameterSetVersion,
            },
            ModelOutputs = new ModelOutputs
            {
                MeanCoverageRasterUri = result.ModelOutputs.MeanCoverageRasterUri,
                Reliability95RasterUri = result.ModelOutputs.Reliability95RasterUri,
                Reliability80RasterUri = result.ModelOutputs.Reliability80RasterUri,
                InterferenceRasterUri = result.ModelOutputs.InterferenceRasterUri,
                CapacityRasterUri = result.ModelOutputs.CapacityRasterUri,
            },
            AnalysisOutputs = new AnalysisOutputs
            {
                Link = new LinkOutput
                {
                    DownlinkRssiDbm = result.AnalysisOutputs.Link.DownlinkRssiDbm,
                    UplinkRssiDbm = result.AnalysisOutputs.Link.UplinkRssiDbm,
                    DownlinkMarginDb = result.AnalysisOutputs.Link.DownlinkMarginDb,
                    UplinkMarginDb = result.AnalysisOutputs.Link.UplinkMarginDb,
                    LinkFeasible = result.AnalysisOutputs.Link.LinkFeasible,
                    MarginGuardrail = result.AnalysisOutputs.Link.MarginGuardrail,
                },
                Terrain = new TerrainOutput
                {
                    IsLineOfSight = result.AnalysisOutputs.Terrain.IsLineOfSight,
                    PathState = result.AnalysisOutputs.Terrain.PathState,
                    DominantObstructionLabel = result.AnalysisOutputs.Terrain.DominantObstructionLabel,
                    DominantObstructionDistanceKm = result.AnalysisOutputs.Terrain.DominantObstructionDistanceKm,
                    DominantObstructionHeightM = result.AnalysisOutputs.Terrain.DominantObstructionHeightM,
                    ObstructionAboveLosM = result.AnalysisOutputs.Terrain.ObstructionAboveLosM,
                    SampleStepM = result.AnalysisOutputs.Terrain.SampleStepM,
                },
                TerrainMap = new TerrainMapOutput
                {
                    Crs = result.AnalysisOutputs.TerrainMap.Crs,
                    WidthM = result.AnalysisOutputs.TerrainMap.WidthM,
                    HeightM = result.AnalysisOutputs.TerrainMap.HeightM,
                    SampleStepM = result.AnalysisOutputs.TerrainMap.SampleStepM,
                    Columns = result.AnalysisOutputs.TerrainMap.Columns,
                    Rows = result.AnalysisOutputs.TerrainMap.Rows,
                    MinElevationM = result.AnalysisOutputs.TerrainMap.MinElevationM,
                    MaxElevationM = result.AnalysisOutputs.TerrainMap.MaxElevationM,
                },
                Reliability = new ReliabilityOutput
                {
                    P95 = result.AnalysisOutputs.Reliability.P95,
                    P80 = result.AnalysisOutputs.Reliability.P80,
                    ConfidenceNote = result.AnalysisOutputs.Reliability.ConfidenceNote,
                },
                LossBreakdown = new LossBreakdownOutput
                {
                    FsplDb = result.AnalysisOutputs.LossBreakdown.FsplDb,
                    DiffractionDb = result.AnalysisOutputs.LossBreakdown.DiffractionDb,
                    FresnelDb = result.AnalysisOutputs.LossBreakdown.FresnelDb,
                    VegetationDb = result.AnalysisOutputs.LossBreakdown.VegetationDb,
                    ReflectionDb = result.AnalysisOutputs.LossBreakdown.ReflectionDb,
                    ShadowDb = result.AnalysisOutputs.LossBreakdown.ShadowDb,
                    EnvironmentDb = result.AnalysisOutputs.LossBreakdown.EnvironmentDb,
                    TotalDb = result.AnalysisOutputs.LossBreakdown.TotalDb,
                },
                Fresnel = new FresnelOutput
                {
                    RadiusM = result.AnalysisOutputs.Fresnel.RadiusM,
                    ClearanceRatio = result.AnalysisOutputs.Fresnel.ClearanceRatio,
                    MinimumClearanceM = result.AnalysisOutputs.Fresnel.MinimumClearanceM,
                    AdditionalLossDb = result.AnalysisOutputs.Fresnel.AdditionalLossDb,
                    RiskLevel = result.AnalysisOutputs.Fresnel.RiskLevel,
                },
                CoverageProbability = new CoverageProbabilityOutput
                {
                    AreaP95Km2 = result.AnalysisOutputs.CoverageProbability.AreaP95Km2,
                    AreaP80Km2 = result.AnalysisOutputs.CoverageProbability.AreaP80Km2,
                    ThresholdRssiDbm = result.AnalysisOutputs.CoverageProbability.ThresholdRssiDbm,
                    ReliableReachKm = result.AnalysisOutputs.CoverageProbability.ReliableReachKm,
                },
                Network = new NetworkOutput
                {
                    SinrDb = result.AnalysisOutputs.Network.SinrDb,
                    ConflictRate = result.AnalysisOutputs.Network.ConflictRate,
                    MaxCapacityNodes = result.AnalysisOutputs.Network.MaxCapacityNodes,
                    AlohaLoadPercent = result.AnalysisOutputs.Network.AlohaLoadPercent,
                    ChannelOccupancyPercent = result.AnalysisOutputs.Network.ChannelOccupancyPercent,
                    AirtimeMs = result.AnalysisOutputs.Network.AirtimeMs,
                },
                Profile = new ProfileOutput
                {
                    DistanceKm = result.AnalysisOutputs.Profile.DistanceKm,
                    FresnelRadiusM = result.AnalysisOutputs.Profile.FresnelRadiusM,
                    MarginDb = result.AnalysisOutputs.Profile.MarginDb,
                    MainObstacle = new MainObstacle
                    {
                        Label = result.AnalysisOutputs.Profile.MainObstacle.Label,
                        V = result.AnalysisOutputs.Profile.MainObstacle.V,
                        LdDb = result.AnalysisOutputs.Profile.MainObstacle.LdDb,
                    },
                },
                Optimization = new OptimizationOutput
                {
                    Algorithm = result.AnalysisOutputs.Optimization.Algorithm,
                    CandidateCount = result.AnalysisOutputs.Optimization.CandidateCount,
                    ConstraintSummary = result.AnalysisOutputs.Optimization.ConstraintSummary,
                    RecommendedPlanId = result.AnalysisOutputs.Optimization.RecommendedPlanId,
                },
                Reflection = new ReflectionOutput
                {
                    Enabled = result.AnalysisOutputs.Reflection.Enabled,
                    ReflectionCoefficient = result.AnalysisOutputs.Reflection.ReflectionCoefficient,
                    RelativeGainDb = result.AnalysisOutputs.Reflection.RelativeGainDb,
                    ExcessDelayNs = result.AnalysisOutputs.Reflection.ExcessDelayNs,
                    MultipathRisk = result.AnalysisOutputs.Reflection.MultipathRisk,
                },
                Uncertainty = new UncertaintyOutput
                {
                    Iterations = result.AnalysisOutputs.Uncertainty.Iterations,
                    CiLower = result.AnalysisOutputs.Uncertainty.CiLower,
                    CiUpper = result.AnalysisOutputs.Uncertainty.CiUpper,
                    StabilityIndex = result.AnalysisOutputs.Uncertainty.StabilityIndex,
                    MarginP10Db = result.AnalysisOutputs.Uncertainty.MarginP10Db,
                    MarginP50Db = result.AnalysisOutputs.Uncertainty.MarginP50Db,
                    MarginP90Db = result.AnalysisOutputs.Uncertainty.MarginP90Db,
                    SensitivitySummary = result.AnalysisOutputs.Uncertainty.SensitivitySummary,
                },
                Calibration = new CalibrationOutput
                {
                    TrainingSampleCount = result.AnalysisOutputs.Calibration.TrainingSampleCount,
                    ValidationSampleCount = result.AnalysisOutputs.Calibration.ValidationSampleCount,
                    MaeBefore = result.AnalysisOutputs.Calibration.MaeBefore,
                    MaeAfter = result.AnalysisOutputs.Calibration.MaeAfter,
                    MaeDelta = result.AnalysisOutputs.Calibration.MaeDelta,
                    RmseBefore = result.AnalysisOutputs.Calibration.RmseBefore,
                    RmseAfter = result.AnalysisOutputs.Calibration.RmseAfter,
                    RmseDelta = result.AnalysisOutputs.Calibration.RmseDelta,
                    ValidationMaeAfter = result.AnalysisOutputs.Calibration.ValidationMaeAfter,
                    ValidationRmseAfter = result.AnalysisOutputs.Calibration.ValidationRmseAfter,
                    CalibrationRunId = result.AnalysisOutputs.Calibration.CalibrationRunId,
                },
                SpatialAlignment = new SpatialAlignmentOutput
                {
                    TargetCrs = result.AnalysisOutputs.SpatialAlignment.TargetCrs,
                    DemResamplingMethod = result.AnalysisOutputs.SpatialAlignment.DemResamplingMethod,
                    LandcoverResamplingMethod = result.AnalysisOutputs.SpatialAlignment.LandcoverResamplingMethod,
                    DemResolutionM = result.AnalysisOutputs.SpatialAlignment.DemResolutionM,
                    LandcoverResolutionM = result.AnalysisOutputs.SpatialAlignment.LandcoverResolutionM,
                    HorizontalOffsetM = result.AnalysisOutputs.SpatialAlignment.HorizontalOffsetM,
                    VerticalOffsetM = result.AnalysisOutputs.SpatialAlignment.VerticalOffsetM,
                    AlignmentScore = result.AnalysisOutputs.SpatialAlignment.AlignmentScore,
                    Status = result.AnalysisOutputs.SpatialAlignment.Status,
                },
            },
            Provenance = new Provenance
            {
                DatasetBundle = new DatasetBundle
                {
                    DemVersion = result.Provenance.DatasetBundle.DemVersion,
                    LandcoverVersion = result.Provenance.DatasetBundle.LandcoverVersion,
                    SurfaceVersion = result.Provenance.DatasetBundle.SurfaceVersion,
                },
                ModelVersion = result.Provenance.ModelVersion,
                GitCommit = result.Provenance.GitCommit,
                ParameterHash = result.Provenance.ParameterHash,
            },
            QualityFlags = new QualityFlags(),
            SceneGeometry = new SceneGeometry(),
        };

        foreach (var layer in result.ModelOutputs.RasterLayers)
        {
            var grpcLayer = new RasterLayerMetadata
            {
                LayerId = layer.LayerId,
                RasterUri = layer.RasterUri,
                TileTemplateUri = layer.TileTemplateUri,
                MinZoom = layer.MinZoom ?? 0,
                MaxZoom = layer.MaxZoom ?? 0,
                TileSize = layer.TileSize ?? 0,
                HasTileSchema = layer.MinZoom.HasValue && layer.MaxZoom.HasValue && layer.TileSize.HasValue,
                Bounds = new RasterBounds { MinX = layer.Bounds.MinX, MinZ = layer.Bounds.MinZ, MaxX = layer.Bounds.MaxX, MaxZ = layer.Bounds.MaxZ },
                Crs = layer.Crs,
                MinValue = layer.MinValue ?? 0,
                MaxValue = layer.MaxValue ?? 0,
                HasMinMax = layer.MinValue.HasValue && layer.MaxValue.HasValue,
                NoDataValue = layer.NoDataValue ?? 0,
                HasNoData = layer.NoDataValue.HasValue,
                ValueScale = layer.ValueScale ?? 0,
                ValueOffset = layer.ValueOffset ?? 0,
                HasValueTransform = layer.ValueScale.HasValue || layer.ValueOffset.HasValue,
                Unit = layer.Unit,
                Palette = layer.Palette,
            };
            grpcLayer.ClassBreaks.AddRange(layer.ClassBreaks);
            response.ModelOutputs.RasterLayers.Add(grpcLayer);
        }

        response.AnalysisOutputs.Profile.Samples.AddRange(result.AnalysisOutputs.Profile.Samples.Select(sample => new ProfileSample
        {
            Index = sample.Index,
            DistanceKm = sample.DistanceKm,
            TerrainElevationM = sample.TerrainElevationM,
            LosHeightM = sample.LosHeightM,
            FresnelRadiusM = sample.FresnelRadiusM,
            IsBlocked = sample.IsBlocked,
            SurfaceType = sample.SurfaceType,
        }));
        response.AnalysisOutputs.TerrainMap.ElevationSamples.AddRange(result.AnalysisOutputs.TerrainMap.ElevationSamples);
        response.AnalysisOutputs.TerrainMap.LandcoverSamples.AddRange(result.AnalysisOutputs.TerrainMap.LandcoverSamples.Select(ToGrpcLandcover));
        response.AnalysisOutputs.TerrainMap.ContourLines.AddRange(result.AnalysisOutputs.TerrainMap.ContourLines.Select(ToSceneLine));
        response.AnalysisOutputs.TerrainMap.Sites.AddRange(result.AnalysisOutputs.TerrainMap.Sites.Select(ToScenePoint));
        response.AnalysisOutputs.Optimization.TopPlans.AddRange(result.AnalysisOutputs.Optimization.TopPlans.Select(plan =>
        {
            var grpcPlan = new RelayPlan
            {
                PlanId = plan.PlanId,
                Score = plan.Score,
                CoverageGain = plan.CoverageGain,
                ReliabilityGain = plan.ReliabilityGain,
                BlindAreaPenalty = plan.BlindAreaPenalty,
                InterferencePenalty = plan.InterferencePenalty,
                CostPenalty = plan.CostPenalty,
                Explanation = plan.Explanation,
            };
            grpcPlan.SiteIds.AddRange(plan.SiteIds);
            return grpcPlan;
        }));
        response.AnalysisOutputs.Calibration.ParameterAdjustments.AddRange(result.AnalysisOutputs.Calibration.ParameterAdjustments.Select(adjustment => new ParameterAdjustment
        {
            Name = adjustment.Name,
            Before = adjustment.Before,
            After = adjustment.After,
            Unit = adjustment.Unit,
        }));
        response.QualityFlags.AssumptionFlags.AddRange(result.QualityFlags.AssumptionFlags);
        response.QualityFlags.ValidityWarnings.AddRange(result.QualityFlags.ValidityWarnings);
        response.SceneGeometry.RelayCandidates.AddRange(result.SceneGeometry.RelayCandidates.Select(ToScenePoint));
        response.SceneGeometry.RelayRecommendations.AddRange(result.SceneGeometry.RelayRecommendations.Select(ToScenePoint));
        response.SceneGeometry.ProfileObstacles.AddRange(result.SceneGeometry.ProfileObstacles.Select(ToScenePoint));
        response.SceneGeometry.ProfileLines.AddRange(result.SceneGeometry.ProfileLines.Select(ToSceneLine));
        response.SceneGeometry.RidgeLines.AddRange(result.SceneGeometry.RidgeLines.Select(ToSceneLine));

        return response;
    }

    private static JobState ToGrpcState(PropagationJobState state)
    {
        return state switch
        {
            PropagationJobState.Queued => JobState.Queued,
            PropagationJobState.Running => JobState.Running,
            PropagationJobState.Paused => JobState.Paused,
            PropagationJobState.Completed => JobState.Completed,
            PropagationJobState.Failed => JobState.Failed,
            PropagationJobState.Canceled => JobState.Canceled,
            _ => JobState.Unspecified,
        };
    }

    private static ScenePoint ToScenePoint(PropagationScenePoint point)
    {
        return new ScenePoint
        {
            Id = point.Id,
            Label = point.Label,
            X = point.X,
            Z = point.Z,
            HasY = point.Y.HasValue,
            Y = point.Y ?? 0,
            HasScore = point.Score.HasValue,
            Score = point.Score ?? 0,
        };
    }

    private static ScenePolyline ToSceneLine(PropagationScenePolyline line)
    {
        var grpc = new ScenePolyline { Id = line.Id };
        grpc.Points.AddRange(line.Points.Select(point => new ScenePolylinePoint
        {
            X = point.X,
            Z = point.Z,
            HasY = point.Y.HasValue,
            Y = point.Y ?? 0,
        }));
        return grpc;
    }

    private static LandcoverClass ToGrpcLandcover(PropagationLandcoverClass landcoverClass)
    {
        return landcoverClass switch
        {
            PropagationLandcoverClass.SparseForest => LandcoverClass.SparseForest,
            PropagationLandcoverClass.DenseForest => LandcoverClass.DenseForest,
            PropagationLandcoverClass.Water => LandcoverClass.Water,
            _ => LandcoverClass.BareGround,
        };
    }
}
