using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailMateCenter.Localization;
using TrailMateCenter.Propagation.Engine;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public sealed class PropagationAnalyticalLayerItemViewModel
{
    public PropagationAnalyticalLayerItemViewModel(string title, string summary, string valueText, string accentColor)
    {
        Title = title;
        Summary = summary;
        ValueText = valueText;
        AccentColor = accentColor;
    }

    public string Title { get; }
    public string Summary { get; }
    public string ValueText { get; }
    public string AccentColor { get; }
}

public sealed class PropagationTopicCoverageItemViewModel
{
    public PropagationTopicCoverageItemViewModel(string topic, string status, string summary, string accentColor)
    {
        Topic = topic;
        Status = status;
        Summary = summary;
        AccentColor = accentColor;
    }

    public string Topic { get; }
    public string Status { get; }
    public string Summary { get; }
    public string AccentColor { get; }
}

public sealed class PropagationLegendItemViewModel
{
    public PropagationLegendItemViewModel(
        string label,
        string colorHex,
        PropagationLegendItemKind kind = PropagationLegendItemKind.Fill,
        string? secondaryColorHex = null,
        string? summary = null,
        double strokeThickness = 2d,
        bool isDashed = false)
    {
        Label = label;
        ColorHex = colorHex;
        Kind = kind;
        SecondaryColorHex = secondaryColorHex ?? colorHex;
        Summary = summary ?? string.Empty;
        StrokeThickness = strokeThickness;
        IsDashed = isDashed;
    }

    public string Label { get; }
    public string ColorHex { get; }
    public PropagationLegendItemKind Kind { get; }
    public string SecondaryColorHex { get; }
    public string Summary { get; }
    public double StrokeThickness { get; }
    public bool IsDashed { get; }
    public bool ShowFillSample => Kind == PropagationLegendItemKind.Fill;
    public bool ShowLineSample => Kind == PropagationLegendItemKind.Line;
    public bool ShowHatchSample => Kind == PropagationLegendItemKind.Hatch;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
}

public sealed class PropagationProbeMetricItemViewModel
{
    public PropagationProbeMetricItemViewModel(string label, string value, string accentColor)
    {
        Label = label;
        Value = value;
        AccentColor = accentColor;
    }

    public string Label { get; }
    public string Value { get; }
    public string AccentColor { get; }
}

public sealed class PropagationProfileSampleItemViewModel
{
    public PropagationProfileSampleItemViewModel(
        string distanceText,
        string terrainText,
        string losText,
        string clearanceText,
        string fresnelText,
        string surfaceText,
        string stateText,
        string accentColor)
    {
        DistanceText = distanceText;
        TerrainText = terrainText;
        LosText = losText;
        ClearanceText = clearanceText;
        FresnelText = fresnelText;
        SurfaceText = surfaceText;
        StateText = stateText;
        AccentColor = accentColor;
    }

    public string DistanceText { get; }
    public string TerrainText { get; }
    public string LosText { get; }
    public string ClearanceText { get; }
    public string FresnelText { get; }
    public string SurfaceText { get; }
    public string StateText { get; }
    public string AccentColor { get; }
}

public sealed class PropagationRelayPlanSummaryItemViewModel
{
    public PropagationRelayPlanSummaryItemViewModel(
        string planId,
        string scoreText,
        string gainText,
        string penaltyText,
        string sitesText,
        string explanation)
    {
        PlanId = planId;
        ScoreText = scoreText;
        GainText = gainText;
        PenaltyText = penaltyText;
        SitesText = sitesText;
        Explanation = explanation;
    }

    public string PlanId { get; }
    public string ScoreText { get; }
    public string GainText { get; }
    public string PenaltyText { get; }
    public string SitesText { get; }
    public string Explanation { get; }
}

public sealed class PropagationParameterAdjustmentItemViewModel
{
    public PropagationParameterAdjustmentItemViewModel(string name, string beforeText, string afterText, string deltaText)
    {
        Name = name;
        BeforeText = beforeText;
        AfterText = afterText;
        DeltaText = deltaText;
    }

    public string Name { get; }
    public string BeforeText { get; }
    public string AfterText { get; }
    public string DeltaText { get; }
}

public sealed class PropagationTerrainMapSceneViewModel
{
    public static PropagationTerrainMapSceneViewModel Empty { get; } = new();

    public string Crs { get; init; } = string.Empty;
    public bool UseViewportProjection { get; init; } = true;
    public double MinX { get; init; }
    public double MinZ { get; init; }
    public double MaxX { get; init; }
    public double MaxZ { get; init; }
    public double WidthM { get; init; }
    public double HeightM { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public double MinElevationM { get; init; }
    public double MaxElevationM { get; init; }
    public IReadOnlyList<double> ElevationSamples { get; init; } = Array.Empty<double>();
    public IReadOnlyList<PropagationLandcoverClass> LandcoverSamples { get; init; } = Array.Empty<PropagationLandcoverClass>();
    public IReadOnlyList<PropagationScenePolyline> ContourLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePolyline> RidgeLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePolyline> ProfileLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePolyline> LinkEdgeLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePolyline> StableMarginLines { get; init; } = Array.Empty<PropagationScenePolyline>();
    public IReadOnlyList<PropagationScenePoint> Sites { get; init; } = Array.Empty<PropagationScenePoint>();
    public PropagationScenePolyline? SelectedPathLine { get; init; }
    public PropagationScenePoint? PendingSite { get; init; }
    public string? SelectedSiteId { get; init; }
    public double? SelectedSiteCoverageRadiusM { get; init; }
    public IReadOnlyList<PropagationCoverageCellViewModel> SelectedSiteCoverageCells { get; init; } = Array.Empty<PropagationCoverageCellViewModel>();
    public IReadOnlyList<PropagationCoverageCellViewModel> ActiveLayerCoverageCells { get; init; } = Array.Empty<PropagationCoverageCellViewModel>();
    public IReadOnlyList<PropagationScenePoint> RelayCandidates { get; init; } = Array.Empty<PropagationScenePoint>();
    public IReadOnlyList<PropagationScenePoint> RelayRecommendations { get; init; } = Array.Empty<PropagationScenePoint>();
    public IReadOnlyList<PropagationScenePoint> ProfileObstacles { get; init; } = Array.Empty<PropagationScenePoint>();

    public bool HasTerrain => WidthM > 0 && HeightM > 0 && Columns > 0 && Rows > 0 && ElevationSamples.Count >= Columns * Rows;
    public bool HasLandcover => Columns > 0 && Rows > 0 && LandcoverSamples.Count >= Columns * Rows;
    public bool UsesLocalCoordinates => !string.IsNullOrWhiteSpace(Crs) && Crs.StartsWith("LOCAL_", StringComparison.OrdinalIgnoreCase);
}

public sealed class PropagationControlPanelViewModel : ObservableObject
{
    private readonly PropagationViewModel _root;

    public PropagationControlPanelViewModel(PropagationViewModel root)
    {
        _root = root;
        _root.PropertyChanged += OnRootPropertyChanged;
    }

    public double FrequencyMHz
    {
        get => _root.FrequencyMHz;
        set => _root.FrequencyMHz = value;
    }

    public double TxPowerDbm
    {
        get => _root.TxPowerDbm;
        set => _root.TxPowerDbm = value;
    }

    public string SpreadingFactor
    {
        get => _root.SpreadingFactor;
        set => _root.SpreadingFactor = value;
    }

    public double EnvironmentLossDb
    {
        get => _root.EnvironmentLossDb;
        set => _root.EnvironmentLossDb = value;
    }

    public double VegetationAlphaSparse
    {
        get => _root.VegetationAlphaSparse;
        set => _root.VegetationAlphaSparse = value;
    }

    public double VegetationAlphaDense
    {
        get => _root.VegetationAlphaDense;
        set => _root.VegetationAlphaDense = value;
    }

    public double ShadowSigmaDb
    {
        get => _root.ShadowSigmaDb;
        set => _root.ShadowSigmaDb = value;
    }

    public double ReflectionCoeff
    {
        get => _root.ReflectionCoeff;
        set => _root.ReflectionCoeff = value;
    }

    public bool UseMonteCarlo
    {
        get => _root.UseMonteCarlo;
        set => _root.UseMonteCarlo = value;
    }

    public int MonteCarloIterations
    {
        get => _root.MonteCarloIterations;
        set => _root.MonteCarloIterations = value;
    }

    public string SelectedOptimizationAlgorithm
    {
        get => _root.SelectedOptimizationAlgorithm;
        set => _root.SelectedOptimizationAlgorithm = value;
    }

    public string SelectedPresetName
    {
        get => _root.SelectedPresetName;
        set => _root.SelectedPresetName = value;
    }

    public string ScenarioSummary => $"{SelectedPresetName} | {FrequencyMHz:F0} MHz | {TxPowerDbm:F1} dBm | {SpreadingFactor}";
    public string ScenarioSiteSummary => ResolveScenarioSiteSummary();
    public string ScenarioSiteHint => LocalizationService.Instance.GetString("Ui.Propagation.ScenarioSites.Hint");
    public string ExecutionSummary => _root.SimulationStateText;
    public string DirtySummary => _root.IsParametersDirty
        ? LocalizationService.Instance.GetString("Ui.Propagation.ControlPanel.Dirty")
        : LocalizationService.Instance.GetString("Ui.Propagation.ControlPanel.Clean");
    public string AdvancedSummary => LocalizationService.Instance.Format(
        UseMonteCarlo ? "Ui.Propagation.ControlPanel.Advanced.Enabled" : "Ui.Propagation.ControlPanel.Advanced.Disabled",
        MonteCarloIterations,
        SelectedOptimizationAlgorithm);

    public IAsyncRelayCommand RunSimulationCommand => _root.RunSimulationCommand;
    public IAsyncRelayCommand PauseSimulationCommand => _root.PauseSimulationCommand;
    public IAsyncRelayCommand CancelSimulationCommand => _root.CancelSimulationCommand;
    public IAsyncRelayCommand OptimizeRelaysCommand => _root.OptimizeRelaysCommand;
    public IAsyncRelayCommand AnalyzeInterferenceCommand => _root.AnalyzeInterferenceCommand;
    public IAsyncRelayCommand RunMonteCarloCommand => _root.RunMonteCarloCommand;
    public IAsyncRelayCommand ExportResultCommand => _root.ExportResultCommand;
    public IRelayCommand ClearScenarioSitesCommand => new RelayCommand(_root.ClearScenarioSites);

    private string ResolveScenarioSiteSummary()
    {
        if (_root.ScenarioSites.Count > 0)
            return string.Join(" | ", _root.ScenarioSites.Select(site => site.Label));

        return LocalizationService.Instance.GetString("Ui.Propagation.ScenarioSites.Empty");
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }

    public void OnScenarioSitesChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    public void OnOverlayOpacityChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}

public sealed class PropagationMapWorkbenchViewModel : ObservableObject
{
    private readonly PropagationViewModel _root;
    private PropagationTerrainMapSceneViewModel? _terrainSceneCache;
    private double? _hoverSceneX;
    private double? _hoverSceneZ;
    private PropagationCoverageCellViewModel? _hoverCoverageCellCache;
    private bool _hoverCoverageCellResolved;

    public PropagationMapWorkbenchViewModel(PropagationViewModel root)
    {
        _root = root;
        _root.PropertyChanged += OnRootPropertyChanged;
    }

    public ObservableCollection<PropagationLayerOptionViewModel> LayerOptions => _root.LayerOptions;

    public PropagationLayerOptionViewModel? SelectedLayerOption
    {
        get => _root.SelectedLayerOption;
        set => _root.SelectedLayerOption = value;
    }

    public IEnumerable<PropagationAnalyticalLayerItemViewModel> AnalyticalLayers
    {
        get
        {
            var result = _root.GetSelectedResult();
            var scene = TerrainScene;
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.MeanCoverage.Title"),
                L("Ui.Propagation.LayerCard.MeanCoverage.Summary"),
                result is null ? "-- dBm" : $"{result.AnalysisOutputs.Link.DownlinkRssiDbm:F1} dBm",
                "#75E0A2");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Terrain.Title"),
                L("Ui.Propagation.LayerCard.Terrain.Summary"),
                result is null ? "--" : PropagationSemanticPresentation.ResolvePathState(result.AnalysisOutputs.Terrain.PathState),
                result is null ? "#9AA3AE" : result.AnalysisOutputs.Terrain.IsLineOfSight ? "#75E0A2" : "#FF7373");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Landcover.Title"),
                L("Ui.Propagation.LayerCard.Landcover.Summary"),
                ResolveLandcoverCompositionSummary(scene),
                "#5CCFA2");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Reliability.Title"),
                L("Ui.Propagation.LayerCard.Reliability.Summary"),
                result is null ? "--" : $"{result.AnalysisOutputs.Reliability.P95:F0}%",
                "#9AD94C");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Interference.Title"),
                L("Ui.Propagation.LayerCard.Interference.Summary"),
                result is null ? "-- dB" : $"{result.AnalysisOutputs.Network.SinrDb:F1} dB",
                "#FFB04C");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Relay.Title"),
                L("Ui.Propagation.LayerCard.Relay.Summary"),
                result is null ? "--" : $"{result.SceneGeometry.RelayCandidates.Count} candidates",
                "#66C6FF");
            yield return new PropagationAnalyticalLayerItemViewModel(
                L("Ui.Propagation.LayerCard.Calibration.Title"),
                L("Ui.Propagation.LayerCard.Calibration.Summary"),
                result is null ? "--" : $"{result.AnalysisOutputs.SpatialAlignment.AlignmentScore:F1} score",
                "#D8A8FF");
        }
    }

    public string WorkbenchTitle => _root.SelectedModeText;
    public string WorkbenchHint => _root.ModeHintText;
    public string LayerSummary => _root.SelectedLayerOption?.Label ?? L("Ui.Propagation.Workbench.NoLayerSelected");
    public string SelectedLayerKey => _root.SelectedLayerOption?.Key ?? string.Empty;
    public string LegendTitle => IsLandcoverLayerSelected
        ? L("Ui.Propagation.Legend.Landcover")
        : L("Ui.Propagation.Workbench.Legend");
    public string ProbeSummary => ResolveProbeSummary();
    public string ReliabilitySummary => ResolveReliabilitySummary();
    public string CoverageAreaSummary => ResolveCoverageAreaSummary();
    public string ModeOverlaySummary => ResolveModeOverlaySummary();
    public string WarningSummary => string.IsNullOrWhiteSpace(_root.ValidityWarningsText) ? "--" : _root.ValidityWarningsText;
    public string RidgeSummary => ResolveRidgeSummary();
    public string AlignmentSummary => ResolveAlignmentSummary();
    public PropagationTerrainMapSceneViewModel TerrainScene => _terrainSceneCache ??= ResolveTerrainScene();
    public bool ShowReferenceBasemap => true;
    public double TerrainOverlayOpacity => _root.TerrainOverlayOpacity;
    public bool IsLandcoverLayerSelected => string.Equals(SelectedLayerKey, "landcover", StringComparison.Ordinal);
    public bool HasHoverProbe => HoverCoverageCell is not null;
    public string HoverProbeTitle => HoverCoverageCell is null
        ? L("Ui.Propagation.Workbench.Hover.EmptyTitle")
        : F("Ui.Propagation.Workbench.Hover.Title", PropagationCoveragePresentation.ResolveLabel(HoverCoverageCell.Status));
    public string HoverProbeSubtitle => ResolveHoverProbeSubtitle();
    public string HoverProbeStatusText => HoverCoverageCell is null
        ? "--"
        : PropagationCoveragePresentation.ResolveLabel(HoverCoverageCell.Status);
    public string HoverProbeStatusColor => HoverCoverageCell is null
        ? "#9AA3AE"
        : PropagationCoveragePresentation.ResolveFillColorHex(HoverCoverageCell.Status);
    public string HoverProbePathSummary => ResolveHoverPathSummary();
    public string HoverProbeLandcoverSummary => ResolveHoverLandcoverSummary();
    public IEnumerable<PropagationProbeMetricItemViewModel> HoverLocalMetrics => ResolveHoverLocalMetrics();
    public IEnumerable<PropagationProbeMetricItemViewModel> HoverLossMetrics => ResolveHoverLossMetrics();
    public IEnumerable<PropagationLegendItemViewModel> LegendItems => ResolveLegendItems();

    private static string L(string key) => LocalizationService.Instance.GetString(key);
    private static string F(string key, params object[] args) => LocalizationService.Instance.Format(key, args);

    private PropagationCoverageCellViewModel? HoverCoverageCell
    {
        get
        {
            if (_hoverCoverageCellResolved)
                return _hoverCoverageCellCache;

            _hoverCoverageCellCache = ResolveHoverCoverageCell();
            _hoverCoverageCellResolved = true;
            return _hoverCoverageCellCache;
        }
    }

    private string ResolveProbeSummary()
    {
        if (HoverCoverageCell is { } hoverCell)
        {
            if (!hoverCell.IsComputed)
            {
                return F(
                    "Ui.Propagation.Workbench.Probe.CoverageNoData",
                    PropagationCoveragePresentation.ResolveLabel(hoverCell.Status),
                    PropagationSemanticPresentation.ResolveDominantReason(hoverCell.DominantReasonCode));
            }

            return F(
                "Ui.Propagation.Workbench.Probe.Coverage",
                PropagationCoveragePresentation.ResolveLabel(hoverCell.Status),
                hoverCell.MarginDb,
                hoverCell.ReceivedPowerDbm,
                PropagationSemanticPresentation.ResolveDominantReason(hoverCell.DominantReasonCode));
        }

        var result = _root.GetSelectedResult();
        if (result?.AnalysisOutputs.Terrain is { } terrain)
            return F(
                "Ui.Propagation.Workbench.Probe.Terrain",
                PropagationSemanticPresentation.ResolvePathState(terrain.PathState),
                PropagationSemanticPresentation.ResolveObstacleLabel(terrain.DominantObstructionLabel),
                terrain.DominantObstructionDistanceKm);

        if (!string.IsNullOrWhiteSpace(_root.SelectedNodeId))
            return F("Ui.Propagation.Workbench.Probe.Node", _root.SelectedNodeId, _root.SelectedPointX ?? 0, _root.SelectedPointY ?? 0);

        if (_root.SelectedPointX.HasValue && _root.SelectedPointY.HasValue)
            return F("Ui.Propagation.Workbench.Probe.Point", _root.SelectedPointX.Value, _root.SelectedPointY.Value);

        return L("Ui.Propagation.Workbench.Probe.Empty");
    }

    private string ResolveHoverProbeSubtitle()
    {
        if (HoverCoverageCell is not { } cell)
            return L("Ui.Propagation.Workbench.Hover.EmptyHint");

        if (!double.IsFinite(cell.DistanceM))
            return F("Ui.Propagation.Workbench.Probe.Point", cell.X, cell.Z);

        return F(
            "Ui.Propagation.Workbench.Hover.Subtitle",
            cell.X,
            cell.Z,
            cell.DistanceM);
    }

    private string ResolveHoverPathSummary()
    {
        if (HoverCoverageCell is not { } cell)
            return L("Ui.Propagation.Workbench.Hover.EmptyHint");

        return F(
            "Ui.Propagation.Workbench.Hover.PathSummary",
            cell.IsComputed ? PropagationSemanticPresentation.ResolvePathState(cell.IsLineOfSight ? "LOS" : "NLOS") : PropagationCoveragePresentation.ResolveLabel(PropagationCoverageStatus.NoData),
            PropagationSemanticPresentation.ResolveObstacleLabel(cell.DominantObstructionCode),
            cell.RidgeCrossings,
            PropagationSemanticPresentation.ResolveDominantReason(cell.DominantReasonCode));
    }

    private string ResolveHoverLandcoverSummary()
    {
        if (HoverCoverageCell is not { } cell)
            return "--";

        return F(
            "Ui.Propagation.Workbench.Hover.LandcoverSummary",
            PropagationLandcoverPresentation.ResolveLabel(cell.LandcoverClass),
            cell.LandcoverInputCoefficientDbPerM,
            cell.LandcoverEffectiveCoefficientDbPerM);
    }

    private IEnumerable<PropagationProbeMetricItemViewModel> ResolveHoverLocalMetrics()
    {
        if (HoverCoverageCell is not { } cell)
            yield break;

        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.RxPower", FormatDbm(cell.ReceivedPowerDbm), HoverProbeStatusColor);
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Threshold", FormatDbm(cell.ThresholdDbm), "#A5C7FF");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Margin", FormatDb(cell.MarginDb), HoverProbeStatusColor);
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Elevation", FormatMeters(cell.ElevationM), "#D7C195");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Fresnel", FormatRatio(cell.FresnelClearanceRatio), "#FFB04C");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Clearance", FormatMeters(cell.MinimumClearanceM), "#FFCF48");
    }

    private IEnumerable<PropagationProbeMetricItemViewModel> ResolveHoverLossMetrics()
    {
        if (HoverCoverageCell is not { } cell)
            yield break;

        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Fspl", FormatDb(cell.FsplDb), "#9AD94C");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Diffraction", FormatDb(cell.DiffractionLossDb), "#FF8E6B");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.FresnelLoss", FormatDb(cell.FresnelLossDb), "#F7C15A");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.LandcoverLoss", FormatDb(cell.LandcoverLossDb), PropagationLandcoverPresentation.ResolveAccentColorHex(cell.LandcoverClass));
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Shadow", FormatDb(cell.ShadowLossDb), "#B0A7FF");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Reflection", FormatDb(cell.ReflectionLossDb), "#66C6FF");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Environment", FormatDb(cell.EnvironmentLossDb), "#9AA3AE");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.Ridge", FormatDb(cell.RidgePenaltyDb), "#6BD6FF");
        yield return CreateMetric("Ui.Propagation.Workbench.Hover.Metric.TotalLoss", FormatDb(cell.TotalLossDb), "#FFFFFF");
    }

    private string ResolveReliabilitySummary()
    {
        var result = _root.GetSelectedResult();
        return result is null
            ? L("Ui.Propagation.Workbench.Reliability.Empty")
            : F("Ui.Propagation.Workbench.Reliability.Value", result.AnalysisOutputs.Reliability.P95, result.AnalysisOutputs.Reliability.P80);
    }

    private string ResolveCoverageAreaSummary()
    {
        var result = _root.GetSelectedResult();
        return result is null
            ? L("Ui.Propagation.Workbench.CoverageArea.Empty")
            : F("Ui.Propagation.Workbench.CoverageArea.Value", result.AnalysisOutputs.CoverageProbability.AreaP95Km2, result.AnalysisOutputs.CoverageProbability.AreaP80Km2);
    }

    private string ResolveModeOverlaySummary()
    {
        return _root.SelectedPropagationMode switch
        {
            PropagationMode.CoverageMap => L("Ui.Propagation.Workbench.ModeOverlay.Coverage"),
            PropagationMode.InterferenceAnalysis => L("Ui.Propagation.Workbench.ModeOverlay.Interference"),
            PropagationMode.RelayOptimization => L("Ui.Propagation.Workbench.ModeOverlay.Relay"),
            PropagationMode.AdvancedModeling => L("Ui.Propagation.Workbench.ModeOverlay.Advanced"),
            PropagationMode.CalibrationValidation => L("Ui.Propagation.Workbench.ModeOverlay.Calibration"),
            _ => L("Ui.Propagation.Workbench.ModeOverlay.Default")
        };
    }

    private string ResolveRidgeSummary()
    {
        var result = _root.GetSelectedResult();
        return result is null
            ? L("Ui.Propagation.Workbench.Ridge.Empty")
            : F("Ui.Propagation.Workbench.Ridge.Value", result.SceneGeometry.RidgeLines.Count, result.SceneGeometry.RelayCandidates.Count, result.SceneGeometry.RelayRecommendations.Count);
    }

    private string ResolveAlignmentSummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return L("Ui.Propagation.Workbench.Alignment.Empty");

        var alignment = result.AnalysisOutputs.SpatialAlignment;
        return F("Ui.Propagation.Workbench.Alignment.Value", alignment.Status, alignment.TargetCrs, alignment.HorizontalOffsetM);
    }

    private IEnumerable<PropagationLegendItemViewModel> ResolveLegendItems()
    {
        if (IsLandcoverLayerSelected)
        {
            yield return new PropagationLegendItemViewModel(
                PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.BareGround),
                PropagationLandcoverPresentation.ResolveAccentColorHex(PropagationLandcoverClass.BareGround),
                summary: L("Ui.Propagation.Legend.Landcover.BareGround"));
            yield return new PropagationLegendItemViewModel(
                PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.SparseForest),
                PropagationLandcoverPresentation.ResolveAccentColorHex(PropagationLandcoverClass.SparseForest),
                summary: L("Ui.Propagation.Legend.Landcover.SparseForest"));
            yield return new PropagationLegendItemViewModel(
                PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.DenseForest),
                PropagationLandcoverPresentation.ResolveAccentColorHex(PropagationLandcoverClass.DenseForest),
                summary: L("Ui.Propagation.Legend.Landcover.DenseForest"));
            yield return new PropagationLegendItemViewModel(
                PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.Water),
                PropagationLandcoverPresentation.ResolveAccentColorHex(PropagationLandcoverClass.Water),
                summary: L("Ui.Propagation.Legend.Landcover.Water"));
            yield break;
        }

        foreach (var status in new[]
                 {
                     PropagationCoverageStatus.NoData,
                     PropagationCoverageStatus.Unreachable,
                     PropagationCoverageStatus.Marginal,
                     PropagationCoverageStatus.Reachable,
                     PropagationCoverageStatus.Strong,
                 })
        {
            yield return new PropagationLegendItemViewModel(
                PropagationCoveragePresentation.ResolveLabel(status),
                PropagationCoveragePresentation.ResolveFillColorHex(status),
                status == PropagationCoverageStatus.NoData ? PropagationLegendItemKind.Hatch : PropagationLegendItemKind.Fill,
                PropagationCoveragePresentation.ResolveSecondaryColorHex(status),
                PropagationCoveragePresentation.ResolveLegendSummary(status));
        }

        yield return new PropagationLegendItemViewModel(
            L("Ui.Propagation.Legend.Line.LinkEdge"),
            PropagationCoveragePresentation.ResolveBoundaryColorHex(PropagationCoveragePresentation.LinkEdgeMarginDb),
            PropagationLegendItemKind.Line,
            summary: L("Ui.Propagation.Legend.Line.LinkEdge.Summary"));
        yield return new PropagationLegendItemViewModel(
            L("Ui.Propagation.Legend.Line.Stable"),
            PropagationCoveragePresentation.ResolveBoundaryColorHex(PropagationCoveragePresentation.StableMarginDb),
            PropagationLegendItemKind.Line,
            summary: L("Ui.Propagation.Legend.Line.Stable.Summary"));
        yield return new PropagationLegendItemViewModel(
            L("Ui.Propagation.Legend.Line.Ridge"),
            "#6BD6FF",
            PropagationLegendItemKind.Line,
            summary: L("Ui.Propagation.Legend.Line.Ridge.Summary"));
        yield return new PropagationLegendItemViewModel(
            L("Ui.Propagation.Legend.Line.SelectedPath"),
            "#4AA3FF",
            PropagationLegendItemKind.Line,
            summary: L("Ui.Propagation.Legend.Line.SelectedPath.Summary"),
            isDashed: true);
    }

    private static string ResolveLandcoverCompositionSummary(PropagationTerrainMapSceneViewModel scene)
    {
        if (!scene.HasLandcover || scene.LandcoverSamples.Count == 0)
            return "--";

        var total = scene.LandcoverSamples.Count;
        var denseShare = ResolveLandcoverShare(scene.LandcoverSamples, total, PropagationLandcoverClass.DenseForest);
        var sparseShare = ResolveLandcoverShare(scene.LandcoverSamples, total, PropagationLandcoverClass.SparseForest);
        var waterShare = ResolveLandcoverShare(scene.LandcoverSamples, total, PropagationLandcoverClass.Water);

        return $"{PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.DenseForest)} {denseShare:F0}% | " +
               $"{PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.SparseForest)} {sparseShare:F0}% | " +
               $"{PropagationLandcoverPresentation.ResolveLabel(PropagationLandcoverClass.Water)} {waterShare:F0}%";
    }

    private static double ResolveLandcoverShare(
        IReadOnlyList<PropagationLandcoverClass> landcoverSamples,
        int total,
        PropagationLandcoverClass landcoverClass)
    {
        if (total <= 0)
            return 0d;

        var count = landcoverSamples.Count(sample => sample == landcoverClass);
        return (count * 100d) / total;
    }

    private PropagationTerrainMapSceneViewModel ResolveTerrainScene()
    {
        var selectedSite = _root.ScenarioSites.FirstOrDefault(site => string.Equals(site.Id, _root.SelectedScenarioSiteId, StringComparison.Ordinal));
        var result = _root.GetSelectedResult();
        if (result is null || _root.IsParametersDirty)
        {
            return BuildEditableSceneWithoutPreview();
        }

        var terrainMap = result.AnalysisOutputs.TerrainMap;
        var sites = (_root.PreferScenarioSiteScene || _root.ScenarioSites.Count > 0)
            ? _root.ScenarioSites.Select(site => new PropagationScenePoint
            {
                Id = site.Id,
                Label = site.Label,
                ColorHex = site.ColorHex,
                X = site.X,
                Z = site.Z,
                Y = site.ElevationM,
            }).ToArray()
            : terrainMap.Sites;

        var baseScene = new PropagationTerrainMapSceneViewModel
        {
            Crs = terrainMap.Crs,
            UseViewportProjection = true,
            MinX = terrainMap.MinX,
            MinZ = terrainMap.MinZ,
            MaxX = terrainMap.MaxX,
            MaxZ = terrainMap.MaxZ,
            WidthM = terrainMap.WidthM,
            HeightM = terrainMap.HeightM,
            Columns = terrainMap.Columns,
            Rows = terrainMap.Rows,
            MinElevationM = terrainMap.MinElevationM,
            MaxElevationM = terrainMap.MaxElevationM,
            ElevationSamples = terrainMap.ElevationSamples,
            LandcoverSamples = terrainMap.LandcoverSamples,
            ContourLines = terrainMap.ContourLines,
            RidgeLines = result.SceneGeometry.RidgeLines,
            ProfileLines = result.SceneGeometry.ProfileLines,
            Sites = sites,
            PendingSite = _root.PendingScenarioSitePoint,
            SelectedSiteId = _root.SelectedScenarioSiteId,
            SelectedSiteCoverageRadiusM = _root.SelectedScenarioSiteCoverageRadiusM,
            RelayCandidates = result.SceneGeometry.RelayCandidates,
            RelayRecommendations = result.SceneGeometry.RelayRecommendations,
            ProfileObstacles = result.SceneGeometry.ProfileObstacles,
        };

        var selectedSiteCoverageCells = selectedSite is null
            ? Array.Empty<PropagationCoverageCellViewModel>()
            : PropagationCoveragePreviewBuilder.Build(
                baseScene,
                selectedSite,
                _root.FrequencyMHz,
                _root.TxPowerDbm,
                _root.SpreadingFactor,
                _root.VegetationAlphaSparse,
                _root.VegetationAlphaDense,
                _root.EnvironmentLossDb,
                _root.ShadowSigmaDb,
                _root.ReflectionCoeff).ToArray();
        var activeLayerCoverageCells = ResolveActiveLayerCoverageCells(baseScene);
        var guideCells = ResolveGuideSourceCells(selectedSiteCoverageCells, activeLayerCoverageCells);
        var linkEdgeLines = guideCells.Count == 0
            ? Array.Empty<PropagationScenePolyline>()
            : PropagationCoverageGuideBuilder.BuildMarginIsolines(
                guideCells,
                baseScene.Columns,
                baseScene.Rows,
                PropagationCoveragePresentation.LinkEdgeMarginDb);
        var stableMarginLines = guideCells.Count == 0
            ? Array.Empty<PropagationScenePolyline>()
            : PropagationCoverageGuideBuilder.BuildMarginIsolines(
                guideCells,
                baseScene.Columns,
                baseScene.Rows,
                PropagationCoveragePresentation.StableMarginDb);
        var selectedPathLine = ResolveSelectedPathLine(baseScene);

        return new PropagationTerrainMapSceneViewModel
        {
            Crs = baseScene.Crs,
            UseViewportProjection = baseScene.UseViewportProjection,
            MinX = baseScene.MinX,
            MinZ = baseScene.MinZ,
            MaxX = baseScene.MaxX,
            MaxZ = baseScene.MaxZ,
            WidthM = baseScene.WidthM,
            HeightM = baseScene.HeightM,
            Columns = baseScene.Columns,
            Rows = baseScene.Rows,
            MinElevationM = baseScene.MinElevationM,
            MaxElevationM = baseScene.MaxElevationM,
            ElevationSamples = baseScene.ElevationSamples,
            LandcoverSamples = baseScene.LandcoverSamples,
            ContourLines = baseScene.ContourLines,
            RidgeLines = baseScene.RidgeLines,
            ProfileLines = baseScene.ProfileLines,
            LinkEdgeLines = linkEdgeLines,
            StableMarginLines = stableMarginLines,
            Sites = baseScene.Sites,
            SelectedPathLine = selectedPathLine,
            PendingSite = baseScene.PendingSite,
            SelectedSiteId = baseScene.SelectedSiteId,
            SelectedSiteCoverageRadiusM = baseScene.SelectedSiteCoverageRadiusM,
            SelectedSiteCoverageCells = selectedSiteCoverageCells,
            ActiveLayerCoverageCells = activeLayerCoverageCells,
            RelayCandidates = baseScene.RelayCandidates,
            RelayRecommendations = baseScene.RelayRecommendations,
            ProfileObstacles = baseScene.ProfileObstacles,
        };
    }

    private PropagationTerrainMapSceneViewModel BuildEditableSceneWithoutPreview()
    {
        var sites = _root.ScenarioSites.Select(site => new PropagationScenePoint
        {
            Id = site.Id,
            Label = site.Label,
            ColorHex = site.ColorHex,
            X = site.X,
            Z = site.Z,
            Y = site.ElevationM,
        }).ToArray();

        var hasViewportBounds = _root.TryGetViewportWorldBounds(out var viewportBounds);
        var minX = hasViewportBounds ? viewportBounds.MinX : 0d;
        var minZ = hasViewportBounds ? viewportBounds.MinZ : 0d;
        var maxX = hasViewportBounds ? viewportBounds.MaxX : 0d;
        var maxZ = hasViewportBounds ? viewportBounds.MaxZ : 0d;

        return new PropagationTerrainMapSceneViewModel
        {
            Crs = "EPSG:3857",
            UseViewportProjection = true,
            MinX = minX,
            MinZ = minZ,
            MaxX = maxX,
            MaxZ = maxZ,
            WidthM = Math.Max(0d, maxX - minX),
            HeightM = Math.Max(0d, maxZ - minZ),
            Sites = sites,
            PendingSite = _root.PendingScenarioSitePoint,
            SelectedSiteId = _root.SelectedScenarioSiteId,
            SelectedSiteCoverageRadiusM = _root.SelectedScenarioSiteCoverageRadiusM,
        };
    }

    private IReadOnlyList<PropagationCoverageCellViewModel> ResolveActiveLayerCoverageCells(PropagationTerrainMapSceneViewModel scene)
    {
        if (!scene.HasTerrain)
            return Array.Empty<PropagationCoverageCellViewModel>();

        if (!string.Equals(SelectedLayerKey, "coverage_mean", StringComparison.Ordinal) &&
            !string.Equals(SelectedLayerKey, "link_margin", StringComparison.Ordinal))
        {
            return Array.Empty<PropagationCoverageCellViewModel>();
        }

        var txSite = _root.ScenarioSites.FirstOrDefault(site => site.Role == PropagationSiteRole.BaseStation)
                     ?? _root.ScenarioSites.FirstOrDefault();
        if (txSite is null)
        {
            return PropagationCoveragePreviewBuilder.BuildNoData(
                    scene,
                    _root.VegetationAlphaSparse,
                    _root.VegetationAlphaDense)
                .ToArray();
        }

        return PropagationCoveragePreviewBuilder.Build(
                scene,
                txSite,
                _root.FrequencyMHz,
                _root.TxPowerDbm,
                _root.SpreadingFactor,
                _root.VegetationAlphaSparse,
                _root.VegetationAlphaDense,
                _root.EnvironmentLossDb,
                _root.ShadowSigmaDb,
                _root.ReflectionCoeff)
            .ToArray();
    }

    public void UpdateHoverProbe(double x, double z)
    {
        var previousCell = HoverCoverageCell;
        _hoverSceneX = x;
        _hoverSceneZ = z;
        _hoverCoverageCellResolved = false;
        var nextCell = HoverCoverageCell;
        if (AreSameCoverageCell(previousCell, nextCell))
            return;

        OnPropertyChanged(string.Empty);
    }

    public void ClearHoverProbe()
    {
        if (!_hoverSceneX.HasValue && !_hoverSceneZ.HasValue && HoverCoverageCell is null)
            return;

        _hoverSceneX = null;
        _hoverSceneZ = null;
        _hoverCoverageCellCache = null;
        _hoverCoverageCellResolved = true;
        OnPropertyChanged(string.Empty);
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ShouldInvalidateTerrainSceneCache(e.PropertyName))
        {
            _terrainSceneCache = null;
            _hoverCoverageCellResolved = false;
        }
        OnPropertyChanged(string.Empty);
    }

    public void OnScenarioSitesChanged()
    {
        _terrainSceneCache = null;
        _hoverCoverageCellResolved = false;
        OnPropertyChanged(string.Empty);
    }

    public void OnOverlayOpacityChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    private static bool ShouldInvalidateTerrainSceneCache(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return true;

        return propertyName switch
        {
            nameof(PropagationViewModel.SelectedTask) => true,
            nameof(PropagationViewModel.SelectedRunIdText) => true,
            nameof(PropagationViewModel.SelectedScenarioSiteId) => true,
            nameof(PropagationViewModel.SelectedLayerOption) => true,
            nameof(PropagationViewModel.IsParametersDirty) => true,
            nameof(PropagationViewModel.SelectedPropagationMode) => true,
            nameof(PropagationViewModel.SelectedPresetName) => true,
            nameof(PropagationViewModel.ScenarioSites) => true,
            nameof(PropagationViewModel.FrequencyMHz) => true,
            nameof(PropagationViewModel.TxPowerDbm) => true,
            nameof(PropagationViewModel.SpreadingFactor) => true,
            nameof(PropagationViewModel.EnvironmentLossDb) => true,
            nameof(PropagationViewModel.VegetationAlphaSparse) => true,
            nameof(PropagationViewModel.VegetationAlphaDense) => true,
            nameof(PropagationViewModel.ShadowSigmaDb) => true,
            nameof(PropagationViewModel.ReflectionCoeff) => true,
            _ => false,
        };
    }

    private PropagationCoverageCellViewModel? ResolveHoverCoverageCell()
    {
        if (!_hoverSceneX.HasValue || !_hoverSceneZ.HasValue)
            return null;

        var scene = TerrainScene;
        var cells = ResolveHoverCoverageSourceCells(scene);
        if (cells.Count < scene.Columns * scene.Rows || scene.Columns <= 0 || scene.Rows <= 0)
            return null;

        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        if (maxX <= minX || maxZ <= minZ)
            return null;

        var col = Math.Clamp((int)Math.Floor(((_hoverSceneX.Value - minX) / Math.Max(1d, maxX - minX)) * scene.Columns), 0, scene.Columns - 1);
        var row = Math.Clamp((int)Math.Floor(((_hoverSceneZ.Value - minZ) / Math.Max(1d, maxZ - minZ)) * scene.Rows), 0, scene.Rows - 1);
        return cells[(row * scene.Columns) + col];
    }

    private IReadOnlyList<PropagationCoverageCellViewModel> ResolveHoverCoverageSourceCells(PropagationTerrainMapSceneViewModel scene)
    {
        if (!IsLandcoverLayerSelected && scene.SelectedSiteCoverageCells.Count > 0)
            return scene.SelectedSiteCoverageCells;
        if (scene.ActiveLayerCoverageCells.Count > 0)
            return scene.ActiveLayerCoverageCells;
        return scene.SelectedSiteCoverageCells;
    }

    private static IReadOnlyList<PropagationCoverageCellViewModel> ResolveGuideSourceCells(
        IReadOnlyList<PropagationCoverageCellViewModel> selectedSiteCoverageCells,
        IReadOnlyList<PropagationCoverageCellViewModel> activeLayerCoverageCells)
    {
        return selectedSiteCoverageCells.Count > 0 ? selectedSiteCoverageCells : activeLayerCoverageCells;
    }

    private PropagationScenePolyline? ResolveSelectedPathLine(PropagationTerrainMapSceneViewModel scene)
    {
        var selectedSite = _root.ScenarioSites.FirstOrDefault(site => string.Equals(site.Id, _root.SelectedScenarioSiteId, StringComparison.Ordinal));
        if (selectedSite is null)
            return null;

        PropagationSiteInput? txSite;
        PropagationSiteInput? rxSite;
        if (selectedSite.Role == PropagationSiteRole.TargetNode)
        {
            txSite = _root.ScenarioSites.FirstOrDefault(site => site.Role == PropagationSiteRole.BaseStation);
            rxSite = selectedSite;
        }
        else
        {
            var target = _root.ScenarioSites.Where(site => site.Role == PropagationSiteRole.TargetNode).Take(1).FirstOrDefault();
            txSite = selectedSite;
            rxSite = target;
        }

        if (txSite is null || rxSite is null || string.Equals(txSite.Id, rxSite.Id, StringComparison.Ordinal))
            return null;

        return new PropagationScenePolyline
        {
            Points = new[]
            {
                new PropagationScenePolylinePoint { X = txSite.X, Z = txSite.Z },
                new PropagationScenePolylinePoint { X = rxSite.X, Z = rxSite.Z },
            },
        };
    }

    private static bool AreSameCoverageCell(PropagationCoverageCellViewModel? previous, PropagationCoverageCellViewModel? next)
    {
        if (previous is null && next is null)
            return true;
        if (previous is null || next is null)
            return false;

        return previous.Row == next.Row &&
               previous.Column == next.Column &&
               previous.IsComputed == next.IsComputed &&
               previous.Status == next.Status;
    }

    private static PropagationProbeMetricItemViewModel CreateMetric(string labelKey, string value, string accentColor)
    {
        return new PropagationProbeMetricItemViewModel(LocalizationService.Instance.GetString(labelKey), value, accentColor);
    }

    private static string FormatDbm(double value) => double.IsFinite(value) ? $"{value:F1} dBm" : "--";
    private static string FormatDb(double value) => double.IsFinite(value) ? $"{value:+0.0;-0.0;0.0} dB" : "--";
    private static string FormatMeters(double value) => double.IsFinite(value) ? $"{value:F1} m" : "--";
    private static string FormatRatio(double value) => double.IsFinite(value) ? $"{value:F2}" : "--";

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
}

public sealed class PropagationAnalysisPanelViewModel : ObservableObject
{
    private readonly PropagationViewModel _root;
    private PropagationSelectedPathPreview? _selectedPathPreviewCache;
    private bool _selectedPathPreviewResolved;

    public PropagationAnalysisPanelViewModel(PropagationViewModel root)
    {
        _root = root;
        _root.PropertyChanged += OnRootPropertyChanged;
    }

    public double? DownlinkRssiDbm => _root.DownlinkRssiDbm;
    public double? UplinkRssiDbm => _root.UplinkRssiDbm;
    public double? DownlinkMarginDb => _root.DownlinkMarginDb;
    public double? UplinkMarginDb => _root.UplinkMarginDb;
    public bool? IsLinkFeasible => _root.IsLinkFeasible;
    public double? Reliability95 => _root.Reliability95;
    public double? Reliability80 => _root.Reliability80;
    public double? Coverage95AreaKm2 => _root.Coverage95AreaKm2;
    public double? Coverage80AreaKm2 => _root.Coverage80AreaKm2;
    public double? FsplDb => _root.FsplDb;
    public double? DiffractionLossDb => _root.DiffractionLossDb;
    public double? VegetationLossDb => _root.VegetationLossDb;
    public double? ReflectionLossDb => _root.ReflectionLossDb;
    public double? ShadowLossDb => _root.ShadowLossDb;
    public double? SinrDb => _root.SinrDb;
    public double? ConflictRate => _root.ConflictRate;
    public int? MaxCapacity => _root.MaxCapacity;
    public bool HasSelectedLandcoverPath => SelectedPathPreview is not null;
    public string SelectedLandcoverPathRoute => ResolveSelectedLandcoverPathRoute();
    public string SelectedLandcoverPathSummary => ResolveSelectedLandcoverPathSummary();
    public string SelectedLandcoverPathHint => ResolveSelectedLandcoverPathHint();
    public IEnumerable<PropagationAnalyticalLayerItemViewModel> SelectedLandcoverPathItems => ResolveSelectedLandcoverPathItems();
    public string AssumptionFlagsText => LocalizationService.Instance.Format("Ui.Propagation.Assumptions", _root.AssumptionFlagsText);
    public string ValidityWarningsText => LocalizationService.Instance.Format("Ui.Propagation.Warnings", _root.ValidityWarningsText);
    public string OutputLayerUrisText => LocalizationService.Instance.Format("Ui.Propagation.Layers", _root.OutputLayerUrisText);

    public string TerrainSummary
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                return "--";

            var terrain = result.AnalysisOutputs.Terrain;
            return LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.TerrainSummary",
                PropagationSemanticPresentation.ResolvePathState(terrain.PathState),
                PropagationSemanticPresentation.ResolveObstacleLabel(terrain.DominantObstructionLabel),
                terrain.ObstructionAboveLosM);
        }
    }

    public string FresnelSummary
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                return "--";

            var fresnel = result.AnalysisOutputs.Fresnel;
            return LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.FresnelSummary",
                fresnel.ClearanceRatio,
                fresnel.MinimumClearanceM,
                fresnel.AdditionalLossDb,
                PropagationSemanticPresentation.ResolveFresnelRisk(fresnel.RiskLevel));
        }
    }

    public string ReflectionSummary
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                return "--";

            var reflection = result.AnalysisOutputs.Reflection;
            return LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.ReflectionSummary",
                PropagationSemanticPresentation.ResolveReflectionRisk(reflection.MultipathRisk),
                reflection.RelativeGainDb,
                reflection.ExcessDelayNs);
        }
    }

    public string NetworkLoadSummary
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                return "--";

            var network = result.AnalysisOutputs.Network;
            return LocalizationService.Instance.Format("Ui.Propagation.Analysis.NetworkLoadSummary", network.AlohaLoadPercent, network.ChannelOccupancyPercent, network.AirtimeMs);
        }
    }

    public string OptimizationSummary
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                return "--";

            var optimization = result.AnalysisOutputs.Optimization;
            return LocalizationService.Instance.Format("Ui.Propagation.Analysis.OptimizationSummary", optimization.Algorithm, optimization.CandidateCount, optimization.RecommendedPlanId);
        }
    }

    public IEnumerable<PropagationRelayPlanSummaryItemViewModel> RelayPlans
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                yield break;

            foreach (var plan in result.AnalysisOutputs.Optimization.TopPlans)
            {
                yield return new PropagationRelayPlanSummaryItemViewModel(
                    plan.PlanId,
                    LocalizationService.Instance.Format("Ui.Propagation.Analysis.PlanScore", plan.Score),
                    LocalizationService.Instance.Format("Ui.Propagation.Analysis.PlanGain", plan.CoverageGain, plan.ReliabilityGain),
                    LocalizationService.Instance.Format("Ui.Propagation.Analysis.PlanPenalty", plan.BlindAreaPenalty, plan.InterferencePenalty, plan.CostPenalty),
                    plan.SiteIds.Count == 0 ? "--" : string.Join(", ", plan.SiteIds),
                    string.IsNullOrWhiteSpace(plan.Explanation) ? LocalizationService.Instance.GetString("Ui.Propagation.Analysis.NoExplanation") : plan.Explanation);
            }
        }
    }

    public IEnumerable<PropagationTopicCoverageItemViewModel> TopicCoverage
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                yield break;

            yield return CreateTopic("01 FSPL / Link Budget", "Ready", $"FSPL {result.AnalysisOutputs.LossBreakdown.FsplDb:F1} dB with explicit total-loss accounting.", "#75E0A2");
            yield return CreateTopic(
                "02 LOS / Terrain",
                "Ready",
                $"{PropagationSemanticPresentation.ResolvePathState(result.AnalysisOutputs.Terrain.PathState)} | {PropagationSemanticPresentation.ResolveObstacleLabel(result.AnalysisOutputs.Terrain.DominantObstructionLabel)}.",
                "#75E0A2");
            yield return CreateTopic("03 Knife-edge Diffraction", "Ready", $"v={result.AnalysisOutputs.Profile.MainObstacle.V:F2}, Ld={result.AnalysisOutputs.Profile.MainObstacle.LdDb:F1} dB.", "#75E0A2");
            yield return CreateTopic(
                "04 Fresnel Clearance",
                "Ready",
                $"ratio {result.AnalysisOutputs.Fresnel.ClearanceRatio:F2}, risk {PropagationSemanticPresentation.ResolveFresnelRisk(result.AnalysisOutputs.Fresnel.RiskLevel)}.",
                "#75E0A2");
            yield return CreateTopic("05 Vegetation Attenuation", "Ready", $"{result.AnalysisOutputs.LossBreakdown.VegetationDb:F1} dB accumulated along profile segments.", "#75E0A2");
            yield return CreateTopic("06 Shadow Fading", "Ready", $"{result.AnalysisOutputs.LossBreakdown.ShadowDb:F1} dB with sigma-driven confidence note.", "#75E0A2");
            yield return CreateTopic("07 Bidirectional Margin", "Ready", $"DL {result.AnalysisOutputs.Link.DownlinkMarginDb:F1} dB / UL {result.AnalysisOutputs.Link.UplinkMarginDb:F1} dB.", "#75E0A2");
            yield return CreateTopic("08 Coverage Probability", "Ready", $"A95 {result.AnalysisOutputs.CoverageProbability.AreaP95Km2:F2} km2, A80 {result.AnalysisOutputs.CoverageProbability.AreaP80Km2:F2} km2.", "#75E0A2");
            yield return CreateTopic("09 SINR / Interference", "Ready", $"SINR {result.AnalysisOutputs.Network.SinrDb:F1} dB with conflict {result.AnalysisOutputs.Network.ConflictRate:F1}%.", "#75E0A2");
            yield return CreateTopic("10 ALOHA Capacity", "Ready", $"Capacity {result.AnalysisOutputs.Network.MaxCapacityNodes} nodes, load {result.AnalysisOutputs.Network.AlohaLoadPercent:F0}%.", "#75E0A2");
            yield return CreateTopic("11 Ridge Detection", "Ready", $"{result.SceneGeometry.RidgeLines.Count} ridge lines and {result.SceneGeometry.RelayCandidates.Count} relay candidates.", "#75E0A2");
            yield return CreateTopic("12 Relay Optimization", result.AnalysisOutputs.Optimization.TopPlans.Count > 0 ? "Ready" : "Standby", result.AnalysisOutputs.Optimization.TopPlans.Count > 0 ? $"Top plan {result.AnalysisOutputs.Optimization.RecommendedPlanId} with score decomposition." : "Switch to relay mode to generate Top-N plans.", result.AnalysisOutputs.Optimization.TopPlans.Count > 0 ? "#75E0A2" : "#FFCF48");
            yield return CreateTopic(
                "13 Ground Reflection",
                "Ready",
                $"{PropagationSemanticPresentation.ResolveReflectionRisk(result.AnalysisOutputs.Reflection.MultipathRisk)} | {result.AnalysisOutputs.Reflection.RelativeGainDb:+0.0;-0.0;0.0} dB.",
                "#75E0A2");
            yield return CreateTopic("14 Monte Carlo Uncertainty", result.AnalysisOutputs.Uncertainty.Iterations > 0 ? "Ready" : "Standby", result.AnalysisOutputs.Uncertainty.SensitivitySummary, result.AnalysisOutputs.Uncertainty.Iterations > 0 ? "#75E0A2" : "#FFCF48");
            yield return CreateTopic("15 Parameter Calibration", "Ready", $"MAE {result.AnalysisOutputs.Calibration.MaeBefore:F1}->{result.AnalysisOutputs.Calibration.MaeAfter:F1}, validation MAE {result.AnalysisOutputs.Calibration.ValidationMaeAfter:F1}.", "#75E0A2");
            yield return CreateTopic("16 Spatial Alignment", "Ready", $"{result.AnalysisOutputs.SpatialAlignment.Status}, offset {result.AnalysisOutputs.SpatialAlignment.HorizontalOffsetM:F1} m.", "#75E0A2");
        }
    }

    private static PropagationTopicCoverageItemViewModel CreateTopic(string topic, string status, string summary, string accentColor)
    {
        return new PropagationTopicCoverageItemViewModel(topic, status, summary, accentColor);
    }

    private PropagationSelectedPathPreview? SelectedPathPreview
    {
        get
        {
            if (_selectedPathPreviewResolved)
                return _selectedPathPreviewCache;

            _selectedPathPreviewCache = ResolveSelectedPathPreview();
            _selectedPathPreviewResolved = true;
            return _selectedPathPreviewCache;
        }
    }

    private string ResolveSelectedLandcoverPathRoute()
    {
        var preview = SelectedPathPreview;
        if (preview is null)
            return LocalizationService.Instance.GetString("Ui.Propagation.Analysis.LandcoverEmpty");

        var stateText = LocalizationService.Instance.GetString(
            preview.IsLineOfSight
                ? "Ui.Propagation.Profile.Clear"
                : "Ui.Propagation.Profile.Blocked");
        var ridgeText = LocalizationService.Instance.Format("Ui.Propagation.Analysis.LandcoverRidges", preview.RidgeCrossings);
        return LocalizationService.Instance.Format(
            "Ui.Propagation.Analysis.LandcoverRoute",
            preview.TxSiteLabel,
            preview.RxSiteLabel,
            preview.DistanceM,
            stateText,
            ridgeText);
    }

    private string ResolveSelectedLandcoverPathSummary()
    {
        var preview = SelectedPathPreview;
        if (preview is null)
            return LocalizationService.Instance.GetString("Ui.Propagation.Analysis.LandcoverEmptyHint");

        return LocalizationService.Instance.Format(
            "Ui.Propagation.Analysis.LandcoverSummary",
            preview.LandcoverLossDb,
            preview.FsplDb,
            preview.TotalLossDb,
            preview.MarginDb,
            preview.ReceivedPowerDbm,
            preview.ThresholdDbm);
    }

    private string ResolveSelectedLandcoverPathHint()
    {
        return LocalizationService.Instance.GetString("Ui.Propagation.Analysis.LandcoverCaveat");
    }

    private IEnumerable<PropagationAnalyticalLayerItemViewModel> ResolveSelectedLandcoverPathItems()
    {
        var preview = SelectedPathPreview;
        if (preview is null || preview.DistanceM <= 0)
            yield break;

        yield return new PropagationAnalyticalLayerItemViewModel(
            LocalizationService.Instance.GetString("Ui.Propagation.Analysis.PathEvidence.LocalClass"),
            LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.PathEvidence.LocalClassSummary",
                preview.RxLandcoverInputCoefficientDbPerM,
                preview.RxLandcoverEffectiveCoefficientDbPerM),
            PropagationLandcoverPresentation.ResolveLabel(preview.RxLandcoverClass),
            PropagationLandcoverPresentation.ResolveAccentColorHex(preview.RxLandcoverClass));
        yield return new PropagationAnalyticalLayerItemViewModel(
            LocalizationService.Instance.GetString("Ui.Propagation.Analysis.PathEvidence.Constraint"),
            LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.PathEvidence.ConstraintSummary",
                preview.RidgeCrossings,
                preview.FresnelClearanceRatio,
                preview.MinimumClearanceM),
            PropagationSemanticPresentation.ResolveDominantReason(preview.DominantReasonCode),
            "#FFCF48");

        foreach (var segment in preview.LandcoverSegments
                     .Where(item => item.LengthM > 0d || item.LossDb > 0d)
                     .OrderBy(item => PropagationLandcoverPresentation.ResolveSortOrder(item.LandcoverClass))
                     .ThenByDescending(item => item.LossDb)
                     .ThenByDescending(item => item.LengthM))
        {
            var label = PropagationLandcoverPresentation.ResolveLabel(segment.LandcoverClass);
            var sharePercent = Math.Clamp((segment.LengthM / preview.DistanceM) * 100d, 0d, 100d);
            var summary = LocalizationService.Instance.Format(
                "Ui.Propagation.Analysis.LandcoverItemSummary",
                segment.LengthM,
                sharePercent);
            yield return new PropagationAnalyticalLayerItemViewModel(
                label,
                summary,
                $"{segment.LossDb:F1} dB",
                PropagationLandcoverPresentation.ResolveAccentColorHex(segment.LandcoverClass));
        }
    }

    private PropagationSelectedPathPreview? ResolveSelectedPathPreview()
    {
        var scene = _root.MapWorkbench.TerrainScene;
        if (!scene.HasTerrain || !scene.HasLandcover)
            return null;

        var selectedSite = _root.ScenarioSites.FirstOrDefault(site => string.Equals(site.Id, _root.SelectedScenarioSiteId, StringComparison.Ordinal));
        if (selectedSite is null)
            return null;

        PropagationSiteInput? txSite;
        PropagationSiteInput? rxSite;
        if (selectedSite.Role == PropagationSiteRole.TargetNode)
        {
            txSite = _root.ScenarioSites.FirstOrDefault(site => site.Role == PropagationSiteRole.BaseStation);
            rxSite = selectedSite;
        }
        else
        {
            var targets = _root.ScenarioSites.Where(site => site.Role == PropagationSiteRole.TargetNode).ToArray();
            if (targets.Length != 1)
                return null;

            txSite = selectedSite;
            rxSite = targets[0];
        }

        if (txSite is null || rxSite is null || string.Equals(txSite.Id, rxSite.Id, StringComparison.Ordinal))
            return null;

        return PropagationCoveragePreviewBuilder.BuildPathPreview(
            scene,
            txSite,
            rxSite,
            _root.FrequencyMHz,
            _root.TxPowerDbm,
            _root.SpreadingFactor,
            _root.VegetationAlphaSparse,
            _root.VegetationAlphaDense,
            _root.EnvironmentLossDb,
            _root.ShadowSigmaDb,
            _root.ReflectionCoeff);
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _selectedPathPreviewResolved = false;
        _selectedPathPreviewCache = null;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class PropagationInvestigationStripViewModel : ObservableObject
{
    private readonly PropagationViewModel _root;

    public PropagationInvestigationStripViewModel(PropagationViewModel root)
    {
        _root = root;
        _root.PropertyChanged += OnRootPropertyChanged;
    }

    public double? ProfileDistanceKm => _root.ProfileDistanceKm;
    public double? ProfileFresnelRadiusM => _root.ProfileFresnelRadiusM;
    public double? ProfileMarginDb => _root.ProfileMarginDb;
    public string? ProfileMainObstacleLabel => string.IsNullOrWhiteSpace(_root.ProfileMainObstacleLabel)
        ? _root.ProfileMainObstacleLabel
        : PropagationSemanticPresentation.ResolveObstacleLabel(_root.ProfileMainObstacleLabel);
    public double? ProfileMainObstacleV => _root.ProfileMainObstacleV;
    public double? ProfileMainObstacleLdDb => _root.ProfileMainObstacleLdDb;
    public ObservableCollection<PropagationTaskItemViewModel> ActiveTasks => _root.ActiveTasks;

    public PropagationTaskItemViewModel? SelectedTask
    {
        get => _root.SelectedTask;
        set => _root.SelectedTask = value;
    }

    public IEnumerable<PropagationProfileSampleItemViewModel> ProfileSamples
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                yield break;

            foreach (var sample in result.AnalysisOutputs.Profile.Samples)
            {
                var clearance = sample.LosHeightM - sample.TerrainElevationM;
                yield return new PropagationProfileSampleItemViewModel(
                    $"{sample.DistanceKm:F2} km",
                    $"{sample.TerrainElevationM:F1} m",
                    $"{sample.LosHeightM:F1} m",
                    $"{clearance:+0.0;-0.0;0.0} m",
                    $"{sample.FresnelRadiusM:F1} m",
                    PropagationSemanticPresentation.ResolveLandcoverToken(sample.SurfaceType),
                    sample.IsBlocked
                        ? LocalizationService.Instance.GetString("Ui.Propagation.Profile.Blocked")
                        : LocalizationService.Instance.GetString("Ui.Propagation.Profile.Clear"),
                    sample.IsBlocked ? "#FF7373" : "#75E0A2");
            }
        }
    }

    public IEnumerable<PropagationParameterAdjustmentItemViewModel> ParameterAdjustments
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                yield break;

            foreach (var adjustment in result.AnalysisOutputs.Calibration.ParameterAdjustments)
            {
                var delta = adjustment.After - adjustment.Before;
                var unit = string.IsNullOrWhiteSpace(adjustment.Unit) ? string.Empty : $" {adjustment.Unit}";
                yield return new PropagationParameterAdjustmentItemViewModel(
                    adjustment.Name,
                    LocalizationService.Instance.Format("Ui.Propagation.Calibration.Before", $"{adjustment.Before:F2}{unit}"),
                    LocalizationService.Instance.Format("Ui.Propagation.Calibration.After", $"{adjustment.After:F2}{unit}"),
                    LocalizationService.Instance.Format("Ui.Propagation.Calibration.Delta", $"{delta:+0.00;-0.00;0.00}{unit}"));
            }
        }
    }

    public string ComparisonReferenceRunId => LocalizationService.Instance.Format("Ui.Propagation.Compare.ReferenceRun", ResolveComparisonReferenceRunId());
    public string ComparisonCoverageDeltaText => LocalizationService.Instance.Format("Ui.Propagation.Compare.CoverageDelta", FormatDelta(ResolveCoverageDelta(), "km2"));
    public string ComparisonSinrDeltaText => LocalizationService.Instance.Format("Ui.Propagation.Compare.SinrDelta", FormatDelta(ResolveSinrDelta(), "dB"));
    public string ComparisonMarginDeltaText => LocalizationService.Instance.Format("Ui.Propagation.Compare.MarginDelta", FormatDelta(ResolveMarginDelta(), "dB"));
    public string CalibrationSnapshotSummary => ResolveCalibrationSnapshotSummary();
    public string CalibrationRiskSummary => _root.ValidityWarningsText;
    public string CalibrationAssumptionSummary => _root.AssumptionFlagsText;
    public string UncertaintySummary => ResolveUncertaintySummary();
    public string AlignmentSummary => ResolveAlignmentSummary();
    public string CandidateSummary => ResolveCandidateSummary();

    private string ResolveComparisonReferenceRunId()
    {
        var selectedRunId = _root.SelectedTask?.RunId ?? _root.SelectedRunIdText;
        var reference = _root.ActiveTasks
            .Where(task => !string.Equals(task.RunId, selectedRunId, StringComparison.Ordinal))
            .Select(task => task.RunId)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(reference) ? "--" : reference;
    }

    private double? ResolveCoverageDelta()
    {
        return ResolveDelta(static result => result.AnalysisOutputs.CoverageProbability.AreaP95Km2);
    }

    private double? ResolveSinrDelta()
    {
        return ResolveDelta(static result => result.AnalysisOutputs.Network.SinrDb);
    }

    private double? ResolveMarginDelta()
    {
        return ResolveDelta(static result => result.AnalysisOutputs.Link.DownlinkMarginDb);
    }

    private double? ResolveDelta(Func<PropagationSimulationResult, double?> selector)
    {
        var selected = _root.GetSelectedResult();
        var reference = _root.GetReferenceResult();
        if (selected == null || reference == null)
            return null;

        var selectedValue = selector(selected);
        var referenceValue = selector(reference);
        if (!selectedValue.HasValue || !referenceValue.HasValue)
            return null;

        return selectedValue.Value - referenceValue.Value;
    }

    private string ResolveCalibrationSnapshotSummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Snapshot", $"{_root.ModelVersionTag} | {_root.ParameterHashTag}");

        var calibration = result.AnalysisOutputs.Calibration;
        return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Snapshot", $"{result.Provenance.ModelVersion} | train {calibration.TrainingSampleCount} | val {calibration.ValidationSampleCount} | {calibration.CalibrationRunId}");
    }

    private string ResolveUncertaintySummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Uncertainty", "--");

        var uncertainty = result.AnalysisOutputs.Uncertainty;
        return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Uncertainty", $"N={uncertainty.Iterations} | CI [{uncertainty.CiLower:F1}, {uncertainty.CiUpper:F1}] | margin P10/P50/P90 {uncertainty.MarginP10Db:F1}/{uncertainty.MarginP50Db:F1}/{uncertainty.MarginP90Db:F1} dB");
    }

    private string ResolveAlignmentSummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Alignment", "--");

        var alignment = result.AnalysisOutputs.SpatialAlignment;
        return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Alignment", $"{alignment.TargetCrs} | DEM {alignment.DemResamplingMethod} {alignment.DemResolutionM:F0} m | LC {alignment.LandcoverResamplingMethod} {alignment.LandcoverResolutionM:F0} m | score {alignment.AlignmentScore:F1}");
    }

    private string ResolveCandidateSummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Candidates", "--");

        return LocalizationService.Instance.Format("Ui.Propagation.Evidence.Candidates", $"{result.SceneGeometry.RelayCandidates.Count} candidates | {result.SceneGeometry.RelayRecommendations.Count} recommended | {result.SceneGeometry.ProfileObstacles.Count} obstacles");
    }

    private static string FormatDelta(double? value, string unit)
    {
        return value.HasValue ? $"{value.Value:+0.0;-0.0;0.0} {unit}" : $"-- {unit}";
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }
}

public sealed class PropagationTaskRailViewModel : ObservableObject
{
    private readonly PropagationViewModel _root;

    public PropagationTaskRailViewModel(PropagationViewModel root)
    {
        _root = root;
        _root.PropertyChanged += OnRootPropertyChanged;
    }

    public string SelectedRunIdText => LocalizationService.Instance.Format("Ui.Propagation.RunLabel", _root.SelectedRunIdText);
    public string SelectedRunStatusText => LocalizationService.Instance.Format("Ui.Propagation.StatusLabel", _root.SelectedRunStatusText);
    public string DataVersionTag => LocalizationService.Instance.Format("Ui.Propagation.DataLabel", _root.DataVersionTag);
    public string ModelVersionTag => LocalizationService.Instance.Format("Ui.Propagation.ModelLabel", _root.ModelVersionTag);
    public string ParameterHashTag => LocalizationService.Instance.Format("Ui.Propagation.HashLabel", _root.ParameterHashTag);
    public string AssumptionFlagsText => LocalizationService.Instance.Format("Ui.Propagation.Assumptions", _root.AssumptionFlagsText);
    public string ValidityWarningsText => LocalizationService.Instance.Format("Ui.Propagation.Warnings", _root.ValidityWarningsText);
    public string OutputLayerUrisText => LocalizationService.Instance.Format("Ui.Propagation.Layers", _root.OutputLayerUrisText);
    public string SimulationMessage => _root.SimulationMessage;
    public string CurrentStageText => LocalizationService.Instance.Format("Ui.Propagation.StageLabel", _root.CurrentStageText);
    public string SimulationStateText => LocalizationService.Instance.Format("Ui.Propagation.StateLabel", _root.SimulationStateText);
    public string SimulationIssueTitle => _root.SimulationIssueTitle;
    public string SimulationIssueAction => _root.SimulationIssueAction;
    public string SimulationIssueDetail => _root.SimulationIssueDetail;
    public bool HasSimulationIssue => !string.IsNullOrWhiteSpace(_root.SimulationIssueTitle);
    public string KnowledgeStatusSummary => ResolveKnowledgeStatusSummary();

    public IEnumerable<PropagationTopicCoverageItemViewModel> EvidenceChecklist
    {
        get
        {
            var result = _root.GetSelectedResult();
            if (result is null)
                yield break;

            var noWarnings = string.Equals(_root.ValidityWarningsText, LocalizationService.Instance.GetString("Status.Propagation.NoValidityWarning"), StringComparison.OrdinalIgnoreCase);
            yield return new PropagationTopicCoverageItemViewModel(LocalizationService.Instance.GetString("Ui.Propagation.EvidenceItem.Datasets"), LocalizationService.Instance.GetString("Ui.Propagation.Status.Ready"), $"{result.Provenance.DatasetBundle.DemVersion} | {result.Provenance.DatasetBundle.LandcoverVersion}", "#75E0A2");
            yield return new PropagationTopicCoverageItemViewModel(LocalizationService.Instance.GetString("Ui.Propagation.EvidenceItem.Assumptions"), LocalizationService.Instance.GetString("Ui.Propagation.Status.Ready"), _root.AssumptionFlagsText, "#75E0A2");
            yield return new PropagationTopicCoverageItemViewModel(LocalizationService.Instance.GetString("Ui.Propagation.EvidenceItem.Warnings"), LocalizationService.Instance.GetString(noWarnings ? "Ui.Propagation.Status.Clear" : "Ui.Propagation.Status.Watch"), _root.ValidityWarningsText, noWarnings ? "#75E0A2" : "#FFCF48");
            yield return new PropagationTopicCoverageItemViewModel(LocalizationService.Instance.GetString("Ui.Propagation.EvidenceItem.Alignment"), LocalizationService.Instance.GetString("Ui.Propagation.Status.Ready"), result.AnalysisOutputs.SpatialAlignment.Status, "#75E0A2");
            yield return new PropagationTopicCoverageItemViewModel(LocalizationService.Instance.GetString("Ui.Propagation.EvidenceItem.Calibration"), LocalizationService.Instance.GetString("Ui.Propagation.Status.Ready"), $"{result.AnalysisOutputs.Calibration.CalibrationRunId} | MAE delta {result.AnalysisOutputs.Calibration.MaeDelta:F1}", "#75E0A2");
        }
    }

    private string ResolveKnowledgeStatusSummary()
    {
        var result = _root.GetSelectedResult();
        if (result is null)
            return LocalizationService.Instance.GetString("Ui.Propagation.KnowledgeStatus.Empty");

        var relay = result.AnalysisOutputs.Optimization.TopPlans.Count > 0
            ? LocalizationService.Instance.GetString("Ui.Propagation.KnowledgeStatus.RelayReady")
            : LocalizationService.Instance.GetString("Ui.Propagation.KnowledgeStatus.RelayStandby");
        var uncertainty = result.AnalysisOutputs.Uncertainty.Iterations > 0
            ? LocalizationService.Instance.GetString("Ui.Propagation.KnowledgeStatus.UncertaintyReady")
            : LocalizationService.Instance.GetString("Ui.Propagation.KnowledgeStatus.UncertaintyStandby");
        return LocalizationService.Instance.Format("Ui.Propagation.KnowledgeStatus.Value", relay, uncertainty);
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }
}

public sealed partial class PropagationViewModel
{
    public PropagationControlPanelViewModel ControlPanel { get; private set; } = null!;
    public PropagationMapWorkbenchViewModel MapWorkbench { get; private set; } = null!;
    public PropagationAnalysisPanelViewModel AnalysisPanel { get; private set; } = null!;
    public PropagationInvestigationStripViewModel InvestigationStrip { get; private set; } = null!;
    public PropagationTaskRailViewModel TaskRail { get; private set; } = null!;

    private void InitializeWorkbenchModules()
    {
        ControlPanel = new PropagationControlPanelViewModel(this);
        MapWorkbench = new PropagationMapWorkbenchViewModel(this);
        AnalysisPanel = new PropagationAnalysisPanelViewModel(this);
        InvestigationStrip = new PropagationInvestigationStripViewModel(this);
        TaskRail = new PropagationTaskRailViewModel(this);
    }

    internal PropagationSimulationResult? GetSelectedResult()
    {
        if (SelectedTask != null && _resultsByRunId.TryGetValue(SelectedTask.RunId, out var selectedByTask))
            return selectedByTask;

        if (!string.IsNullOrWhiteSpace(SelectedRunIdText) && _resultsByRunId.TryGetValue(SelectedRunIdText, out var selectedByRunId))
            return selectedByRunId;

        return null;
    }

    internal PropagationSimulationResult? GetReferenceResult()
    {
        var selectedRunId = SelectedTask?.RunId ?? SelectedRunIdText;
        foreach (var task in ActiveTasks)
        {
            if (string.Equals(task.RunId, selectedRunId, StringComparison.Ordinal))
                continue;

            if (_resultsByRunId.TryGetValue(task.RunId, out var reference))
                return reference;
        }

        return null;
    }
}
