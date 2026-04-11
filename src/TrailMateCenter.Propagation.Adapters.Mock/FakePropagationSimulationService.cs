using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrailMateCenter.Services;

public sealed class FakePropagationSimulationService : IPropagationSimulationService
{
    private const string ModelVersion = "prop_core_0.4.0-dev";
    private readonly ConcurrentDictionary<string, RunContext> _runs = new(StringComparer.Ordinal);

    public Task<PropagationRunHandle> StartSimulationAsync(PropagationSimulationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runId = $"sim_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Random.Shared.Next(1000, 9999)}";
        var ctx = new RunContext
        {
            Request = request,
            StartedAtUtc = DateTimeOffset.UtcNow,
            State = PropagationJobState.Queued,
            ProgressPercent = 0,
            CacheHit = ComputeCacheHint(request),
        };
        _runs[runId] = ctx;
        return Task.FromResult(new PropagationRunHandle
        {
            RunId = runId,
            InitialState = PropagationJobState.Queued,
        });
    }

    public async IAsyncEnumerable<PropagationSimulationUpdate> StreamSimulationUpdatesAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var ctx))
            throw new InvalidOperationException($"Run not found: {runId}");

        yield return BuildUpdate(runId, ctx, PropagationJobState.Queued, 0, "queued", "Task queued");
        await Task.Delay(120, cancellationToken);

        var stagePrefix = ctx.Request.Mode switch
        {
            PropagationSimulationMode.CoverageMap => "coverage",
            PropagationSimulationMode.InterferenceAnalysis => "interference",
            PropagationSimulationMode.RelayOptimization => "relay",
            PropagationSimulationMode.AdvancedModeling => "advanced",
            _ => "coverage",
        };

        for (var step = 1; step <= 20; step++)
        {
            var canceled = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (ctx.SyncRoot)
                {
                    canceled = ctx.IsCanceled;
                    if (!ctx.IsPaused)
                        break;

                    ctx.State = PropagationJobState.Paused;
                }

                yield return BuildUpdate(runId, ctx, PropagationJobState.Paused, ctx.ProgressPercent, "paused", "Paused by user");
                await Task.Delay(200, cancellationToken);
            }

            if (canceled)
            {
                lock (ctx.SyncRoot)
                {
                    ctx.State = PropagationJobState.Canceled;
                    ctx.ProgressPercent = Math.Min(ctx.ProgressPercent, 99);
                }

                yield return BuildUpdate(runId, ctx, PropagationJobState.Canceled, ctx.ProgressPercent, "canceled", "Canceled");
                yield break;
            }

            await Task.Delay(120, cancellationToken);
            var progress = step * 5d;
            var stage = ResolveStage(stagePrefix, progress);
            lock (ctx.SyncRoot)
            {
                ctx.State = PropagationJobState.Running;
                ctx.ProgressPercent = progress;
            }

            yield return BuildUpdate(runId, ctx, PropagationJobState.Running, progress, stage, "Running");
        }

        var result = BuildResult(runId, ctx);
        lock (ctx.SyncRoot)
        {
            ctx.Result = result;
            ctx.State = PropagationJobState.Completed;
            ctx.ProgressPercent = 100;
        }

        yield return BuildUpdate(runId, ctx, PropagationJobState.Completed, 100, "completed", "Completed");
    }

    public Task<PropagationSimulationResult> GetSimulationResultAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_runs.TryGetValue(runId, out var ctx))
            throw new InvalidOperationException($"Run not found: {runId}");
        lock (ctx.SyncRoot)
        {
            if (ctx.Result is null)
                throw new InvalidOperationException("Result is not ready yet.");
            return Task.FromResult(ctx.Result);
        }
    }

    public Task PauseSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = GetRunOrThrow(runId);
        lock (ctx.SyncRoot)
        {
            if (ctx.State is PropagationJobState.Completed or PropagationJobState.Canceled or PropagationJobState.Failed)
                return Task.CompletedTask;
            ctx.IsPaused = true;
            ctx.State = PropagationJobState.Paused;
        }
        return Task.CompletedTask;
    }

    public Task ResumeSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = GetRunOrThrow(runId);
        lock (ctx.SyncRoot)
        {
            if (ctx.State is PropagationJobState.Completed or PropagationJobState.Canceled or PropagationJobState.Failed)
                return Task.CompletedTask;
            ctx.IsPaused = false;
            if (ctx.State == PropagationJobState.Paused)
                ctx.State = PropagationJobState.Running;
        }
        return Task.CompletedTask;
    }

    public Task CancelSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = GetRunOrThrow(runId);
        lock (ctx.SyncRoot)
        {
            ctx.IsCanceled = true;
            if (ctx.State is not (PropagationJobState.Completed or PropagationJobState.Failed))
                ctx.State = PropagationJobState.Canceled;
        }
        return Task.CompletedTask;
    }

    public Task<PropagationExportResult> ExportResultAsync(string runId, string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ctx = GetRunOrThrow(runId);
        lock (ctx.SyncRoot)
        {
            if (ctx.Result is null)
                throw new InvalidOperationException("Result is not ready yet.");
        }

        var safeDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? "Documents/TrailMateCenter/exports"
            : outputDirectory.Trim();
        return Task.FromResult(new PropagationExportResult
        {
            RunId = runId,
            ExportPath = $"{safeDir.TrimEnd('/', '\\')}\\{runId}.zip",
            ExportedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private RunContext GetRunOrThrow(string runId)
    {
        if (_runs.TryGetValue(runId, out var ctx))
            return ctx;
        throw new InvalidOperationException($"Run not found: {runId}");
    }

    private static PropagationSimulationUpdate BuildUpdate(
        string runId,
        RunContext ctx,
        PropagationJobState state,
        double progress,
        string stage,
        string message)
    {
        return new PropagationSimulationUpdate
        {
            RunId = runId,
            State = state,
            ProgressPercent = progress,
            Stage = stage,
            CacheHit = ctx.CacheHit,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string ResolveStage(string stagePrefix, double progressPercent)
    {
        if (progressPercent <= 20)
            return $"{stagePrefix}_terrain_sampling";
        if (progressPercent <= 40)
            return $"{stagePrefix}_los_and_pathloss";
        if (progressPercent <= 60)
            return $"{stagePrefix}_diffraction_and_fresnel";
        if (progressPercent <= 80)
            return $"{stagePrefix}_probability_network";
        return $"{stagePrefix}_finalize";
    }

    private static bool ComputeCacheHint(PropagationSimulationRequest request)
    {
        var hash = ComputeParameterHash(request);
        if (hash.Length < 2)
            return false;
        return (Convert.ToInt32(hash[..2], 16) % 2) == 0;
    }

    private static PropagationSimulationResult BuildResult(string runId, RunContext ctx)
    {
        var request = ctx.Request;
        var seed = Math.Abs(HashCode.Combine(
            runId,
            request.FrequencyMHz,
            request.TxPowerDbm,
            request.ShadowSigmaDb,
            (int)request.Mode));
        var random = new Random(seed);

        var distanceKm = 4.2 + random.NextDouble() * 1.4;
        var profile = BuildProfileSynthesis(distanceKm, request.FrequencyMHz, request, random);
        var sf = ParseSf(request.UplinkSpreadingFactor);
        var sensitivityDbm = SfSensitivityDbm(sf);
        var fsplDb = 32.44 + 20 * Math.Log10(Math.Max(1, request.FrequencyMHz)) + 20 * Math.Log10(distanceKm);
        var diffractionDb = profile.DiffractionLossDb;
        var fresnelDb = profile.FresnelAdditionalLossDb;
        var vegetationDb = profile.VegetationLossDb;
        var reflectionRelativeGainDb = ComputeReflectionRelativeGainDb(request.ReflectionCoeff, profile.DistanceKm, random);
        var reflectionDb = Math.Max(0, -reflectionRelativeGainDb);
        var shadowDb = Math.Max(0, request.ShadowSigmaDb) * (0.7 + random.NextDouble() * 0.35);
        var totalLoss = fsplDb + diffractionDb + fresnelDb + vegetationDb + reflectionDb + shadowDb + request.EnvironmentLossDb;

        var modeOffset = request.Mode switch
        {
            PropagationSimulationMode.CoverageMap => 0,
            PropagationSimulationMode.InterferenceAnalysis => -2.5,
            PropagationSimulationMode.RelayOptimization => 3.2,
            PropagationSimulationMode.AdvancedModeling => 0.5,
            _ => 0,
        };

        var downlinkRssiDbm = request.TxPowerDbm - totalLoss + 8 + modeOffset;
        var uplinkRssiDbm = downlinkRssiDbm - 3.5;
        var downlinkMarginDb = downlinkRssiDbm - sensitivityDbm;
        var uplinkMarginDb = uplinkRssiDbm - sensitivityDbm;
        var linkFeasible = downlinkMarginDb >= 0 && uplinkMarginDb >= 0;

        var reliability95 = Clamp01To100(35 + downlinkMarginDb * 2.1 - request.ShadowSigmaDb * 1.35);
        var reliability80 = Clamp01To100(reliability95 + 7.5);

        var baseSinr = 11 + downlinkMarginDb * 0.25 - request.ShadowSigmaDb * 0.15 + random.NextDouble() * 1.2;
        var sinrDb = request.Mode == PropagationSimulationMode.InterferenceAnalysis ? baseSinr - 2.8 : baseSinr;
        var conflictRate = Clamp01To100(26 - sinrDb * 1.1 + random.NextDouble() * 2.5);
        var maxCapacityNodes = Math.Max(20, (int)Math.Round(220 - conflictRate * 2.9 + reliability80 * 0.45));
        var alohaLoadPercent = Clamp01To100(42 + conflictRate * 0.65 - reliability80 * 0.18);
        var channelOccupancyPercent = Clamp01To100(alohaLoadPercent * 0.82 + random.NextDouble() * 8);
        var airtimeMs = 180 + sf * 22 + random.NextDouble() * 35;

        var profileMarginDb = Math.Min(downlinkMarginDb, uplinkMarginDb);

        var areaP95 = Math.Max(0.5, reliability95 * 0.12);
        var areaP80 = Math.Max(areaP95, reliability80 * 0.16);
        var reliableReachKm = Math.Max(0.4, distanceKm * (0.55 + reliability95 / 180d));

        var duration = DateTimeOffset.UtcNow - ctx.StartedAtUtc;
        var parameterHash = ComputeParameterHash(request);
        var validityWarnings = BuildValidityWarnings(request, downlinkMarginDb, uplinkMarginDb).ToArray();
        var assumptionFlags = BuildAssumptionFlags(request).ToArray();
        var optimization = BuildOptimizationOutput(random, request, profile.RidgeCandidateCount);
        var calibration = BuildCalibrationOutput(request);
        var uncertainty = BuildUncertaintyOutput(request, reliability95, profileMarginDb, shadowDb);
        var alignment = BuildSpatialAlignmentOutput();
        var terrainMap = BuildTerrainMapOutput();

        return new PropagationSimulationResult
        {
            RunMeta = new PropagationRunMeta
            {
                RunId = runId,
                Status = PropagationJobState.Completed,
                StartedAtUtc = ctx.StartedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                DurationMs = Math.Max(1, (long)duration.TotalMilliseconds),
                ProgressPercent = 100,
                CacheHit = ctx.CacheHit,
            },
            InputBundle = new PropagationInputBundle
            {
                AoiId = "aoi_mountain_demo",
                NodeSetVersion = "nodeset_20260226_a",
                ParameterSetVersion = $"paramset_{DateTimeOffset.UtcNow:yyyyMMdd}",
            },
            ModelOutputs = new PropagationModelOutputs
            {
                MeanCoverageRasterUri = $"outputs/{runId}/coverage_mean.tif",
                Reliability95RasterUri = $"outputs/{runId}/reliability_95.tif",
                Reliability80RasterUri = $"outputs/{runId}/reliability_80.tif",
                InterferenceRasterUri = $"outputs/{runId}/interference.tif",
                CapacityRasterUri = $"outputs/{runId}/capacity.tif",
                RasterLayers =
                [
                    new PropagationRasterLayerMetadata
                    {
                        LayerId = "coverage_mean",
                        RasterUri = $"outputs/{runId}/coverage_mean.tif",
                        TileTemplateUri = $"outputs/{runId}/tiles/coverage_mean/{{z}}/{{x}}/{{y}}.png",
                        MinZoom = 0,
                        MaxZoom = 6,
                        TileSize = 256,
                        Bounds = BuildTerrainBounds(terrainMap),
                        Crs = terrainMap.Crs,
                        MinValue = -130,
                        MaxValue = -70,
                        NoDataValue = null,
                        ValueScale = 60,
                        ValueOffset = -130,
                        ClassBreaks = [-120, -110, -100, -90, -80],
                        Unit = "dBm",
                        Palette = "default",
                    },
                    new PropagationRasterLayerMetadata
                    {
                        LayerId = "reliability_95",
                        RasterUri = $"outputs/{runId}/reliability_95.tif",
                        TileTemplateUri = $"outputs/{runId}/tiles/reliability_95/{{z}}/{{x}}/{{y}}.png",
                        MinZoom = 0,
                        MaxZoom = 6,
                        TileSize = 256,
                        Bounds = BuildTerrainBounds(terrainMap),
                        Crs = terrainMap.Crs,
                        MinValue = 0,
                        MaxValue = 1,
                        ValueScale = 1,
                        ValueOffset = 0,
                        ClassBreaks = [0.5, 0.7, 0.8, 0.9, 0.95],
                        Unit = "probability",
                        Palette = "reliability",
                    },
                    new PropagationRasterLayerMetadata
                    {
                        LayerId = "reliability_80",
                        RasterUri = $"outputs/{runId}/reliability_80.tif",
                        TileTemplateUri = $"outputs/{runId}/tiles/reliability_80/{{z}}/{{x}}/{{y}}.png",
                        MinZoom = 0,
                        MaxZoom = 6,
                        TileSize = 256,
                        Bounds = BuildTerrainBounds(terrainMap),
                        Crs = terrainMap.Crs,
                        MinValue = 0,
                        MaxValue = 1,
                        ValueScale = 1,
                        ValueOffset = 0,
                        ClassBreaks = [0.5, 0.65, 0.75, 0.85, 0.95],
                        Unit = "probability",
                        Palette = "reliability",
                    },
                    new PropagationRasterLayerMetadata
                    {
                        LayerId = "interference",
                        RasterUri = $"outputs/{runId}/interference.tif",
                        TileTemplateUri = $"outputs/{runId}/tiles/interference/{{z}}/{{x}}/{{y}}.png",
                        MinZoom = 0,
                        MaxZoom = 6,
                        TileSize = 256,
                        Bounds = BuildTerrainBounds(terrainMap),
                        Crs = terrainMap.Crs,
                        MinValue = -20,
                        MaxValue = 20,
                        ValueScale = 40,
                        ValueOffset = -20,
                        ClassBreaks = [-12, -6, 0, 6, 12],
                        Unit = "dB",
                        Palette = "interference",
                    },
                    new PropagationRasterLayerMetadata
                    {
                        LayerId = "capacity",
                        RasterUri = $"outputs/{runId}/capacity.tif",
                        TileTemplateUri = $"outputs/{runId}/tiles/capacity/{{z}}/{{x}}/{{y}}.png",
                        MinZoom = 0,
                        MaxZoom = 6,
                        TileSize = 256,
                        Bounds = BuildTerrainBounds(terrainMap),
                        Crs = terrainMap.Crs,
                        MinValue = 0,
                        MaxValue = 300,
                        ValueScale = 300,
                        ValueOffset = 0,
                        ClassBreaks = [40, 80, 120, 180, 240],
                        Unit = "nodes",
                        Palette = "capacity",
                    }
                ],
            },
            AnalysisOutputs = new PropagationAnalysisOutputs
            {
                Link = new PropagationLinkOutput
                {
                    DownlinkRssiDbm = downlinkRssiDbm,
                    UplinkRssiDbm = uplinkRssiDbm,
                    DownlinkMarginDb = downlinkMarginDb,
                    UplinkMarginDb = uplinkMarginDb,
                    LinkFeasible = linkFeasible,
                    MarginGuardrail = linkFeasible ? "stable" : "edge_or_unreachable",
                },
                Terrain = new PropagationTerrainOutput
                {
                    IsLineOfSight = profile.IsLineOfSight,
                    PathState = profile.IsLineOfSight ? "LOS" : "NLOS",
                    DominantObstructionLabel = profile.MainObstacle.Label,
                    DominantObstructionDistanceKm = profile.MainObstacleDistanceKm,
                    DominantObstructionHeightM = profile.MainObstacleHeightM,
                    ObstructionAboveLosM = profile.ObstructionAboveLosM,
                    SampleStepM = profile.SampleStepM,
                },
                TerrainMap = terrainMap,
                Reliability = new PropagationReliabilityOutput
                {
                    P95 = reliability95,
                    P80 = reliability80,
                    ConfidenceNote = request.EnableMonteCarlo ? "Monte Carlo enabled" : "Log-normal approximation",
                },
                LossBreakdown = new PropagationLossBreakdownOutput
                {
                    FsplDb = fsplDb,
                    DiffractionDb = diffractionDb,
                    FresnelDb = fresnelDb,
                    VegetationDb = vegetationDb,
                    ReflectionDb = reflectionDb,
                    ShadowDb = shadowDb,
                    EnvironmentDb = request.EnvironmentLossDb,
                    TotalDb = totalLoss,
                },
                Fresnel = new PropagationFresnelOutput
                {
                    RadiusM = profile.FresnelRadiusM,
                    ClearanceRatio = profile.FresnelClearanceRatio,
                    MinimumClearanceM = profile.MinimumClearanceM,
                    AdditionalLossDb = fresnelDb,
                    RiskLevel = ResolveFresnelRisk(profile.FresnelClearanceRatio),
                },
                CoverageProbability = new PropagationCoverageProbabilityOutput
                {
                    AreaP95Km2 = areaP95,
                    AreaP80Km2 = areaP80,
                    ThresholdRssiDbm = sensitivityDbm,
                    ReliableReachKm = reliableReachKm,
                },
                Network = new PropagationNetworkOutput
                {
                    SinrDb = sinrDb,
                    ConflictRate = conflictRate,
                    MaxCapacityNodes = maxCapacityNodes,
                    AlohaLoadPercent = alohaLoadPercent,
                    ChannelOccupancyPercent = channelOccupancyPercent,
                    AirtimeMs = airtimeMs,
                },
                Profile = new PropagationProfileOutput
                {
                    DistanceKm = distanceKm,
                    FresnelRadiusM = profile.FresnelRadiusM,
                    MarginDb = profileMarginDb,
                    MainObstacle = profile.MainObstacle,
                    Samples = profile.Samples,
                },
                Optimization = optimization,
                Reflection = new PropagationReflectionOutput
                {
                    Enabled = request.Mode == PropagationSimulationMode.AdvancedModeling || request.ReflectionCoeff > 0,
                    ReflectionCoefficient = request.ReflectionCoeff,
                    RelativeGainDb = reflectionRelativeGainDb,
                    ExcessDelayNs = profile.ReflectionExcessDelayNs,
                    MultipathRisk = ResolveMultipathRisk(reflectionRelativeGainDb, profile.ReflectionExcessDelayNs),
                },
                Uncertainty = uncertainty,
                Calibration = calibration,
                SpatialAlignment = alignment,
            },
            Provenance = new PropagationProvenance
            {
                DatasetBundle = new PropagationDatasetBundle
                {
                    DemVersion = request.DemVersion,
                    LandcoverVersion = request.LandcoverVersion,
                    SurfaceVersion = request.SurfaceVersion,
                },
                ModelVersion = ModelVersion,
                GitCommit = "local-dev",
                ParameterHash = $"sha256:{parameterHash}",
            },
            QualityFlags = new PropagationQualityFlags
            {
                AssumptionFlags = assumptionFlags,
                ValidityWarnings = validityWarnings,
            },
            SceneGeometry = new PropagationSceneGeometry
            {
                RelayCandidates = BuildRelayCandidates(random, profile.RidgeCandidateCount),
                RelayRecommendations = BuildRelayRecommendations(random),
                ProfileObstacles = BuildProfileObstacles(profile),
                ProfileLines = BuildProfileLines(profile),
                RidgeLines = BuildRidgeLines(random),
            },
        };
    }

    private static PropagationOptimizationOutput BuildOptimizationOutput(
        Random random,
        PropagationSimulationRequest request,
        int ridgeCandidateCount)
    {
        var plans = BuildRelayPlans(random, request.Mode);
        return new PropagationOptimizationOutput
        {
            Algorithm = string.IsNullOrWhiteSpace(request.OptimizationAlgorithm) ? "Greedy" : request.OptimizationAlgorithm,
            CandidateCount = ridgeCandidateCount,
            ConstraintSummary = "max_relays<=2 | ridge_spacing>=180m | avoid_steep_slope",
            RecommendedPlanId = plans.FirstOrDefault()?.PlanId ?? string.Empty,
            TopPlans = plans,
        };
    }

    private static IReadOnlyList<PropagationRelayPlan> BuildRelayPlans(Random random, PropagationSimulationMode mode)
    {
        if (mode != PropagationSimulationMode.RelayOptimization)
            return Array.Empty<PropagationRelayPlan>();

        return new[]
        {
            new PropagationRelayPlan
            {
                PlanId = "ridge_r12",
                Score = 84 + random.NextDouble() * 7,
                CoverageGain = 1.8 + random.NextDouble() * 0.4,
                ReliabilityGain = 8.0 + random.NextDouble() * 2.1,
                BlindAreaPenalty = 2.4 + random.NextDouble(),
                InterferencePenalty = 1.2 + random.NextDouble(),
                CostPenalty = 1.4 + random.NextDouble() * 0.5,
                Explanation = "Best ridge-top LOS exposure with moderate deployment cost and lowest interference penalty.",
                SiteIds = ["cand_01"],
            },
            new PropagationRelayPlan
            {
                PlanId = "spur_r03",
                Score = 77 + random.NextDouble() * 6,
                CoverageGain = 1.2 + random.NextDouble() * 0.5,
                ReliabilityGain = 5.8 + random.NextDouble() * 2.0,
                BlindAreaPenalty = 3.1 + random.NextDouble(),
                InterferencePenalty = 1.7 + random.NextDouble(),
                CostPenalty = 1.1 + random.NextDouble() * 0.7,
                Explanation = "Improves valley access but leaves a weaker back-slope region and slightly higher interference.",
                SiteIds = ["cand_03"],
            },
            new PropagationRelayPlan
            {
                PlanId = "plateau_r27",
                Score = 71 + random.NextDouble() * 5,
                CoverageGain = 0.9 + random.NextDouble() * 0.4,
                ReliabilityGain = 4.2 + random.NextDouble() * 1.6,
                BlindAreaPenalty = 3.8 + random.NextDouble(),
                InterferencePenalty = 2.0 + random.NextDouble(),
                CostPenalty = 2.0 + random.NextDouble() * 0.8,
                Explanation = "Stable but costlier plateau deployment with less incremental coverage than ridge alternatives.",
                SiteIds = ["cand_02", "cand_03"],
            },
        };
    }

    private static PropagationCalibrationOutput BuildCalibrationOutput(PropagationSimulationRequest request)
    {
        var alphaDenseAfter = Math.Max(0.2, request.VegetationAlphaDense * 0.92);
        var alphaSparseAfter = Math.Max(0.1, request.VegetationAlphaSparse * 0.95);
        var shadowAfter = Math.Max(3.5, request.ShadowSigmaDb * 0.88);
        var reflectionAfter = Math.Clamp(request.ReflectionCoeff * 1.08, 0.05, 1.1);

        return new PropagationCalibrationOutput
        {
            TrainingSampleCount = 148,
            ValidationSampleCount = 56,
            MaeBefore = 9.8,
            MaeAfter = 7.4,
            MaeDelta = 2.4,
            RmseBefore = 12.1,
            RmseAfter = 9.6,
            RmseDelta = 2.5,
            ValidationMaeAfter = 7.9,
            ValidationRmseAfter = 10.2,
            CalibrationRunId = $"cal_{DateTimeOffset.UtcNow:yyyyMMdd_HHmm}",
            ParameterAdjustments =
            [
                new PropagationParameterAdjustment { Name = "veg_alpha_dense", Before = request.VegetationAlphaDense, After = alphaDenseAfter, Unit = "dB/m" },
                new PropagationParameterAdjustment { Name = "veg_alpha_sparse", Before = request.VegetationAlphaSparse, After = alphaSparseAfter, Unit = "dB/m" },
                new PropagationParameterAdjustment { Name = "shadow_sigma", Before = request.ShadowSigmaDb, After = shadowAfter, Unit = "dB" },
                new PropagationParameterAdjustment { Name = "reflection_coeff", Before = request.ReflectionCoeff, After = reflectionAfter, Unit = "--" },
            ],
        };
    }

    private static PropagationUncertaintyOutput BuildUncertaintyOutput(
        PropagationSimulationRequest request,
        double reliability95,
        double profileMarginDb,
        double shadowDb)
    {
        var iterations = request.EnableMonteCarlo ? Math.Max(200, request.MonteCarloIterations) : 0;
        return new PropagationUncertaintyOutput
        {
            Iterations = iterations,
            CiLower = Math.Max(0, reliability95 - 9.2),
            CiUpper = Math.Min(100, reliability95 + 6.1),
            StabilityIndex = Clamp01To100(72 + (request.EnableMonteCarlo ? 8 : 0) - request.ShadowSigmaDb * 1.2),
            MarginP10Db = profileMarginDb - shadowDb * 0.35,
            MarginP50Db = profileMarginDb,
            MarginP90Db = profileMarginDb + shadowDb * 0.24,
            SensitivitySummary = request.EnableMonteCarlo
                ? "Shadow sigma dominates robustness; dense vegetation alpha is the second strongest driver."
                : "Deterministic run only. Enable Monte Carlo for robustness ranking and confidence intervals.",
        };
    }

    private static PropagationSpatialAlignmentOutput BuildSpatialAlignmentOutput()
    {
        return new PropagationSpatialAlignmentOutput
        {
            TargetCrs = "EPSG:3857-localized",
            DemResamplingMethod = "bilinear",
            LandcoverResamplingMethod = "nearest",
            DemResolutionM = 10,
            LandcoverResolutionM = 10,
            HorizontalOffsetM = 1.6,
            VerticalOffsetM = 0.8,
            AlignmentScore = 96.2,
            Status = "aligned_within_tolerance",
        };
    }

    private static IEnumerable<string> BuildAssumptionFlags(PropagationSimulationRequest request)
    {
        yield return "single_knife_edge";
        yield return "no_foliage_seasonality";
        if (!request.EnableMonteCarlo)
            yield return "deterministic_shadow_realization";
        if (request.Mode != PropagationSimulationMode.AdvancedModeling)
            yield return "single_reflection_order";
    }

    private static IEnumerable<string> BuildValidityWarnings(
        PropagationSimulationRequest request,
        double downlinkMarginDb,
        double uplinkMarginDb)
    {
        if (request.FrequencyMHz is < 100 or > 3000)
            yield return "frequency_outside_validated_range";
        if (request.ReflectionCoeff is < 0 or > 1.2)
            yield return "reflection_coeff_outside_expected_range";
        if (request.ShadowSigmaDb is < 2 or > 14)
            yield return "shadow_sigma_outside_typical_mountain_range";
        if (downlinkMarginDb < 5 || uplinkMarginDb < 5)
            yield return "low_fade_margin";
    }

    private static int ParseSf(string spreadingFactor)
    {
        if (string.IsNullOrWhiteSpace(spreadingFactor))
            return 10;
        var text = spreadingFactor.Trim();
        if (text.StartsWith("SF", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sf) ? sf : 10;
    }

    private static double SfSensitivityDbm(int sf)
    {
        return sf switch
        {
            <= 7 => -123,
            8 => -126,
            9 => -129,
            10 => -132,
            11 => -134.5,
            _ => -137,
        };
    }

    private static double Clamp01To100(double value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private static string ComputeParameterHash(PropagationSimulationRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<PropagationScenePoint> BuildRelayCandidates(Random random, int ridgeCandidateCount)
    {
        var points = new List<PropagationScenePoint>();
        for (var index = 0; index < Math.Max(3, ridgeCandidateCount); index++)
        {
            points.Add(new PropagationScenePoint
            {
                Id = $"cand_{index + 1:00}",
                Label = index switch
                {
                    0 => "Ridge A",
                    1 => "Ridge B",
                    2 => "Spur C",
                    _ => $"Ridge {index + 1}",
                },
                X = 1080 + index * 180 + random.NextDouble() * 110,
                Z = 920 + index * 120 + random.NextDouble() * 95,
                Score = 0.58 + random.NextDouble() * 0.22,
            });
        }

        return points;
    }

    private static IReadOnlyList<PropagationScenePoint> BuildRelayRecommendations(Random random)
    {
        return new[]
        {
            new PropagationScenePoint { Id = "rec_01", Label = "Top-1", X = 1460 + random.NextDouble() * 80, Z = 1080 + random.NextDouble() * 80, Score = 0.86 + random.NextDouble() * 0.05 },
            new PropagationScenePoint { Id = "rec_02", Label = "Top-2", X = 1660 + random.NextDouble() * 90, Z = 1320 + random.NextDouble() * 70, Score = 0.80 + random.NextDouble() * 0.05 },
        };
    }

    private static IReadOnlyList<PropagationScenePoint> BuildProfileObstacles(ProfileSynthesis profile)
    {
        return new[]
        {
            new PropagationScenePoint { Id = "obs_01", Label = profile.MainObstacle.Label, X = 1380 + profile.MainObstacleDistanceKm * 120, Z = 1120 + profile.ObstructionAboveLosM * 5, Y = profile.MainObstacleHeightM },
            new PropagationScenePoint { Id = "obs_02", Label = "treebelt_02", X = 1190 + profile.DistanceKm * 40, Z = 980 + profile.SampleStepM * 0.4, Y = profile.MainObstacleHeightM - 12 },
        };
    }

    private static IReadOnlyList<PropagationScenePolyline> BuildProfileLines(ProfileSynthesis profile)
    {
        var startX = 1020d;
        var startZ = 930d;
        var endX = 1960d;
        var endZ = 1510d;
        return new[]
        {
            new PropagationScenePolyline
            {
                Id = "profile_main",
                Points = profile.Samples
                    .Select(sample =>
                    {
                        var ratio = profile.DistanceKm <= 0 ? 0 : sample.DistanceKm / profile.DistanceKm;
                        return new PropagationScenePolylinePoint
                        {
                            X = startX + (endX - startX) * ratio,
                            Z = startZ + (endZ - startZ) * ratio,
                            Y = sample.TerrainElevationM,
                        };
                    })
                    .ToArray()
            }
        };
    }

    private static IReadOnlyList<PropagationScenePolyline> BuildRidgeLines(Random random)
    {
        return
        [
            new PropagationScenePolyline
            {
                Id = "ridge_main",
                Points =
                [
                    new PropagationScenePolylinePoint { X = 1180 + random.NextDouble() * 40, Z = 960 + random.NextDouble() * 50 },
                    new PropagationScenePolylinePoint { X = 1460 + random.NextDouble() * 35, Z = 1130 + random.NextDouble() * 40 },
                    new PropagationScenePolylinePoint { X = 1710 + random.NextDouble() * 30, Z = 1320 + random.NextDouble() * 45 },
                ],
            },
            new PropagationScenePolyline
            {
                Id = "ridge_secondary",
                Points =
                [
                    new PropagationScenePolylinePoint { X = 980 + random.NextDouble() * 30, Z = 1420 + random.NextDouble() * 30 },
                    new PropagationScenePolylinePoint { X = 1240 + random.NextDouble() * 45, Z = 1600 + random.NextDouble() * 35 },
                    new PropagationScenePolylinePoint { X = 1510 + random.NextDouble() * 35, Z = 1750 + random.NextDouble() * 30 },
                ],
            },
        ];
    }

    private static double ComputeReflectionRelativeGainDb(double reflectionCoeff, double distanceKm, Random random)
    {
        var phase = distanceKm * 2.35 + random.NextDouble() * 0.7;
        return Math.Clamp(Math.Cos(phase) * Math.Max(0, reflectionCoeff) * 4.5, -4.5, 3.5);
    }

    private static string ResolveFresnelRisk(double clearanceRatio)
    {
        if (clearanceRatio >= 1)
            return "clear";
        if (clearanceRatio >= 0.6)
            return "watch";
        return "critical";
    }

    private static string ResolveMultipathRisk(double reflectionRelativeGainDb, double excessDelayNs)
    {
        if (reflectionRelativeGainDb > 1.5 && excessDelayNs < 120)
            return "constructive";
        if (reflectionRelativeGainDb < -1.5)
            return "deep_fade";
        return "moderate";
    }

    private static ProfileSynthesis BuildProfileSynthesis(
        double distanceKm,
        double frequencyMHz,
        PropagationSimulationRequest request,
        Random random)
    {
        var lambdaM = 300d / Math.Max(1, frequencyMHz);
        var sampleCount = 25;
        var distanceM = distanceKm * 1000d;
        var sampleStepM = distanceM / (sampleCount - 1);
        var startGroundM = 1180 + random.NextDouble() * 40;
        var endGroundM = 1250 + random.NextDouble() * 35;
        var txHeightM = startGroundM + 22;
        var rxHeightM = endGroundM + 16;
        var samples = new List<PropagationProfileSample>(sampleCount);

        var maxObstruction = double.MinValue;
        var minClearance = double.MaxValue;
        var mainObstacleIndex = 0;
        var vegetationLossDb = 0d;

        for (var index = 0; index < sampleCount; index++)
        {
            var ratio = index / (double)(sampleCount - 1);
            var d1 = ratio * distanceM;
            var d2 = distanceM - d1;
            var losHeight = txHeightM + (rxHeightM - txHeightM) * ratio;
            var ridge = 58 * Math.Exp(-Math.Pow((ratio - 0.58) / 0.12, 2));
            var spur = 16 * Math.Exp(-Math.Pow((ratio - 0.34) / 0.08, 2));
            var undulation = Math.Sin(ratio * Math.PI * 2.4) * 9;
            var terrain = startGroundM + (endGroundM - startGroundM) * ratio + ridge + spur + undulation;
            var fresnel = index is 0 or 24 ? 0 : Math.Sqrt(lambdaM * d1 * d2 / Math.Max(1, d1 + d2));
            var clearance = losHeight - terrain;
            var surfaceType = ratio switch
            {
                > 0.28 and < 0.42 => "sparse_forest",
                > 0.52 and < 0.68 => "dense_forest",
                _ => "bare_ground",
            };

            if (index > 0)
            {
                var previousRatio = (index - 1) / (double)(sampleCount - 1);
                var midpointRatio = (previousRatio + ratio) * 0.5d;
                var midpointLandcover = midpointRatio switch
                {
                    > 0.28 and < 0.42 => PropagationLandcoverClass.SparseForest,
                    > 0.52 and < 0.68 => PropagationLandcoverClass.DenseForest,
                    _ => PropagationLandcoverClass.BareGround,
                };
                vegetationLossDb += PropagationLandcoverModel.ResolvePathLossDb(
                    midpointLandcover,
                    sampleStepM,
                    request.VegetationAlphaSparse,
                    request.VegetationAlphaDense);
            }

            if (-clearance > maxObstruction)
            {
                maxObstruction = -clearance;
                mainObstacleIndex = index;
            }

            minClearance = Math.Min(minClearance, clearance - fresnel * 0.6);

            samples.Add(new PropagationProfileSample
            {
                Index = index,
                DistanceKm = d1 / 1000d,
                TerrainElevationM = terrain,
                LosHeightM = losHeight,
                FresnelRadiusM = fresnel,
                IsBlocked = clearance < 0,
                SurfaceType = surfaceType,
            });
        }

        var mainSample = samples[mainObstacleIndex];
        var obstructionAboveLosM = Math.Max(0, mainSample.TerrainElevationM - mainSample.LosHeightM);
        var isLineOfSight = obstructionAboveLosM <= 0.01;
        var d1Obstacle = Math.Max(1, mainSample.DistanceKm * 1000d);
        var d2Obstacle = Math.Max(1, distanceM - d1Obstacle);
        var v = obstructionAboveLosM > 0
            ? obstructionAboveLosM * Math.Sqrt((2d / lambdaM) * ((1d / d1Obstacle) + (1d / d2Obstacle)))
            : -1;
        var diffractionLossDb = v > -0.7
            ? 6.9 + 20 * Math.Log10(Math.Sqrt(Math.Pow(v - 0.1, 2) + 1) + v - 0.1)
            : 0;

        var centerFresnel = samples[sampleCount / 2].FresnelRadiusM;
        var clearanceRatio = centerFresnel <= 0 ? 1 : Math.Clamp((mainSample.LosHeightM - mainSample.TerrainElevationM) / (centerFresnel * 0.6), -1.5, 2);
        var fresnelAdditionalLossDb = clearanceRatio >= 1 ? 0 : Math.Clamp((1 - clearanceRatio) * 6.5, 0, 12);
        var obstacleLabel = isLineOfSight ? "treebelt_02" : "ridge_k2";
        var reflectionExcessDelayNs = 28 + distanceKm * 22 + random.NextDouble() * 36;

        return new ProfileSynthesis
        {
            DistanceKm = distanceKm,
            SampleStepM = sampleStepM,
            IsLineOfSight = isLineOfSight,
            MainObstacle = new PropagationMainObstacle
            {
                Label = obstacleLabel,
                V = Math.Max(v, -0.7),
                LdDb = diffractionLossDb,
            },
            MainObstacleDistanceKm = mainSample.DistanceKm,
            MainObstacleHeightM = mainSample.TerrainElevationM,
            ObstructionAboveLosM = obstructionAboveLosM,
            FresnelRadiusM = centerFresnel,
            FresnelClearanceRatio = clearanceRatio,
            MinimumClearanceM = minClearance,
            FresnelAdditionalLossDb = fresnelAdditionalLossDb,
            DiffractionLossDb = diffractionLossDb,
            VegetationLossDb = vegetationLossDb,
            ReflectionExcessDelayNs = reflectionExcessDelayNs,
            Samples = samples,
            RidgeCandidateCount = 5,
        };
    }

    private static PropagationTerrainMapOutput BuildTerrainMapOutput()
    {
        const double width = 3200;
        const double height = 2200;
        const double step = 50;
        var columns = (int)(width / step) + 1;
        var rows = (int)(height / step) + 1;
        var elevations = new double[columns * rows];
        var landcoverSamples = new PropagationLandcoverClass[columns * rows];
        var index = 0;

        for (var row = 0; row < rows; row++)
        {
            var z = row * step;
            for (var col = 0; col < columns; col++)
            {
                var x = col * step;
                elevations[index++] = 1180
                                      + 150 * Math.Exp(-Math.Pow((x - 980) / 420, 2) - Math.Pow((z - 620) / 280, 2))
                                      + 115 * Math.Exp(-Math.Pow((x - 1680) / 500, 2) - Math.Pow((z - 1080) / 320, 2))
                                      + 84 * Math.Exp(-Math.Pow((x - 2360) / 380, 2) - Math.Pow((z - 980) / 260, 2))
                                      - 72 * Math.Exp(-Math.Pow((x - 1960) / 540, 2) - Math.Pow((z - 1520) / 240, 2))
                                      + Math.Sin(x / 170) * 14
                                      + Math.Cos(z / 160) * 11;
                landcoverSamples[(row * columns) + col] = ResolveMockLandcoverAt(x, z);
            }
        }

        var contourLevels = new[] { 1180d, 1240d, 1300d, 1360d, 1420d, 1480d };
        return new PropagationTerrainMapOutput
        {
            Crs = "LOCAL_TERRAIN_M",
            WidthM = width,
            HeightM = height,
            SampleStepM = step,
            Columns = columns,
            Rows = rows,
            MinElevationM = 1120,
            MaxElevationM = 1520,
            ElevationSamples = elevations,
            LandcoverSamples = landcoverSamples,
            ContourLines = BuildContourLines(columns, rows, step, elevations, contourLevels),
            Sites =
            [
                new PropagationScenePoint { Id = "base", Label = "Base Station", X = 900, Z = 520, Y = 1310 },
                new PropagationScenePoint { Id = "node_a", Label = "Node A", X = 2740, Z = 760, Y = 1285 },
                new PropagationScenePoint { Id = "node_b", Label = "Node B", X = 2790, Z = 1390, Y = 1208 },
                new PropagationScenePoint { Id = "node_c", Label = "Node C", X = 2140, Z = 980, Y = 1338 },
            ],
        };
    }

    private static PropagationRasterBounds BuildTerrainBounds(PropagationTerrainMapOutput terrainMap)
    {
        return new PropagationRasterBounds
        {
            MinX = 0,
            MinZ = 0,
            MaxX = terrainMap.WidthM,
            MaxZ = terrainMap.HeightM,
        };
    }

    private static IReadOnlyList<PropagationScenePolyline> BuildContourLines(
        int columns,
        int rows,
        double step,
        IReadOnlyList<double> elevations,
        IReadOnlyList<double> levels)
    {
        var contours = new List<PropagationScenePolyline>();
        var contourIndex = 0;
        foreach (var level in levels)
        {
            for (var row = 0; row < rows - 1; row++)
            {
                for (var col = 0; col < columns - 1; col++)
                {
                    var corners = new[]
                    {
                        new ContourCorner(col * step, row * step, elevations[row * columns + col]),
                        new ContourCorner((col + 1) * step, row * step, elevations[row * columns + col + 1]),
                        new ContourCorner((col + 1) * step, (row + 1) * step, elevations[(row + 1) * columns + col + 1]),
                        new ContourCorner(col * step, (row + 1) * step, elevations[(row + 1) * columns + col]),
                    };

                    var intersections = new List<PropagationScenePolylinePoint>(4);
                    TryAddIntersection(intersections, corners[0], corners[1], level);
                    TryAddIntersection(intersections, corners[1], corners[2], level);
                    TryAddIntersection(intersections, corners[2], corners[3], level);
                    TryAddIntersection(intersections, corners[3], corners[0], level);

                    if (intersections.Count < 2)
                        continue;

                    for (var i = 0; i + 1 < intersections.Count; i += 2)
                    {
                        contours.Add(new PropagationScenePolyline
                        {
                            Id = $"mock_contour_{contourIndex++:0000}",
                            Points = [intersections[i], intersections[i + 1]],
                        });
                    }
                }
            }
        }

        return contours;
    }

    private static PropagationLandcoverClass ResolveMockLandcoverAt(double x, double z)
    {
        if (z > 1320 && x > 1720)
            return PropagationLandcoverClass.Water;
        if (x > 1100 && x < 2300 && z > 720 && z < 1320)
            return PropagationLandcoverClass.DenseForest;
        if (x > 600 && x < 2600 && z > 520 && z < 1700)
            return PropagationLandcoverClass.SparseForest;
        return PropagationLandcoverClass.BareGround;
    }

    private static void TryAddIntersection(List<PropagationScenePolylinePoint> intersections, ContourCorner a, ContourCorner b, double level)
    {
        var deltaA = a.Elevation - level;
        var deltaB = b.Elevation - level;
        if ((deltaA < 0 && deltaB < 0) || (deltaA > 0 && deltaB > 0))
            return;
        if (Math.Abs(deltaA - deltaB) < 0.0001)
            return;

        var t = (level - a.Elevation) / (b.Elevation - a.Elevation);
        if (double.IsNaN(t) || double.IsInfinity(t) || t < 0 || t > 1)
            return;

        intersections.Add(new PropagationScenePolylinePoint
        {
            X = a.X + ((b.X - a.X) * t),
            Z = a.Z + ((b.Z - a.Z) * t),
            Y = level,
        });
    }

    private sealed class RunContext
    {
        public object SyncRoot { get; } = new();
        public required PropagationSimulationRequest Request { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public PropagationJobState State { get; set; }
        public double ProgressPercent { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCanceled { get; set; }
        public bool CacheHit { get; init; }
        public PropagationSimulationResult? Result { get; set; }
    }

    private sealed class ProfileSynthesis
    {
        public double DistanceKm { get; init; }
        public double SampleStepM { get; init; }
        public bool IsLineOfSight { get; init; }
        public required PropagationMainObstacle MainObstacle { get; init; }
        public double MainObstacleDistanceKm { get; init; }
        public double MainObstacleHeightM { get; init; }
        public double ObstructionAboveLosM { get; init; }
        public double FresnelRadiusM { get; init; }
        public double FresnelClearanceRatio { get; init; }
        public double MinimumClearanceM { get; init; }
        public double FresnelAdditionalLossDb { get; init; }
        public double DiffractionLossDb { get; init; }
        public double VegetationLossDb { get; init; }
        public double ReflectionExcessDelayNs { get; init; }
        public int RidgeCandidateCount { get; init; }
        public required IReadOnlyList<PropagationProfileSample> Samples { get; init; }
    }

    private readonly record struct ContourCorner(double X, double Z, double Elevation);
}
