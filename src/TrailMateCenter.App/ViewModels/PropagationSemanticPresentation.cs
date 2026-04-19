using System;
using TrailMateCenter.Localization;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public enum PropagationCoverageStatus
{
    NoData = 0,
    Unreachable = 1,
    Marginal = 2,
    Reachable = 3,
    Strong = 4,
}

public enum PropagationLegendItemKind
{
    Fill = 0,
    Line = 1,
    Hatch = 2,
}

internal static class PropagationCoveragePresentation
{
    public const double LinkEdgeMarginDb = 0d;
    public const double StableMarginDb = 10d;
    public const double ReachableMarginDb = 4d;
    public const double StrongMarginDb = 14d;

    public static PropagationCoverageStatus ResolveStatus(double marginDb, bool isComputed)
    {
        if (!isComputed)
            return PropagationCoverageStatus.NoData;
        if (marginDb < LinkEdgeMarginDb)
            return PropagationCoverageStatus.Unreachable;
        if (marginDb < ReachableMarginDb)
            return PropagationCoverageStatus.Marginal;
        if (marginDb < StrongMarginDb)
            return PropagationCoverageStatus.Reachable;
        return PropagationCoverageStatus.Strong;
    }

    public static string ResolveLabel(PropagationCoverageStatus status)
    {
        return LocalizationService.Instance.GetString(status switch
        {
            PropagationCoverageStatus.NoData => "Ui.Propagation.CoverageStatus.NoData",
            PropagationCoverageStatus.Unreachable => "Ui.Propagation.CoverageStatus.Unreachable",
            PropagationCoverageStatus.Marginal => "Ui.Propagation.CoverageStatus.Marginal",
            PropagationCoverageStatus.Reachable => "Ui.Propagation.CoverageStatus.Reachable",
            _ => "Ui.Propagation.CoverageStatus.Strong",
        });
    }

    public static string ResolveLegendSummary(PropagationCoverageStatus status)
    {
        return LocalizationService.Instance.GetString(status switch
        {
            PropagationCoverageStatus.NoData => "Ui.Propagation.CoverageStatus.NoData.Summary",
            PropagationCoverageStatus.Unreachable => "Ui.Propagation.CoverageStatus.Unreachable.Summary",
            PropagationCoverageStatus.Marginal => "Ui.Propagation.CoverageStatus.Marginal.Summary",
            PropagationCoverageStatus.Reachable => "Ui.Propagation.CoverageStatus.Reachable.Summary",
            _ => "Ui.Propagation.CoverageStatus.Strong.Summary",
        });
    }

    public static string ResolveFillColorHex(PropagationCoverageStatus status)
    {
        return status switch
        {
            PropagationCoverageStatus.NoData => "#38424C",
            PropagationCoverageStatus.Unreachable => "#465C79",
            PropagationCoverageStatus.Marginal => "#B98435",
            PropagationCoverageStatus.Reachable => "#7DAF45",
            _ => "#4CB463",
        };
    }

    public static string ResolveSecondaryColorHex(PropagationCoverageStatus status)
    {
        return status switch
        {
            PropagationCoverageStatus.NoData => "#1F2730",
            PropagationCoverageStatus.Unreachable => "#243142",
            PropagationCoverageStatus.Marginal => "#6D4F1E",
            PropagationCoverageStatus.Reachable => "#446D2D",
            _ => "#28683A",
        };
    }

    public static string ResolveBoundaryColorHex(double thresholdDb)
    {
        return thresholdDb >= StableMarginDb ? "#B7F07D" : "#FFD56A";
    }
}

internal static class PropagationSemanticPresentation
{
    public static string ResolvePathState(string? token)
    {
        return LocalizationService.Instance.GetString(token?.Trim().ToUpperInvariant() switch
        {
            "LOS" => "Ui.Propagation.Semantics.Los",
            "NLOS" => "Ui.Propagation.Semantics.Nlos",
            _ => "Common.Unknown",
        });
    }

    public static string ResolveObstacleLabel(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return LocalizationService.Instance.GetString("Ui.Propagation.Semantics.ClearPath");

        if (TryResolveLandcoverToken(token, out var landcover))
            return PropagationLandcoverPresentation.ResolveLabel(landcover);

        return LocalizationService.Instance.GetString(token.Trim().ToLowerInvariant() switch
        {
            "ridge_obstruction" => "Ui.Propagation.Semantics.RidgeObstruction",
            "clear_path" => "Ui.Propagation.Semantics.ClearPath",
            _ => "Common.Unknown",
        });
    }

    public static string ResolveDominantReason(string? token)
    {
        return LocalizationService.Instance.GetString(token?.Trim().ToLowerInvariant() switch
        {
            "no_data" => "Ui.Propagation.Semantics.Reason.NoData",
            "ridge_obstruction" => "Ui.Propagation.Semantics.Reason.RidgeObstruction",
            "diffraction" => "Ui.Propagation.Semantics.Reason.Diffraction",
            "fresnel_intrusion" => "Ui.Propagation.Semantics.Reason.FresnelIntrusion",
            "vegetation_clutter" => "Ui.Propagation.Semantics.Reason.VegetationClutter",
            "shadow_fading" => "Ui.Propagation.Semantics.Reason.ShadowFading",
            "reflection" => "Ui.Propagation.Semantics.Reason.Reflection",
            "environment" => "Ui.Propagation.Semantics.Reason.Environment",
            "path_loss" => "Ui.Propagation.Semantics.Reason.PathLoss",
            _ => "Common.Unknown",
        });
    }

    public static string ResolveFresnelRisk(string? token)
    {
        return LocalizationService.Instance.GetString(token?.Trim().ToLowerInvariant() switch
        {
            "clear" => "Ui.Propagation.Semantics.Fresnel.Clear",
            "watch" => "Ui.Propagation.Semantics.Fresnel.Watch",
            "critical" => "Ui.Propagation.Semantics.Fresnel.Critical",
            _ => "Common.Unknown",
        });
    }

    public static string ResolveReflectionRisk(string? token)
    {
        return LocalizationService.Instance.GetString(token?.Trim().ToLowerInvariant() switch
        {
            "deep_fade" => "Ui.Propagation.Semantics.Reflection.DeepFade",
            "moderate" => "Ui.Propagation.Semantics.Reflection.Moderate",
            "constructive" => "Ui.Propagation.Semantics.Reflection.Constructive",
            _ => "Common.Unknown",
        });
    }

    public static string ResolveLandcoverToken(string? token)
    {
        return TryResolveLandcoverToken(token, out var landcover)
            ? PropagationLandcoverPresentation.ResolveLabel(landcover)
            : LocalizationService.Instance.GetString("Common.Unknown");
    }

    private static bool TryResolveLandcoverToken(string? token, out PropagationLandcoverClass landcoverClass)
    {
        landcoverClass = PropagationLandcoverClass.BareGround;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return Enum.TryParse(token.Trim(), ignoreCase: true, out landcoverClass);
    }
}
