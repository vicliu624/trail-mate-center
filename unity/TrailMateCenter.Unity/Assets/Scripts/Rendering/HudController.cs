using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class HudController : MonoBehaviour
{
    private readonly Dictionary<string, LayerRow> _layerRows = new(StringComparer.Ordinal);
    private LayerManager? _layerManager;
    private LayerConfig _layerConfig = new();
    private Text? _evidenceText;
    private Text? _legendTitle;
    private Text? _legendRange;
    private RawImage? _legendImage;

    public void Initialize(Canvas canvas, LayerManager layerManager, LayerConfig layerConfig)
    {
        _layerManager = layerManager;
        _layerConfig = layerConfig;
        _layerManager.LayerPresentationChanged += OnLayerPresentationChanged;

        BuildEvidencePanel(canvas);
        BuildLegendPanel(canvas);
        BuildLayerControlPanel(canvas);

        foreach (var item in _layerManager.GetLayerPresentation())
            UpdateLayerRow(item);
    }

    private void OnDestroy()
    {
        if (_layerManager != null)
            _layerManager.LayerPresentationChanged -= OnLayerPresentationChanged;
    }

    public void ApplyResult(JObject payload)
    {
        if (_evidenceText == null)
            return;

        var sb = new StringBuilder();
        var runMeta = payload["runMeta"] as JObject;
        var analysis = payload["analysisOutputs"] as JObject;
        var quality = payload["qualityFlags"] as JObject;
        var provenance = payload["provenance"] as JObject;

        AppendRunMeta(sb, runMeta);
        AppendLink(sb, analysis?["link"] as JObject);
        AppendLoss(sb, analysis?["lossBreakdown"] as JObject);
        AppendReliability(sb, analysis?["reliability"] as JObject);
        AppendCoverage(sb, analysis?["coverageProbability"] as JObject);
        AppendNetwork(sb, analysis?["network"] as JObject);
        AppendProfile(sb, analysis?["profile"] as JObject);
        AppendOptimization(sb, analysis?["optimization"] as JObject);
        AppendUncertainty(sb, analysis?["uncertainty"] as JObject);
        AppendCalibration(sb, analysis?["calibration"] as JObject);
        AppendQuality(sb, quality);
        AppendProvenance(sb, provenance);

        if (sb.Length == 0)
            sb.Append("Result payload received.");

        _evidenceText.text = sb.ToString();
    }

    private void BuildEvidencePanel(Canvas canvas)
    {
        if (!_layerConfig.ShowEvidenceCards)
            return;

        var panel = CreatePanel(canvas.transform, "EvidencePanel", new Vector2(14, -14), new Vector2(430, 420));
        _evidenceText = CreateText(panel.transform, "EvidenceText", 16f);
        _evidenceText!.text = "Awaiting results...";
    }

    private void BuildLegendPanel(Canvas canvas)
    {
        if (!_layerConfig.ShowLegend)
            return;

        var panel = CreatePanel(canvas.transform, "LegendPanel", new Vector2(14, -450), new Vector2(430, 130));
        _legendTitle = CreateText(panel.transform, "LegendTitle", 17f);
        _legendTitle!.text = "Legend";

        var legendImageGo = new GameObject("LegendImage");
        legendImageGo.transform.SetParent(panel.transform, false);
        var imageRect = legendImageGo.AddComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0, 0);
        imageRect.anchorMax = new Vector2(1, 0);
        imageRect.anchoredPosition = new Vector2(0, 22);
        imageRect.sizeDelta = new Vector2(-20, 24);
        _legendImage = legendImageGo.AddComponent<RawImage>();
        _legendImage.color = Color.white;

        _legendRange = CreateText(panel.transform, "LegendRange", 15f);
        _legendRange!.alignment = TextAnchor.LowerLeft;
        _legendRange.text = "--";
    }

    private void BuildLayerControlPanel(Canvas canvas)
    {
        var panel = CreatePanel(canvas.transform, "LayerControlPanel", new Vector2(-14, -14), new Vector2(430, 520), rightAligned: true);
        var title = CreateText(panel.transform, "LayerTitle", 17f);
        title.text = "Layer Controls";
    }

    private void OnLayerPresentationChanged(object? sender, LayerPresentationChangedEventArgs e)
    {
        UpdateLayerRow(e);
        if (e.IsActive)
            UpdateLegend(e);
    }

    private void UpdateLayerRow(LayerPresentationChangedEventArgs e)
    {
        if (_layerManager == null)
            return;

        if (!_layerRows.TryGetValue(e.LayerId, out var row))
        {
            var panel = transform.Find("LayerControlPanel");
            if (panel == null)
                return;

            row = CreateLayerRow(panel, _layerRows.Count, e.LayerId, _layerManager);
            _layerRows[e.LayerId] = row;
        }

        row.SuppressEvents = true;
        row.Toggle.isOn = e.Visible;
        row.Slider.value = e.Opacity;
        row.Label.text = $"{e.DisplayName} {(e.IsActive ? "(Active)" : string.Empty)}";
        row.SuppressEvents = false;
    }

    private void UpdateLegend(LayerPresentationChangedEventArgs e)
    {
        if (_legendTitle != null)
            _legendTitle.text = $"Legend - {e.DisplayName}";

        if (_legendRange != null)
        {
            var min = e.MinValue?.ToString("F2") ?? "--";
            var max = e.MaxValue?.ToString("F2") ?? "--";
            var legend = _layerManager?.GetActiveLegend();
            if (legend != null && legend.ClassBreaks.Count > 0)
            {
                var breaks = string.Join(", ", legend.ClassBreaks.Select(static value => value.ToString("F2")));
                _legendRange.text = $"Range: {min} .. {max} {e.Unit}\nBreaks: {breaks}".Trim();
            }
            else
            {
                _legendRange.text = $"Range: {min} .. {max} {e.Unit}".Trim();
            }
        }

        if (_legendImage != null && _layerManager != null)
        {
            var legendState = _layerManager.GetActiveLegend();
            _legendImage.texture = legendState?.RampTexture;
        }
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, bool rightAligned = false)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = rightAligned ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = rect.anchorMin;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var image = panel.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.50f);
        return panel;
    }

    private static Text CreateText(Transform parent, string name, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(12f, 12f);
        rect.offsetMax = new Vector2(-12f, -12f);

        var text = go.AddComponent<Text>();
        text.font = GetBuiltinFont();
        text.fontSize = Mathf.RoundToInt(fontSize);
        text.color = new Color(0.92f, 0.95f, 0.98f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static LayerRow CreateLayerRow(Transform panel, int index, string layerId, LayerManager manager)
    {
        var rowGo = new GameObject($"LayerRow_{layerId}");
        rowGo.transform.SetParent(panel, false);
        var rowRect = rowGo.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -40f - index * 62f);
        rowRect.sizeDelta = new Vector2(0f, 56f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(rowGo.transform, false);
        var labelRect = labelGo.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.45f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(68f, 0f);
        labelRect.offsetMax = new Vector2(-10f, 0f);
        var label = labelGo.AddComponent<Text>();
        label.font = GetBuiltinFont();
        label.fontSize = 14;
        label.color = Color.white;
        label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.text = layerId;

        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(rowGo.transform, false);
        var toggleRect = toggleGo.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0f, 0.5f);
        toggleRect.anchorMax = new Vector2(0f, 0.5f);
        toggleRect.anchoredPosition = new Vector2(28f, 8f);
        toggleRect.sizeDelta = new Vector2(18f, 18f);
        var toggle = toggleGo.AddComponent<Toggle>();
        var bg = toggleGo.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.2f);
        toggle.targetGraphic = bg;

        var sliderGo = new GameObject("Opacity");
        sliderGo.transform.SetParent(rowGo.transform, false);
        var sliderRect = sliderGo.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0.45f);
        sliderRect.offsetMin = new Vector2(68f, 6f);
        sliderRect.offsetMax = new Vector2(-12f, -4f);
        var slider = sliderGo.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.6f;
        var sliderBg = sliderGo.AddComponent<Image>();
        sliderBg.color = new Color(1f, 1f, 1f, 0.1f);
        slider.targetGraphic = sliderBg;

        var row = new LayerRow
        {
            LayerId = layerId,
            Toggle = toggle,
            Slider = slider,
            Label = label,
            SuppressEvents = false
        };

        toggle.onValueChanged.AddListener(value =>
        {
            if (row.SuppressEvents)
                return;
            manager.SetLayerVisibility(layerId, value);
        });
        slider.onValueChanged.AddListener(value =>
        {
            if (row.SuppressEvents)
                return;
            manager.SetLayerOpacity(layerId, value);
        });

        return row;
    }

    private static void AppendRunMeta(StringBuilder sb, JObject? meta)
    {
        if (meta == null)
            return;
        var runId = meta.Value<string>("runId");
        var status = meta.Value<int?>("status");
        var duration = meta.Value<double?>("durationMs");
        if (!string.IsNullOrWhiteSpace(runId)) sb.AppendLine($"Run: {runId}");
        if (status.HasValue) sb.AppendLine($"Status: {status.Value}");
        if (duration.HasValue) sb.AppendLine($"Duration: {duration.Value:F0} ms");
        sb.AppendLine();
    }

    private static void AppendLink(StringBuilder sb, JObject? link)
    {
        if (link == null)
            return;
        sb.AppendLine("Link");
        AppendKV(sb, "DL RSSI", link.Value<double?>("downlinkRssiDbm"), "dBm");
        AppendKV(sb, "UL RSSI", link.Value<double?>("uplinkRssiDbm"), "dBm");
        AppendKV(sb, "DL Margin", link.Value<double?>("downlinkMarginDb"), "dB");
        AppendKV(sb, "UL Margin", link.Value<double?>("uplinkMarginDb"), "dB");
        var feasible = link.Value<bool?>("linkFeasible");
        if (feasible.HasValue) sb.AppendLine($"Link Feasible: {feasible.Value}");
        sb.AppendLine();
    }

    private static void AppendLoss(StringBuilder sb, JObject? loss)
    {
        if (loss == null)
            return;
        sb.AppendLine("Loss");
        AppendKV(sb, "FSPL", loss.Value<double?>("fsplDb"), "dB");
        AppendKV(sb, "Diffraction", loss.Value<double?>("diffractionDb"), "dB");
        AppendKV(sb, "Vegetation", loss.Value<double?>("vegetationDb"), "dB");
        AppendKV(sb, "Reflection", loss.Value<double?>("reflectionDb"), "dB");
        AppendKV(sb, "Shadow", loss.Value<double?>("shadowDb"), "dB");
        sb.AppendLine();
    }

    private static void AppendReliability(StringBuilder sb, JObject? reliability)
    {
        if (reliability == null)
            return;
        sb.AppendLine("Reliability");
        AppendKV(sb, "P95", reliability.Value<double?>("p95"), "");
        AppendKV(sb, "P80", reliability.Value<double?>("p80"), "");
        var note = reliability.Value<string>("confidenceNote");
        if (!string.IsNullOrWhiteSpace(note)) sb.AppendLine($"Note: {note}");
        sb.AppendLine();
    }

    private static void AppendCoverage(StringBuilder sb, JObject? coverage)
    {
        if (coverage == null)
            return;
        sb.AppendLine("Coverage");
        AppendKV(sb, "Area P95", coverage.Value<double?>("areaP95Km2"), "km2");
        AppendKV(sb, "Area P80", coverage.Value<double?>("areaP80Km2"), "km2");
        sb.AppendLine();
    }

    private static void AppendNetwork(StringBuilder sb, JObject? network)
    {
        if (network == null)
            return;
        sb.AppendLine("Network");
        AppendKV(sb, "SINR", network.Value<double?>("sinrDb"), "dB");
        AppendKV(sb, "Conflict", network.Value<double?>("conflictRate"), "");
        AppendKV(sb, "Capacity", network.Value<double?>("maxCapacityNodes"), "nodes");
        sb.AppendLine();
    }

    private static void AppendProfile(StringBuilder sb, JObject? profile)
    {
        if (profile == null)
            return;
        sb.AppendLine("Profile");
        AppendKV(sb, "Distance", profile.Value<double?>("distanceKm"), "km");
        AppendKV(sb, "Fresnel", profile.Value<double?>("fresnelRadiusM"), "m");
        AppendKV(sb, "Margin", profile.Value<double?>("marginDb"), "dB");
        sb.AppendLine();
    }

    private static void AppendOptimization(StringBuilder sb, JObject? optimization)
    {
        if (optimization == null)
            return;
        var plans = optimization["topPlans"] as JArray;
        if (plans == null || plans.Count == 0)
            return;
        var top = plans[0] as JObject;
        if (top == null)
            return;
        sb.AppendLine("Optimization");
        sb.AppendLine($"Top Plan: {top.Value<string>("planId")}");
        AppendKV(sb, "Score", top.Value<double?>("score"), "");
        sb.AppendLine();
    }

    private static void AppendUncertainty(StringBuilder sb, JObject? uncertainty)
    {
        if (uncertainty == null)
            return;
        sb.AppendLine("Uncertainty");
        AppendKV(sb, "CI Low", uncertainty.Value<double?>("ciLower"), "");
        AppendKV(sb, "CI High", uncertainty.Value<double?>("ciUpper"), "");
        AppendKV(sb, "Stability", uncertainty.Value<double?>("stabilityIndex"), "");
        sb.AppendLine();
    }

    private static void AppendCalibration(StringBuilder sb, JObject? calibration)
    {
        if (calibration == null)
            return;
        sb.AppendLine("Calibration");
        AppendKV(sb, "MAE Before", calibration.Value<double?>("maeBefore"), "");
        AppendKV(sb, "MAE After", calibration.Value<double?>("maeAfter"), "");
        AppendKV(sb, "RMSE Before", calibration.Value<double?>("rmseBefore"), "");
        AppendKV(sb, "RMSE After", calibration.Value<double?>("rmseAfter"), "");
        sb.AppendLine();
    }

    private static void AppendQuality(StringBuilder sb, JObject? quality)
    {
        if (quality == null)
            return;
        var warnings = quality["validityWarnings"] as JArray;
        if (warnings == null || warnings.Count == 0)
            return;
        sb.AppendLine("Warnings");
        foreach (var w in warnings)
            sb.AppendLine($"- {w}");
        sb.AppendLine();
    }

    private static void AppendProvenance(StringBuilder sb, JObject? provenance)
    {
        if (provenance == null)
            return;
        sb.AppendLine("Provenance");
        var model = provenance.Value<string>("modelVersion");
        var commit = provenance.Value<string>("gitCommit");
        var hash = provenance.Value<string>("parameterHash");
        if (!string.IsNullOrWhiteSpace(model)) sb.AppendLine($"Model: {model}");
        if (!string.IsNullOrWhiteSpace(commit)) sb.AppendLine($"Commit: {commit}");
        if (!string.IsNullOrWhiteSpace(hash)) sb.AppendLine($"Params: {hash}");
    }

    private static void AppendKV(StringBuilder sb, string label, double? value, string unit)
    {
        if (!value.HasValue)
            return;
        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        sb.AppendLine($"{label}: {value.Value:F2}{suffix}");
    }

    private static Font GetBuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private sealed class LayerRow
    {
        public string LayerId { get; set; } = string.Empty;
        public Toggle Toggle { get; set; } = null!;
        public Slider Slider { get; set; } = null!;
        public Text Label { get; set; } = null!;
        public bool SuppressEvents { get; set; }
    }
}
}





