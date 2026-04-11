using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

public sealed partial class FormalPropagationSolver : IPropagationSolver
{
    private const string ModelVersion = "prop_solver_1.0.0";

    public Task<PropagationSimulationResult> SolveAsync(
        string runId,
        PropagationSimulationRequest request,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataset = ScenarioTerrainDataset.Create(request);
        var seed = Math.Abs(HashCode.Combine(
            request.TerrainInput?.AoiId ?? request.ScenarioPresetName,
            request.FrequencyMHz,
            request.TxPowerDbm,
            request.ShadowSigmaDb,
            request.ReflectionCoeff,
            request.OptimizationAlgorithm,
            (int)request.Mode));
        var random = new Random(seed);

        var terrain = BuildProfile(dataset, request, random);
        var link = EvaluateLink(request, terrain);
        var reliability = EvaluateReliability(request, link, terrain);
        var network = EvaluateNetwork(request, link, reliability, terrain);
        var reflection = EvaluateReflection(request, terrain, random);
        var coverage = EvaluateCoverage(request, reliability, link);
        var uncertainty = EvaluateUncertainty(request, link, reliability, terrain);
        var calibration = EvaluateCalibration(request);
        var alignment = EvaluateSpatialAlignment(dataset);
        var lossBreakdown = BuildLossBreakdown(request, terrain, reflection);
        var optimization = BuildOptimization(dataset, request, reliability, network, terrain, random);
        var duration = DateTimeOffset.UtcNow - startedAtUtc;
        var parameterHash = ComputeParameterHash(request);

        return Task.FromResult(new PropagationSimulationResult
        {
            RunMeta = new PropagationRunMeta
            {
                RunId = runId,
                Status = PropagationJobState.Completed,
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                DurationMs = Math.Max(1, (long)duration.TotalMilliseconds),
                ProgressPercent = 100,
                CacheHit = false,
            },
            InputBundle = new PropagationInputBundle
            {
                AoiId = dataset.DatasetId,
                NodeSetVersion = dataset.NodeSetVersion,
                ParameterSetVersion = $"paramset_{dataset.DatasetId}_{DateTimeOffset.UtcNow:yyyyMMdd}",
            },
            ModelOutputs = BuildModelOutputs(runId, dataset),
            AnalysisOutputs = new PropagationAnalysisOutputs
            {
                Link = link,
                Terrain = terrain.TerrainOutput,
                TerrainMap = BuildTerrainMap(dataset, request),
                Reliability = reliability,
                LossBreakdown = lossBreakdown,
                Fresnel = terrain.FresnelOutput,
                CoverageProbability = coverage,
                Network = network,
                Profile = new PropagationProfileOutput
                {
                    DistanceKm = terrain.ProfileOutput.DistanceKm,
                    FresnelRadiusM = terrain.ProfileOutput.FresnelRadiusM,
                    MarginDb = Math.Min(link.DownlinkMarginDb, link.UplinkMarginDb),
                    MainObstacle = terrain.ProfileOutput.MainObstacle,
                    Samples = terrain.ProfileOutput.Samples,
                },
                Optimization = optimization,
                Reflection = reflection,
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
                AssumptionFlags = BuildAssumptionFlags(request).ToArray(),
                ValidityWarnings = BuildValidityWarnings(request, link, alignment).ToArray(),
            },
            SceneGeometry = BuildSceneGeometry(dataset, terrain, optimization),
        });
    }

    private static string ComputeParameterHash(PropagationSimulationRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
