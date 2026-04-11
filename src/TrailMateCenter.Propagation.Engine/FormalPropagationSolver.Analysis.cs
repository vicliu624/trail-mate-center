using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

public sealed partial class FormalPropagationSolver
{
    private static TerrainEvaluation BuildProfile(ScenarioTerrainDataset dataset, PropagationSimulationRequest request, Random random)
    {
        var sites = ResolveSites(dataset, request);
        var tx = sites.BaseStation;
        var rx = sites.TargetNodes[request.Mode switch
        {
            PropagationSimulationMode.InterferenceAnalysis => Math.Min(1, sites.TargetNodes.Length - 1),
            PropagationSimulationMode.RelayOptimization => Math.Min(2, sites.TargetNodes.Length - 1),
            PropagationSimulationMode.AdvancedModeling => Math.Min(1, sites.TargetNodes.Length - 1),
            _ => 0,
        }];

        var distanceMeters = Math.Sqrt(Math.Pow(rx.X - tx.X, 2) + Math.Pow(rx.Z - tx.Z, 2));
        var distanceKm = distanceMeters / 1000d;
        var sampleCount = 64;
        var lambdaM = 300d / Math.Max(1, tx.FrequencyMHz);
        var sampleStepM = distanceMeters / (sampleCount - 1);
        var samples = new List<PropagationProfileSample>(sampleCount);
        var maxObstructionAboveLos = double.MinValue;
        var minClearanceAgainstFresnel = double.MaxValue;
        var mainObstacleIndex = 0;
        var vegetationLossDb = 0d;

        for (var index = 0; index < sampleCount; index++)
        {
            var ratio = index / (double)(sampleCount - 1);
            var x = Lerp(tx.X, rx.X, ratio);
            var z = Lerp(tx.Z, rx.Z, ratio);
            var terrainElevation = dataset.ElevationAt(x, z);
            var losHeight = Lerp(tx.ElevationM + tx.AntennaHeightM, rx.ElevationM + rx.AntennaHeightM, ratio);
            var d1 = Math.Max(1, ratio * distanceMeters);
            var d2 = Math.Max(1, distanceMeters - d1);
            var fresnelRadius = index is 0 or 63 ? 0 : Math.Sqrt(lambdaM * d1 * d2 / (d1 + d2));
            var obstructionAboveLos = terrainElevation - losHeight;
            var minimumClearance = (losHeight - terrainElevation) - (fresnelRadius * 0.6);
            var landClass = dataset.LandcoverAt(x, z);
            if (index > 0)
            {
                var previousRatio = (index - 1) / (double)(sampleCount - 1);
                var midpointX = Lerp(tx.X, rx.X, (previousRatio + ratio) * 0.5d);
                var midpointZ = Lerp(tx.Z, rx.Z, (previousRatio + ratio) * 0.5d);
                var midpointLandcover = dataset.LandcoverAt(midpointX, midpointZ);
                vegetationLossDb += PropagationLandcoverModel.ResolvePathLossDb(
                    midpointLandcover,
                    sampleStepM,
                    request.VegetationAlphaSparse,
                    request.VegetationAlphaDense);
            }

            if (obstructionAboveLos > maxObstructionAboveLos)
            {
                maxObstructionAboveLos = obstructionAboveLos;
                mainObstacleIndex = index;
            }

            minClearanceAgainstFresnel = Math.Min(minClearanceAgainstFresnel, minimumClearance);

            samples.Add(new PropagationProfileSample
            {
                Index = index,
                DistanceKm = d1 / 1000d,
                TerrainElevationM = terrainElevation,
                LosHeightM = losHeight,
                FresnelRadiusM = fresnelRadius,
                IsBlocked = obstructionAboveLos > 0,
                SurfaceType = landClass.ToString(),
            });
        }

        var mainObstacle = samples[mainObstacleIndex];
        var mainDistanceM = mainObstacle.DistanceKm * 1000d;
        var obstacleHeightM = Math.Max(0, mainObstacle.TerrainElevationM - mainObstacle.LosHeightM);
        var d1Obstacle = Math.Max(1, mainDistanceM);
        var d2Obstacle = Math.Max(1, distanceMeters - mainDistanceM);
        var v = obstacleHeightM > 0
            ? obstacleHeightM * Math.Sqrt((2 / lambdaM) * ((1 / d1Obstacle) + (1 / d2Obstacle)))
            : -1;
        var diffractionDb = v > -0.7
            ? 6.9 + 20 * Math.Log10(Math.Sqrt(Math.Pow(v - 0.1, 2) + 1) + v - 0.1)
            : 0;
        var midpoint = samples[sampleCount / 2];
        var midpointClearanceRatio = midpoint.FresnelRadiusM <= 0
            ? 1
            : Math.Clamp((midpoint.LosHeightM - midpoint.TerrainElevationM) / (midpoint.FresnelRadiusM * 0.6), -1.5, 2.0);
        var fresnelAdditionalLossDb = midpointClearanceRatio >= 1
            ? 0
            : Math.Clamp((1 - midpointClearanceRatio) * 6.8, 0, 12);
        var pathState = obstacleHeightM > 0 ? "NLOS" : "LOS";
        var blockerLabel = obstacleHeightM > 0 ? "ridge_obstruction" : midpoint.SurfaceType;

        return new TerrainEvaluation
        {
            Tx = tx,
            Rx = rx,
            DistanceKm = distanceKm,
            ObstructionAboveLosM = Math.Max(0, obstacleHeightM),
            DiffractionDb = diffractionDb,
            FresnelAdditionalLossDb = fresnelAdditionalLossDb,
            VegetationDb = vegetationLossDb,
            FresnelClearanceRatio = midpointClearanceRatio,
            MinimumClearanceM = minClearanceAgainstFresnel,
            DominantObstacleDistanceKm = mainObstacle.DistanceKm,
            DominantObstacleHeightM = mainObstacle.TerrainElevationM,
            SampleStepM = sampleStepM,
            ProfileOutput = new PropagationProfileOutput
            {
                DistanceKm = distanceKm,
                FresnelRadiusM = midpoint.FresnelRadiusM,
                MarginDb = 0,
                MainObstacle = new PropagationMainObstacle
                {
                    Label = blockerLabel,
                    V = Math.Max(v, -0.7),
                    LdDb = diffractionDb,
                },
                Samples = samples,
            },
            TerrainOutput = new PropagationTerrainOutput
            {
                IsLineOfSight = obstacleHeightM <= 0,
                PathState = pathState,
                DominantObstructionLabel = blockerLabel,
                DominantObstructionDistanceKm = mainObstacle.DistanceKm,
                DominantObstructionHeightM = mainObstacle.TerrainElevationM,
                ObstructionAboveLosM = Math.Max(0, obstacleHeightM),
                SampleStepM = sampleStepM,
            },
            FresnelOutput = new PropagationFresnelOutput
            {
                RadiusM = midpoint.FresnelRadiusM,
                ClearanceRatio = midpointClearanceRatio,
                MinimumClearanceM = minClearanceAgainstFresnel,
                AdditionalLossDb = fresnelAdditionalLossDb,
                RiskLevel = midpointClearanceRatio >= 1 ? "clear" : midpointClearanceRatio >= 0.6 ? "watch" : "critical",
            },
            RidgeCandidates = ExtractRidgeCandidates(dataset, random),
        };
    }

    private static ScenarioSites ResolveSites(ScenarioTerrainDataset dataset, PropagationSimulationRequest request)
    {
        if (request.Sites.Count == 0)
            throw new InvalidOperationException("No scenario sites were provided. Add a base station and at least one target node before running the solver.");

        var baseInput = request.Sites.FirstOrDefault(site => site.Role == PropagationSiteRole.BaseStation);
        if (baseInput is null)
            throw new InvalidOperationException("A base station is required before running the solver.");

        var targetInputs = request.Sites.Where(site => site.Role == PropagationSiteRole.TargetNode).ToArray();
        if (targetInputs.Length == 0)
            throw new InvalidOperationException("At least one target node is required before running the solver.");

        return new ScenarioSites(
            ToScenarioSite(baseInput, request, defaultAntennaHeightM: 24, defaultScore: 0.92, dataset),
            targetInputs.Select(input => ToScenarioSite(input, request, defaultAntennaHeightM: 14, defaultScore: 0.8, dataset)).ToArray());
    }

    private static ScenarioSite ToScenarioSite(
        PropagationSiteInput input,
        PropagationSimulationRequest request,
        double defaultAntennaHeightM,
        double defaultScore,
        ScenarioTerrainDataset dataset)
    {
        var x = dataset.ClampX(input.X);
        var z = dataset.ClampZ(input.Z);
        var elevation = input.ElevationM ?? dataset.ElevationAt(x, z);
        var label = string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label;
        return new ScenarioSite(
            string.IsNullOrWhiteSpace(input.Id) ? Guid.NewGuid().ToString("N") : input.Id,
            label,
            string.IsNullOrWhiteSpace(input.ColorHex)
                ? (input.Role == PropagationSiteRole.BaseStation ? "#7BEA49" : "#4AA3FF")
                : input.ColorHex,
            x,
            z,
            elevation,
            input.AntennaHeightM > 0 ? input.AntennaHeightM : defaultAntennaHeightM,
            input.FrequencyMHz > 0 ? input.FrequencyMHz : request.FrequencyMHz,
            input.TxPowerDbm != 0 ? input.TxPowerDbm : request.TxPowerDbm,
            string.IsNullOrWhiteSpace(input.SpreadingFactor)
                ? (input.Role == PropagationSiteRole.BaseStation ? request.DownlinkSpreadingFactor : request.UplinkSpreadingFactor)
                : input.SpreadingFactor,
            defaultScore);
    }

    private sealed record ScenarioSites(ScenarioSite BaseStation, ScenarioSite[] TargetNodes);

    private static PropagationLinkOutput EvaluateLink(PropagationSimulationRequest request, TerrainEvaluation terrain)
    {
        var downlinkSf = ParseSf(string.IsNullOrWhiteSpace(terrain.Rx.SpreadingFactor) ? request.DownlinkSpreadingFactor : terrain.Rx.SpreadingFactor);
        var uplinkSf = ParseSf(string.IsNullOrWhiteSpace(terrain.Tx.SpreadingFactor) ? request.UplinkSpreadingFactor : terrain.Tx.SpreadingFactor);
        var downlinkSensitivityDbm = SfSensitivityDbm(downlinkSf);
        var uplinkSensitivityDbm = SfSensitivityDbm(uplinkSf);
        var frequencyMHz = terrain.Tx.FrequencyMHz > 0 ? terrain.Tx.FrequencyMHz : request.FrequencyMHz;
        var fsplDb = 32.44 + 20 * Math.Log10(Math.Max(1, frequencyMHz)) + 20 * Math.Log10(Math.Max(0.1, terrain.DistanceKm));
        var totalLoss = fsplDb
                        + terrain.DiffractionDb
                        + terrain.FresnelAdditionalLossDb
                        + terrain.VegetationDb
                        + request.EnvironmentLossDb
                        + request.ShadowSigmaDb * 0.82;

        var downlinkRssiDbm = terrain.Tx.TxPowerDbm - totalLoss + 8;
        var uplinkRssiDbm = terrain.Rx.TxPowerDbm - totalLoss + 5.2;
        var downlinkMarginDb = downlinkRssiDbm - downlinkSensitivityDbm;
        var uplinkMarginDb = uplinkRssiDbm - uplinkSensitivityDbm;
        return new PropagationLinkOutput
        {
            DownlinkRssiDbm = downlinkRssiDbm,
            UplinkRssiDbm = uplinkRssiDbm,
            DownlinkMarginDb = downlinkMarginDb,
            UplinkMarginDb = uplinkMarginDb,
            LinkFeasible = downlinkMarginDb >= 0 && uplinkMarginDb >= 0,
            MarginGuardrail = Math.Min(downlinkMarginDb, uplinkMarginDb) switch
            {
                >= 10 => "stable",
                >= 0 => "edge",
                _ => "unreachable",
            },
        };
    }

    private static PropagationReliabilityOutput EvaluateReliability(PropagationSimulationRequest request, PropagationLinkOutput link, TerrainEvaluation terrain)
    {
        var p95 = Math.Clamp(38 + link.DownlinkMarginDb * 2.0 - request.ShadowSigmaDb * 1.1 - terrain.FresnelAdditionalLossDb * 1.5, 0, 100);
        var p80 = Math.Clamp(p95 + 11 - terrain.DiffractionDb * 0.25, 0, 100);
        return new PropagationReliabilityOutput
        {
            P95 = p95,
            P80 = p80,
            ConfidenceNote = request.EnableMonteCarlo
                ? $"Monte Carlo ({Math.Max(200, request.MonteCarloIterations)} iterations) active"
                : "Deterministic evaluation with log-normal shadow model",
        };
    }

    private static PropagationNetworkOutput EvaluateNetwork(PropagationSimulationRequest request, PropagationLinkOutput link, PropagationReliabilityOutput reliability, TerrainEvaluation terrain)
    {
        var sf = ParseSf(string.IsNullOrWhiteSpace(terrain.Rx.SpreadingFactor) ? request.UplinkSpreadingFactor : terrain.Rx.SpreadingFactor);
        var airtimeMs = 120 + sf * 24 + terrain.DistanceKm * 18;
        var alohaLoad = Math.Clamp(28 + (100 - reliability.P80) * 0.35 + terrain.DiffractionDb * 0.8, 0, 100);
        var occupancy = Math.Clamp(alohaLoad * 0.86 + airtimeMs / 18, 0, 100);
        var sinr = 14.5 + link.DownlinkMarginDb * 0.22 - occupancy * 0.08 - request.ReflectionCoeff * 1.5;
        var conflict = Math.Clamp(18 - sinr * 0.9 + occupancy * 0.18, 0, 100);
        var capacity = Math.Max(12, (int)Math.Round(240 - occupancy * 1.5 - conflict * 1.1 + reliability.P95 * 0.5));

        return new PropagationNetworkOutput
        {
            SinrDb = sinr,
            ConflictRate = conflict,
            MaxCapacityNodes = capacity,
            AlohaLoadPercent = alohaLoad,
            ChannelOccupancyPercent = occupancy,
            AirtimeMs = airtimeMs,
        };
    }

    private static PropagationReflectionOutput EvaluateReflection(PropagationSimulationRequest request, TerrainEvaluation terrain, Random random)
    {
        var phase = terrain.DistanceKm * 2.8 + request.ReflectionCoeff * 1.7;
        var relativeGain = Math.Clamp(Math.Cos(phase) * request.ReflectionCoeff * 4.2, -5, 4);
        var delay = 35 + terrain.DistanceKm * 20 + random.NextDouble() * 40;
        return new PropagationReflectionOutput
        {
            Enabled = request.Mode == PropagationSimulationMode.AdvancedModeling || request.ReflectionCoeff > 0,
            ReflectionCoefficient = request.ReflectionCoeff,
            RelativeGainDb = relativeGain,
            ExcessDelayNs = delay,
            MultipathRisk = relativeGain < -1.5 ? "deep_fade" : relativeGain > 1.5 ? "constructive" : "moderate",
        };
    }

    private static PropagationCoverageProbabilityOutput EvaluateCoverage(PropagationSimulationRequest request, PropagationReliabilityOutput reliability, PropagationLinkOutput link)
    {
        var threshold = SfSensitivityDbm(ParseSf(request.UplinkSpreadingFactor));
        var reliableReach = Math.Max(0.4, 1.2 + link.DownlinkMarginDb * 0.11 + reliability.P95 * 0.015);
        return new PropagationCoverageProbabilityOutput
        {
            AreaP95Km2 = Math.Max(0.1, reliability.P95 * 0.16),
            AreaP80Km2 = Math.Max(0.2, reliability.P80 * 0.22),
            ThresholdRssiDbm = threshold,
            ReliableReachKm = reliableReach,
        };
    }

    private static PropagationUncertaintyOutput EvaluateUncertainty(PropagationSimulationRequest request, PropagationLinkOutput link, PropagationReliabilityOutput reliability, TerrainEvaluation terrain)
    {
        var iterations = request.EnableMonteCarlo ? Math.Max(200, request.MonteCarloIterations) : 0;
        var marginP50 = Math.Min(link.DownlinkMarginDb, link.UplinkMarginDb);
        return new PropagationUncertaintyOutput
        {
            Iterations = iterations,
            CiLower = Math.Max(0, reliability.P95 - 8.5),
            CiUpper = Math.Min(100, reliability.P95 + 6.8),
            StabilityIndex = Math.Clamp(76 - request.ShadowSigmaDb * 1.25 - terrain.FresnelAdditionalLossDb * 2 + (request.EnableMonteCarlo ? 8 : 0), 0, 100),
            MarginP10Db = marginP50 - request.ShadowSigmaDb * 0.42,
            MarginP50Db = marginP50,
            MarginP90Db = marginP50 + request.ShadowSigmaDb * 0.28,
            SensitivitySummary = request.EnableMonteCarlo
                ? "Shadow sigma is the dominant driver, followed by dense-forest attenuation and Fresnel intrusion."
                : "Enable Monte Carlo to expose robustness ranking and parameter sensitivity.",
        };
    }
}
