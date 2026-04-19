using System;
using System.Collections.Generic;
using System.Linq;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public sealed class PropagationCoverageCellViewModel
{
    public int Row { get; init; }
    public int Column { get; init; }
    public double X { get; init; }
    public double Z { get; init; }
    public double WidthM { get; init; }
    public double HeightM { get; init; }
    public bool IsComputed { get; init; }
    public PropagationCoverageStatus Status { get; init; }
    public double DistanceM { get; init; }
    public double ElevationM { get; init; }
    public double ReceivedPowerDbm { get; init; }
    public double ThresholdDbm { get; init; }
    public double MarginDb { get; init; }
    public bool IsLineOfSight { get; init; }
    public bool CrossesRidge { get; init; }
    public int RidgeCrossings { get; init; }
    public PropagationLandcoverClass LandcoverClass { get; init; }
    public double LandcoverInputCoefficientDbPerM { get; init; }
    public double LandcoverEffectiveCoefficientDbPerM { get; init; }
    public double FsplDb { get; init; }
    public double DiffractionLossDb { get; init; }
    public double FresnelLossDb { get; init; }
    public double LandcoverLossDb { get; init; }
    public double ReflectionLossDb { get; init; }
    public double ShadowLossDb { get; init; }
    public double EnvironmentLossDb { get; init; }
    public double RidgePenaltyDb { get; init; }
    public double TotalLossDb { get; init; }
    public double ObstructionAboveLosM { get; init; }
    public double DominantObstructionDistanceM { get; init; }
    public double FresnelClearanceRatio { get; init; }
    public double MinimumClearanceM { get; init; }
    public string DominantReasonCode { get; init; } = string.Empty;
    public string DominantObstructionCode { get; init; } = string.Empty;
}

internal sealed class PropagationSelectedPathPreview
{
    public string TxSiteLabel { get; init; } = string.Empty;
    public string RxSiteLabel { get; init; } = string.Empty;
    public double DistanceM { get; init; }
    public bool IsLineOfSight { get; init; }
    public int RidgeCrossings { get; init; }
    public double ThresholdDbm { get; init; }
    public double FsplDb { get; init; }
    public double DiffractionLossDb { get; init; }
    public double FresnelLossDb { get; init; }
    public double LandcoverLossDb { get; init; }
    public double ReflectionLossDb { get; init; }
    public double ShadowLossDb { get; init; }
    public double EnvironmentLossDb { get; init; }
    public double RidgePenaltyDb { get; init; }
    public double TotalLossDb { get; init; }
    public double ReceivedPowerDbm { get; init; }
    public double MarginDb { get; init; }
    public double ObstructionAboveLosM { get; init; }
    public double FresnelClearanceRatio { get; init; }
    public double MinimumClearanceM { get; init; }
    public PropagationLandcoverClass RxLandcoverClass { get; init; }
    public double RxLandcoverInputCoefficientDbPerM { get; init; }
    public double RxLandcoverEffectiveCoefficientDbPerM { get; init; }
    public string DominantReasonCode { get; init; } = string.Empty;
    public string DominantObstructionCode { get; init; } = string.Empty;
    public IReadOnlyList<PropagationLandcoverSegmentContribution> LandcoverSegments { get; init; } = Array.Empty<PropagationLandcoverSegmentContribution>();
}

internal readonly record struct PropagationLandcoverSegmentContribution(
    PropagationLandcoverClass LandcoverClass,
    double LengthM,
    double LossDb);

internal static class PropagationCoveragePreviewBuilder
{
    public static IReadOnlyList<PropagationCoverageCellViewModel> Build(
        PropagationTerrainMapSceneViewModel scene,
        PropagationSiteInput site,
        double defaultFrequencyMHz,
        double defaultTxPowerDbm,
        string defaultSpreadingFactor,
        double vegetationAlphaSparse,
        double vegetationAlphaDense,
        double environmentLossDb,
        double shadowSigmaDb,
        double reflectionCoeff)
    {
        if (!scene.HasTerrain || scene.Columns <= 0 || scene.Rows <= 0 || scene.WidthM <= 0 || scene.HeightM <= 0)
            return Array.Empty<PropagationCoverageCellViewModel>();

        var frequencyMHz = site.FrequencyMHz > 0 ? site.FrequencyMHz : defaultFrequencyMHz;
        var txPowerDbm = site.TxPowerDbm != 0 ? site.TxPowerDbm : defaultTxPowerDbm;
        var spreadingFactor = string.IsNullOrWhiteSpace(site.SpreadingFactor) ? defaultSpreadingFactor : site.SpreadingFactor;
        var thresholdDbm = ResolveSensitivityDbm(spreadingFactor);
        var txElevationM = site.ElevationM ?? SampleElevation(scene, site.X, site.Z);
        var txHeightM = txElevationM + Math.Max(1d, site.AntennaHeightM);
        var rxAntennaHeightM = site.Role == PropagationSiteRole.BaseStation
            ? 14d
            : Math.Max(6d, Math.Min(14d, site.AntennaHeightM));
        var txAntennaBonusDb = ResolveAntennaBonusDb(site.AntennaHeightM);
        var rxAntennaBonusDb = ResolveAntennaBonusDb(rxAntennaHeightM);
        var spreadingGainDb = ResolveSpreadingFactorGainDb(spreadingFactor);
        var shadowPenaltyDb = Math.Max(0d, shadowSigmaDb) * 0.32d;
        var reflectionPenaltyDb = Math.Abs(reflectionCoeff - 0.35d) * 2.2d;
        ResolveSceneBounds(scene, out var minX, out var minZ, out _, out _);
        var cellWidthM = scene.WidthM / Math.Max(1d, scene.Columns);
        var cellHeightM = scene.HeightM / Math.Max(1d, scene.Rows);
        var cells = new List<PropagationCoverageCellViewModel>(scene.Columns * scene.Rows);

        for (var row = 0; row < scene.Rows; row++)
        {
            var z = minZ + ((row + 0.5d) * cellHeightM);
            for (var col = 0; col < scene.Columns; col++)
            {
                var x = minX + ((col + 0.5d) * cellWidthM);
                var distanceM = Math.Sqrt(Math.Pow(x - site.X, 2) + Math.Pow(z - site.Z, 2));
                var effectiveDistanceM = Math.Max(1d, distanceM);
                var rxElevationM = SampleElevation(scene, x, z);
                var rxHeightM = rxElevationM + rxAntennaHeightM;
                var path = EvaluatePath(
                    scene,
                    site.X,
                    site.Z,
                    txHeightM,
                    x,
                    z,
                    rxHeightM,
                    frequencyMHz,
                    vegetationAlphaSparse,
                    vegetationAlphaDense,
                    reflectionCoeff);
                var ridgeCrossings = CountRidgeCrossings(scene.RidgeLines, site.X, site.Z, x, z);
                var ridgePenaltyDb = ridgeCrossings == 0
                    ? 0d
                    : Math.Min(8d, ridgeCrossings * (path.IsLineOfSight ? 0.85d : 2.6d));
                var distanceKm = Math.Max(0.001d, effectiveDistanceM / 1000d);
                var fsplDb = 32.44d + (20d * Math.Log10(Math.Max(1d, frequencyMHz))) + (20d * Math.Log10(distanceKm));
                var totalLossDb =
                    fsplDb +
                    environmentLossDb +
                    shadowPenaltyDb +
                    reflectionPenaltyDb +
                    path.DiffractionLossDb +
                    path.FresnelLossDb +
                    path.LandcoverLossDb +
                    ridgePenaltyDb;

                var receivedPowerDbm =
                    txPowerDbm +
                    spreadingGainDb +
                    txAntennaBonusDb +
                    rxAntennaBonusDb -
                    totalLossDb;
                var marginDb = receivedPowerDbm - thresholdDbm;

                cells.Add(new PropagationCoverageCellViewModel
                {
                    Row = row,
                    Column = col,
                    X = x,
                    Z = z,
                    WidthM = cellWidthM,
                    HeightM = cellHeightM,
                    IsComputed = true,
                    Status = PropagationCoveragePresentation.ResolveStatus(marginDb, isComputed: true),
                    DistanceM = distanceM,
                    ElevationM = rxElevationM,
                    ReceivedPowerDbm = receivedPowerDbm,
                    ThresholdDbm = thresholdDbm,
                    MarginDb = marginDb,
                    IsLineOfSight = path.IsLineOfSight,
                    CrossesRidge = ridgeCrossings > 0,
                    RidgeCrossings = ridgeCrossings,
                    LandcoverClass = path.RxLandcoverClass,
                    LandcoverInputCoefficientDbPerM = path.RxLandcoverInputCoefficientDbPerM,
                    LandcoverEffectiveCoefficientDbPerM = path.RxLandcoverEffectiveCoefficientDbPerM,
                    FsplDb = fsplDb,
                    DiffractionLossDb = path.DiffractionLossDb,
                    FresnelLossDb = path.FresnelLossDb,
                    LandcoverLossDb = path.LandcoverLossDb,
                    ReflectionLossDb = reflectionPenaltyDb,
                    ShadowLossDb = shadowPenaltyDb,
                    EnvironmentLossDb = environmentLossDb,
                    RidgePenaltyDb = ridgePenaltyDb,
                    TotalLossDb = totalLossDb,
                    ObstructionAboveLosM = path.MaxObstructionAboveLosM,
                    DominantObstructionDistanceM = path.ObstructionDistanceM,
                    FresnelClearanceRatio = path.WorstClearanceRatio,
                    MinimumClearanceM = path.MinimumClearanceM,
                    DominantReasonCode = ResolveDominantReasonCode(
                        path.IsLineOfSight,
                        marginDb,
                        ridgeCrossings,
                        path.DiffractionLossDb,
                        path.FresnelLossDb,
                        path.LandcoverLossDb,
                        shadowPenaltyDb,
                        reflectionPenaltyDb,
                        environmentLossDb,
                        ridgePenaltyDb),
                    DominantObstructionCode = path.DominantObstructionCode,
                });
            }
        }

        return cells;
    }

    public static IReadOnlyList<PropagationCoverageCellViewModel> BuildNoData(
        PropagationTerrainMapSceneViewModel scene,
        double vegetationAlphaSparse,
        double vegetationAlphaDense)
    {
        if (!scene.HasTerrain || scene.Columns <= 0 || scene.Rows <= 0 || scene.WidthM <= 0 || scene.HeightM <= 0)
            return Array.Empty<PropagationCoverageCellViewModel>();

        ResolveSceneBounds(scene, out var minX, out var minZ, out _, out _);
        var cellWidthM = scene.WidthM / Math.Max(1d, scene.Columns);
        var cellHeightM = scene.HeightM / Math.Max(1d, scene.Rows);
        var cells = new List<PropagationCoverageCellViewModel>(scene.Columns * scene.Rows);

        for (var row = 0; row < scene.Rows; row++)
        {
            var z = minZ + ((row + 0.5d) * cellHeightM);
            for (var col = 0; col < scene.Columns; col++)
            {
                var x = minX + ((col + 0.5d) * cellWidthM);
                var landcover = SampleLandcover(scene, x, z);
                cells.Add(new PropagationCoverageCellViewModel
                {
                    Row = row,
                    Column = col,
                    X = x,
                    Z = z,
                    WidthM = cellWidthM,
                    HeightM = cellHeightM,
                    IsComputed = false,
                    Status = PropagationCoverageStatus.NoData,
                    DistanceM = double.NaN,
                    ElevationM = SampleElevation(scene, x, z),
                    ReceivedPowerDbm = double.NaN,
                    ThresholdDbm = double.NaN,
                    MarginDb = double.NaN,
                    LandcoverClass = landcover,
                    LandcoverInputCoefficientDbPerM = PropagationLandcoverPresentation.ResolveInputCoefficientDbPerM(
                        landcover,
                        vegetationAlphaSparse,
                        vegetationAlphaDense),
                    LandcoverEffectiveCoefficientDbPerM = PropagationLandcoverPresentation.ResolveEffectiveCoefficientDbPerM(
                        landcover,
                        vegetationAlphaSparse,
                        vegetationAlphaDense),
                    FsplDb = double.NaN,
                    DiffractionLossDb = double.NaN,
                    FresnelLossDb = double.NaN,
                    LandcoverLossDb = double.NaN,
                    ReflectionLossDb = double.NaN,
                    ShadowLossDb = double.NaN,
                    EnvironmentLossDb = double.NaN,
                    RidgePenaltyDb = double.NaN,
                    TotalLossDb = double.NaN,
                    ObstructionAboveLosM = double.NaN,
                    DominantObstructionDistanceM = double.NaN,
                    FresnelClearanceRatio = double.NaN,
                    MinimumClearanceM = double.NaN,
                    DominantReasonCode = "no_data",
                    DominantObstructionCode = "clear_path",
                });
            }
        }

        return cells;
    }

    public static PropagationSelectedPathPreview? BuildPathPreview(
        PropagationTerrainMapSceneViewModel scene,
        PropagationSiteInput txSite,
        PropagationSiteInput rxSite,
        double defaultFrequencyMHz,
        double defaultTxPowerDbm,
        string defaultSpreadingFactor,
        double vegetationAlphaSparse,
        double vegetationAlphaDense,
        double environmentLossDb,
        double shadowSigmaDb,
        double reflectionCoeff)
    {
        if (!scene.HasTerrain || scene.Columns <= 0 || scene.Rows <= 0 || scene.WidthM <= 0 || scene.HeightM <= 0)
            return null;

        var frequencyMHz = txSite.FrequencyMHz > 0
            ? txSite.FrequencyMHz
            : rxSite.FrequencyMHz > 0
                ? rxSite.FrequencyMHz
                : defaultFrequencyMHz;
        var txPowerDbm = txSite.TxPowerDbm != 0 ? txSite.TxPowerDbm : defaultTxPowerDbm;
        var spreadingFactor = !string.IsNullOrWhiteSpace(rxSite.SpreadingFactor)
            ? rxSite.SpreadingFactor
            : !string.IsNullOrWhiteSpace(txSite.SpreadingFactor)
                ? txSite.SpreadingFactor
                : defaultSpreadingFactor;
        var thresholdDbm = ResolveSensitivityDbm(spreadingFactor);
        var distanceM = Math.Sqrt(Math.Pow(rxSite.X - txSite.X, 2) + Math.Pow(rxSite.Z - txSite.Z, 2));
        if (distanceM < 1d)
            return null;

        var txElevationM = txSite.ElevationM ?? SampleElevation(scene, txSite.X, txSite.Z);
        var rxElevationM = rxSite.ElevationM ?? SampleElevation(scene, rxSite.X, rxSite.Z);
        var txHeightM = txElevationM + Math.Max(1d, txSite.AntennaHeightM);
        var rxHeightM = rxElevationM + Math.Max(1d, rxSite.AntennaHeightM);
        var txAntennaBonusDb = ResolveAntennaBonusDb(txSite.AntennaHeightM);
        var rxAntennaBonusDb = ResolveAntennaBonusDb(rxSite.AntennaHeightM);
        var spreadingGainDb = ResolveSpreadingFactorGainDb(spreadingFactor);
        var shadowPenaltyDb = Math.Max(0d, shadowSigmaDb) * 0.32d;
        var reflectionPenaltyDb = Math.Abs(reflectionCoeff - 0.35d) * 2.2d;

        var path = EvaluatePath(
            scene,
            txSite.X,
            txSite.Z,
            txHeightM,
            rxSite.X,
            rxSite.Z,
            rxHeightM,
            frequencyMHz,
            vegetationAlphaSparse,
            vegetationAlphaDense,
            reflectionCoeff,
            includeLandcoverBreakdown: true);
        var ridgeCrossings = CountRidgeCrossings(scene.RidgeLines, txSite.X, txSite.Z, rxSite.X, rxSite.Z);
        var ridgePenaltyDb = ridgeCrossings == 0
            ? 0d
            : Math.Min(8d, ridgeCrossings * (path.IsLineOfSight ? 0.85d : 2.6d));
        var distanceKm = Math.Max(0.001d, distanceM / 1000d);
        var fsplDb = 32.44d + (20d * Math.Log10(Math.Max(1d, frequencyMHz))) + (20d * Math.Log10(distanceKm));
        var totalLossDb =
            fsplDb +
            environmentLossDb +
            shadowPenaltyDb +
            reflectionPenaltyDb +
            path.DiffractionLossDb +
            path.FresnelLossDb +
            path.LandcoverLossDb +
            ridgePenaltyDb;
        var receivedPowerDbm =
            txPowerDbm +
            spreadingGainDb +
            txAntennaBonusDb +
            rxAntennaBonusDb -
            totalLossDb;
        var marginDb = receivedPowerDbm - thresholdDbm;

        return new PropagationSelectedPathPreview
        {
            TxSiteLabel = txSite.Label,
            RxSiteLabel = rxSite.Label,
            DistanceM = distanceM,
            IsLineOfSight = path.IsLineOfSight,
            RidgeCrossings = ridgeCrossings,
            ThresholdDbm = thresholdDbm,
            FsplDb = fsplDb,
            DiffractionLossDb = path.DiffractionLossDb,
            FresnelLossDb = path.FresnelLossDb,
            LandcoverLossDb = path.LandcoverLossDb,
            ReflectionLossDb = reflectionPenaltyDb,
            ShadowLossDb = shadowPenaltyDb,
            EnvironmentLossDb = environmentLossDb,
            RidgePenaltyDb = ridgePenaltyDb,
            TotalLossDb = totalLossDb,
            ReceivedPowerDbm = receivedPowerDbm,
            MarginDb = marginDb,
            ObstructionAboveLosM = path.MaxObstructionAboveLosM,
            FresnelClearanceRatio = path.WorstClearanceRatio,
            MinimumClearanceM = path.MinimumClearanceM,
            RxLandcoverClass = path.RxLandcoverClass,
            RxLandcoverInputCoefficientDbPerM = path.RxLandcoverInputCoefficientDbPerM,
            RxLandcoverEffectiveCoefficientDbPerM = path.RxLandcoverEffectiveCoefficientDbPerM,
            DominantReasonCode = ResolveDominantReasonCode(
                path.IsLineOfSight,
                marginDb,
                ridgeCrossings,
                path.DiffractionLossDb,
                path.FresnelLossDb,
                path.LandcoverLossDb,
                shadowPenaltyDb,
                reflectionPenaltyDb,
                environmentLossDb,
                ridgePenaltyDb),
            DominantObstructionCode = path.DominantObstructionCode,
            LandcoverSegments = path.LandcoverSegments,
        };
    }

    private static PathLossEvaluation EvaluatePath(
        PropagationTerrainMapSceneViewModel scene,
        double txX,
        double txZ,
        double txHeightM,
        double rxX,
        double rxZ,
        double rxHeightM,
        double frequencyMHz,
        double vegetationAlphaSparse,
        double vegetationAlphaDense,
        double reflectionCoeff,
        bool includeLandcoverBreakdown = false)
    {
        var rxLandcoverClass = SampleLandcover(scene, rxX, rxZ);
        var rxInputCoefficientDbPerM = PropagationLandcoverPresentation.ResolveInputCoefficientDbPerM(
            rxLandcoverClass,
            vegetationAlphaSparse,
            vegetationAlphaDense);
        var rxEffectiveCoefficientDbPerM = PropagationLandcoverPresentation.ResolveEffectiveCoefficientDbPerM(
            rxLandcoverClass,
            vegetationAlphaSparse,
            vegetationAlphaDense);
        var distanceM = Math.Sqrt(Math.Pow(rxX - txX, 2) + Math.Pow(rxZ - txZ, 2));
        if (distanceM < 1d)
        {
            return new PathLossEvaluation(
                true,
                0d,
                0d,
                0d,
                0d,
                0d,
                1d,
                0d,
                rxLandcoverClass,
                rxInputCoefficientDbPerM,
                rxEffectiveCoefficientDbPerM,
                rxLandcoverClass == PropagationLandcoverClass.BareGround ? "clear_path" : rxLandcoverClass.ToString(),
                Array.Empty<PropagationLandcoverSegmentContribution>());
        }

        var lambdaM = 300d / Math.Max(1d, frequencyMHz);
        var sampleSpacingM = Math.Max(20d, Math.Min(scene.WidthM / Math.Max(1d, scene.Columns), scene.HeightM / Math.Max(1d, scene.Rows)));
        var sampleCount = (int)Math.Clamp(Math.Ceiling(distanceM / sampleSpacingM) + 1, 12, 128);
        var segmentLengthM = distanceM / Math.Max(1d, sampleCount - 1d);
        var maxObstructionAboveLosM = double.MinValue;
        var worstClearanceRatio = double.MaxValue;
        var minimumClearanceM = double.MaxValue;
        var obstructionDistanceM = 0d;
        var landcoverLossDb = 0d;
        Dictionary<PropagationLandcoverClass, LandcoverContributionAccumulator>? landcoverContributions = includeLandcoverBreakdown
            ? new Dictionary<PropagationLandcoverClass, LandcoverContributionAccumulator>()
            : null;

        for (var index = 1; index < sampleCount - 1; index++)
        {
            var ratio = index / (double)(sampleCount - 1);
            var x = Lerp(txX, rxX, ratio);
            var z = Lerp(txZ, rxZ, ratio);
            var terrainElevationM = SampleElevation(scene, x, z);
            var losHeightM = Lerp(txHeightM, rxHeightM, ratio);
            var d1 = Math.Max(1d, ratio * distanceM);
            var d2 = Math.Max(1d, distanceM - d1);
            var fresnelRadiusM = Math.Sqrt(lambdaM * d1 * d2 / (d1 + d2));
            var obstructionAboveLosM = terrainElevationM - losHeightM;
            var physicalClearanceM = losHeightM - terrainElevationM;
            var clearanceRatio = fresnelRadiusM <= 0d
                ? 1d
                : Math.Clamp(physicalClearanceM / (fresnelRadiusM * 0.6d), -2d, 2d);
            var clearanceAgainstFresnelM = physicalClearanceM - (fresnelRadiusM * 0.6d);

            if (obstructionAboveLosM > maxObstructionAboveLosM)
            {
                maxObstructionAboveLosM = obstructionAboveLosM;
                obstructionDistanceM = d1;
            }

            worstClearanceRatio = Math.Min(worstClearanceRatio, clearanceRatio);
            minimumClearanceM = Math.Min(minimumClearanceM, clearanceAgainstFresnelM);
        }

        for (var index = 1; index < sampleCount; index++)
        {
            var ratio = index / (double)(sampleCount - 1);
            var previousRatio = (index - 1) / (double)(sampleCount - 1);
            var midpointX = Lerp(txX, rxX, (previousRatio + ratio) * 0.5d);
            var midpointZ = Lerp(txZ, rxZ, (previousRatio + ratio) * 0.5d);
            var midpointLandcover = SampleLandcover(scene, midpointX, midpointZ);
            var segmentLossDb = PropagationLandcoverModel.ResolvePathLossDb(
                midpointLandcover,
                segmentLengthM,
                vegetationAlphaSparse,
                vegetationAlphaDense);
            landcoverLossDb += segmentLossDb;
            if (landcoverContributions is not null)
                AddLandcoverContribution(landcoverContributions, midpointLandcover, segmentLengthM, segmentLossDb);
        }

        var obstacleHeightM = Math.Max(0d, maxObstructionAboveLosM);
        var d1Obstacle = Math.Max(1d, obstructionDistanceM);
        var d2Obstacle = Math.Max(1d, distanceM - obstructionDistanceM);
        var v = obstacleHeightM > 0d
            ? obstacleHeightM * Math.Sqrt((2d / lambdaM) * ((1d / d1Obstacle) + (1d / d2Obstacle)))
            : -1d;
        var diffractionLossDb = v > -0.7d
            ? 6.9d + (20d * Math.Log10(Math.Sqrt(Math.Pow(v - 0.1d, 2) + 1d) + v - 0.1d))
            : 0d;
        var fresnelLossDb = worstClearanceRatio >= 1d
            ? 0d
            : Math.Clamp((1d - worstClearanceRatio) * 6.8d, 0d, 18d);
        if (rxLandcoverClass == PropagationLandcoverClass.Water)
        {
            var waterTerminationLossDb = Math.Clamp(Math.Abs(reflectionCoeff - 0.28d) * 1.5d, 0d, 1.6d);
            landcoverLossDb += waterTerminationLossDb;
            if (landcoverContributions is not null)
                AddLandcoverContribution(landcoverContributions, PropagationLandcoverClass.Water, 0d, waterTerminationLossDb);
        }

        var landcoverSegments = landcoverContributions is null
            ? Array.Empty<PropagationLandcoverSegmentContribution>()
            : landcoverContributions
                .Select(entry => new PropagationLandcoverSegmentContribution(
                    entry.Key,
                    entry.Value.LengthM,
                    entry.Value.LossDb))
                .OrderByDescending(entry => entry.LossDb)
                .ThenByDescending(entry => entry.LengthM)
                .ToArray();

        var dominantObstructionCode = obstacleHeightM > 0d
            ? "ridge_obstruction"
            : rxLandcoverClass == PropagationLandcoverClass.BareGround
                ? "clear_path"
                : rxLandcoverClass.ToString();

        return new PathLossEvaluation(
            obstacleHeightM <= 0d,
            diffractionLossDb,
            fresnelLossDb,
            landcoverLossDb,
            obstacleHeightM,
            obstructionDistanceM,
            worstClearanceRatio,
            minimumClearanceM,
            rxLandcoverClass,
            rxInputCoefficientDbPerM,
            rxEffectiveCoefficientDbPerM,
            dominantObstructionCode,
            landcoverSegments);
    }

    private static double SampleElevation(PropagationTerrainMapSceneViewModel scene, double x, double z)
    {
        var columns = scene.Columns;
        var rows = scene.Rows;
        if (columns <= 0 || rows <= 0 || scene.ElevationSamples.Count < columns * rows)
            return scene.MinElevationM;

        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        var normalizedX = ResolveCellCenteredIndex(x, minX, maxX, columns);
        var normalizedZ = ResolveCellCenteredIndex(z, minZ, maxZ, rows);
        var x0 = (int)Math.Floor(normalizedX);
        var z0 = (int)Math.Floor(normalizedZ);
        x0 = Math.Clamp(x0, 0, columns - 1);
        z0 = Math.Clamp(z0, 0, rows - 1);
        var x1 = Math.Clamp(x0 + 1, 0, columns - 1);
        var z1 = Math.Clamp(z0 + 1, 0, rows - 1);
        var tx = Math.Clamp(normalizedX - x0, 0d, 1d);
        var tz = Math.Clamp(normalizedZ - z0, 0d, 1d);

        var e00 = scene.ElevationSamples[(z0 * columns) + x0];
        var e10 = scene.ElevationSamples[(z0 * columns) + x1];
        var e01 = scene.ElevationSamples[(z1 * columns) + x0];
        var e11 = scene.ElevationSamples[(z1 * columns) + x1];
        var e0 = Lerp(e00, e10, tx);
        var e1 = Lerp(e01, e11, tx);
        return Lerp(e0, e1, tz);
    }

    private static int CountRidgeCrossings(IReadOnlyList<PropagationScenePolyline> ridgeLines, double x1, double z1, double x2, double z2)
    {
        var count = 0;

        foreach (var ridgeLine in ridgeLines)
        {
            for (var index = 1; index < ridgeLine.Points.Count; index++)
            {
                var a = ridgeLine.Points[index - 1];
                var b = ridgeLine.Points[index];
                if (SegmentsIntersect(x1, z1, x2, z2, a.X, a.Z, b.X, b.Z))
                    count++;
            }
        }

        return count;
    }

    private static PropagationLandcoverClass SampleLandcover(PropagationTerrainMapSceneViewModel scene, double x, double z)
    {
        var columns = scene.Columns;
        var rows = scene.Rows;
        if (!scene.HasLandcover || columns <= 0 || rows <= 0)
            return PropagationLandcoverClass.BareGround;

        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        var normalizedX = ResolveCellCenteredIndex(x, minX, maxX, columns);
        var normalizedZ = ResolveCellCenteredIndex(z, minZ, maxZ, rows);
        var col = (int)Math.Round(normalizedX);
        var row = (int)Math.Round(normalizedZ);
        col = Math.Clamp(col, 0, columns - 1);
        row = Math.Clamp(row, 0, rows - 1);
        return scene.LandcoverSamples[(row * columns) + col];
    }

    private static double ResolveCellCenteredIndex(double coordinate, double min, double max, int samples)
    {
        if (samples <= 1)
            return 0d;

        var span = Math.Max(1d, max - min);
        return (((Math.Clamp(coordinate, min, max) - min) / span) * samples) - 0.5d;
    }

    private static void ResolveSceneBounds(
        PropagationTerrainMapSceneViewModel scene,
        out double minX,
        out double minZ,
        out double maxX,
        out double maxZ)
    {
        if (scene.MaxX > scene.MinX && scene.MaxZ > scene.MinZ)
        {
            minX = scene.MinX;
            minZ = scene.MinZ;
            maxX = scene.MaxX;
            maxZ = scene.MaxZ;
            return;
        }

        minX = 0d;
        minZ = 0d;
        maxX = scene.WidthM;
        maxZ = scene.HeightM;
    }

    private static bool SegmentsIntersect(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double dx,
        double dy)
    {
        var o1 = Orientation(ax, ay, bx, by, cx, cy);
        var o2 = Orientation(ax, ay, bx, by, dx, dy);
        var o3 = Orientation(cx, cy, dx, dy, ax, ay);
        var o4 = Orientation(cx, cy, dx, dy, bx, by);

        if ((o1 > 0 && o2 < 0 || o1 < 0 && o2 > 0) &&
            (o3 > 0 && o4 < 0 || o3 < 0 && o4 > 0))
        {
            return true;
        }

        return false;
    }

    private static double Orientation(double ax, double ay, double bx, double by, double cx, double cy)
    {
        return ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
    }

    private static double ResolveAntennaBonusDb(double antennaHeightM)
    {
        return Math.Clamp((antennaHeightM - 10d) * 0.35d, -2d, 8d);
    }

    private static double ResolveSpreadingFactorGainDb(string spreadingFactor)
    {
        if (string.IsNullOrWhiteSpace(spreadingFactor))
            return 0d;

        return spreadingFactor.Trim().ToUpperInvariant() switch
        {
            "SF7" => 0d,
            "SF8" => 1.5d,
            "SF9" => 3d,
            "SF10" => 4.5d,
            "SF11" => 6d,
            "SF12" => 7.5d,
            _ => 0d,
        };
    }

    private static double ResolveSensitivityDbm(string spreadingFactor)
    {
        return ParseSf(spreadingFactor) switch
        {
            <= 7 => -123d,
            8 => -126d,
            9 => -129d,
            10 => -132d,
            11 => -134.5d,
            _ => -137d,
        };
    }

    private static int ParseSf(string spreadingFactor)
    {
        if (string.IsNullOrWhiteSpace(spreadingFactor))
            return 10;

        var text = spreadingFactor.Trim();
        if (text.StartsWith("SF", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        return int.TryParse(text, out var sf) ? sf : 10;
    }

    private static string ResolveDominantReasonCode(
        bool isLineOfSight,
        double marginDb,
        int ridgeCrossings,
        double diffractionLossDb,
        double fresnelLossDb,
        double landcoverLossDb,
        double shadowLossDb,
        double reflectionLossDb,
        double environmentLossDb,
        double ridgePenaltyDb)
    {
        if (!double.IsFinite(marginDb))
            return "no_data";

        if (!isLineOfSight && (ridgeCrossings > 0 || diffractionLossDb >= Math.Max(fresnelLossDb, landcoverLossDb)))
            return "ridge_obstruction";

        var candidates = new (string Code, double LossDb)[]
        {
            ("diffraction", diffractionLossDb),
            ("fresnel_intrusion", fresnelLossDb),
            ("vegetation_clutter", landcoverLossDb),
            ("shadow_fading", shadowLossDb),
            ("reflection", reflectionLossDb),
            ("environment", environmentLossDb),
            ("ridge_obstruction", ridgePenaltyDb),
        };
        var dominant = candidates
            .OrderByDescending(candidate => candidate.LossDb)
            .FirstOrDefault();

        if (dominant.LossDb >= 1d)
            return dominant.Code;

        return "path_loss";
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static void AddLandcoverContribution(
        IDictionary<PropagationLandcoverClass, LandcoverContributionAccumulator> landcoverContributions,
        PropagationLandcoverClass landcoverClass,
        double lengthM,
        double lossDb)
    {
        if (!landcoverContributions.TryGetValue(landcoverClass, out var accumulator))
            accumulator = default;

        accumulator.LengthM += lengthM;
        accumulator.LossDb += lossDb;
        landcoverContributions[landcoverClass] = accumulator;
    }

    private readonly record struct PathLossEvaluation(
        bool IsLineOfSight,
        double DiffractionLossDb,
        double FresnelLossDb,
        double LandcoverLossDb,
        double MaxObstructionAboveLosM,
        double ObstructionDistanceM,
        double WorstClearanceRatio,
        double MinimumClearanceM,
        PropagationLandcoverClass RxLandcoverClass,
        double RxLandcoverInputCoefficientDbPerM,
        double RxLandcoverEffectiveCoefficientDbPerM,
        string DominantObstructionCode,
        IReadOnlyList<PropagationLandcoverSegmentContribution> LandcoverSegments);

    private struct LandcoverContributionAccumulator
    {
        public double LengthM;
        public double LossDb;
    }
}
