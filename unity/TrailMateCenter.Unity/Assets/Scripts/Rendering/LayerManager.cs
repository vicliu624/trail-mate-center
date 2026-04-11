using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Bridge;
using TrailMateCenter.Unity.Core;
using TrailMateCenter.Unity.Diagnostics;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class LayerPresentationChangedEventArgs : EventArgs
{
    public string LayerId { get; set; }
    public string DisplayName { get; set; }
    public bool Visible { get; set; }
    public float Opacity { get; set; }
    public bool IsActive { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string Palette { get; set; } = string.Empty;
    public string ClassBreaksText { get; set; } = string.Empty;
}

public sealed class LayerLegendState
{
    public string LayerId { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public IReadOnlyList<double> ClassBreaks { get; set; } = Array.Empty<double>();
    public Texture2D? RampTexture { get; set; }
}

public sealed class LayerManager : MonoBehaviour
{
    private readonly Dictionary<string, LayerSlot> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _ramps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _activeLayers = new();
    private BridgeCoordinator? _bridge;
    private DiagnosticReporter? _diagnostics;
    private GeoReferenceConfig _geoReference = new();
    private LayerConfig _layerConfig = new();
    private PerformanceConfig _performance = new();
    private RasterConversionConfig _rasterConversion = new();
    private RasterTextureCache _textureCache = new(24);
    private Terrain? _terrain;
    private string _activeLayerId = "coverage_mean";
    private Coroutine? _switchRoutine;
    private string _lastRunId = string.Empty;

    public event EventHandler<LayerPresentationChangedEventArgs>? LayerPresentationChanged;

    public void Initialize(
        LayerConfig layerConfig,
        GeoReferenceConfig geoReference,
        PerformanceConfig performance,
        Terrain terrain,
        BridgeCoordinator bridge,
        DiagnosticReporter diagnostics)
    {
        _bridge = bridge;
        _diagnostics = diagnostics;
        _layerConfig = layerConfig;
        _geoReference = geoReference;
        _performance = performance;
        _rasterConversion = layerConfig.RasterConversion ?? new RasterConversionConfig();
        _terrain = terrain;
        _textureCache = new RasterTextureCache(_performance.TextureCacheCapacity);

        var size = terrain.terrainData.size;
        var origin = terrain.GetPosition();
        var center = new Vector3(origin.x + size.x / 2f, origin.y, origin.z + size.z / 2f);

        RegisterLayer("coverage_mean", "Coverage Mean", new Color(0.2f, 0.78f, 0.36f, 0.55f), center, size);
        RegisterLayer("reliability_95", "Reliability 95%", new Color(0.2f, 0.62f, 0.98f, 0.55f), center, size);
        RegisterLayer("reliability_80", "Reliability 80%", new Color(0.98f, 0.82f, 0.2f, 0.55f), center, size);
        RegisterLayer("interference", "Interference", new Color(0.98f, 0.46f, 0.2f, 0.55f), center, size);
        RegisterLayer("capacity", "Capacity", new Color(0.55f, 0.5f, 0.95f, 0.55f), center, size);
        RegisterLayer("link_margin", "Link Margin", new Color(0.85f, 0.25f, 0.25f, 0.55f), center, size);

        _activeLayers.Clear();
        _activeLayers.Add(_activeLayerId);
        EmitAllPresentation();
    }

    public void ApplyResult(string runId, JObject payload)
    {
        _lastRunId = runId;
        var outputs = payload["modelOutputs"] as JObject;
        if (outputs == null)
            return;

        SetLayerUri("coverage_mean", outputs.Value<string>("meanCoverageRasterUri"));
        SetLayerUri("reliability_95", outputs.Value<string>("reliability95RasterUri"));
        SetLayerUri("reliability_80", outputs.Value<string>("reliability80RasterUri"));
        SetLayerUri("interference", outputs.Value<string>("interferenceRasterUri"));
        SetLayerUri("capacity", outputs.Value<string>("capacityRasterUri"));
        TryApplyRasterLayerMetadata(outputs);
    }

    public void RequestActivateLayer(string layerId, string runId)
    {
        if (string.IsNullOrWhiteSpace(layerId))
            return;

        RequestActivateLayers(new[] { layerId }, runId);
    }

    public void RequestActivateLayers(IEnumerable<string> layerIds, string runId)
    {
        var requested = layerIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Count == 0)
            return;

        if (_switchRoutine != null)
            StopCoroutine(_switchRoutine);

        _switchRoutine = StartCoroutine(SwitchLayersRoutine(requested, runId));
    }

    public IReadOnlyList<LayerPresentationChangedEventArgs> GetLayerPresentation()
    {
        return _layers.Values
            .Select(slot => BuildPresentation(slot))
            .ToList();
    }

    public void SetLayerVisibility(string layerId, bool visible)
    {
        if (!_layers.TryGetValue(layerId, out var slot))
            return;

        slot.Visible = visible;
        slot.Overlay.SetVisible(visible && !slot.UseTiledSource);
        slot.TiledSurface.SetVisible(visible && slot.UseTiledSource);
        EmitPresentation(slot);
    }

    public void SetLayerOpacity(string layerId, float opacity)
    {
        if (!_layers.TryGetValue(layerId, out var slot))
            return;

        slot.Opacity = Mathf.Clamp01(opacity);
        slot.Overlay.SetOpacity(slot.Opacity);
        slot.TiledSurface.SetOpacity(slot.Opacity);
        EmitPresentation(slot);
    }

    public void SetLayerOrder(IReadOnlyList<string> layerOrder)
    {
        if (layerOrder == null || layerOrder.Count == 0)
            return;

        for (var i = 0; i < layerOrder.Count; i++)
        {
            if (!_layers.TryGetValue(layerOrder[i], out var slot))
                continue;
            slot.LayerRoot.transform.SetSiblingIndex(i);
        }
    }

    public LayerLegendState? GetActiveLegend()
    {
        if (!_layers.TryGetValue(_activeLayerId, out var slot))
            return null;

        return new LayerLegendState
        {
            LayerId = slot.LayerId,
            Unit = slot.Metadata?.Unit ?? string.Empty,
            MinValue = slot.Metadata?.MinValue,
            MaxValue = slot.Metadata?.MaxValue,
            ClassBreaks = slot.Metadata?.ClassBreaks ?? Array.Empty<double>(),
            RampTexture = GetOrCreateRamp(ResolvePalette(slot.LayerId, slot.Metadata)),
        };
    }

    private IEnumerator SwitchLayersRoutine(IReadOnlyList<string> requestedLayerIds, string runId)
    {
        var started = Time.realtimeSinceStartup;
        var total = Mathf.Max(1, requestedLayerIds.Count);

        for (var i = 0; i < requestedLayerIds.Count; i++)
        {
            var layerId = requestedLayerIds[i];
            if (!_layers.TryGetValue(layerId, out var slot))
            {
                yield return SendLayerState(runId, layerId, "failed", null, null, "layer not found");
                continue;
            }

            var progress = i * 100d / total;
            yield return SendLayerState(runId, layerId, "loading", progress, null, "loading layer");
            if (!EnsureLayerTexture(slot, runId))
            {
                yield return SendLayerState(runId, layerId, "failed", null, null, "layer load failed");
                continue;
            }
        }

        if (_layerConfig.AllowLayerStacking)
        {
            foreach (var slot in _layers.Values)
            {
                var visible = requestedLayerIds.Contains(slot.LayerId, StringComparer.Ordinal);
                slot.Visible = visible;
                slot.Overlay.SetVisible(visible && !slot.UseTiledSource);
                slot.TiledSurface.SetVisible(visible && slot.UseTiledSource);
                EmitPresentation(slot);
            }
            _activeLayers.Clear();
            _activeLayers.AddRange(requestedLayerIds);
            _activeLayerId = requestedLayerIds[0];
        }
        else
        {
            var selected = requestedLayerIds[0];
            _activeLayerId = selected;
            _activeLayers.Clear();
            _activeLayers.Add(selected);
            foreach (var slot in _layers.Values)
            {
                var visible = slot.LayerId == selected;
                slot.Visible = visible;
                slot.Overlay.SetVisible(visible && !slot.UseTiledSource);
                slot.TiledSurface.SetVisible(visible && slot.UseTiledSource);
                EmitPresentation(slot);
            }
        }

        var elapsedMs = (Time.realtimeSinceStartup - started) * 1000f;
        _diagnostics?.UpdateLayerLoadMs(elapsedMs);

        foreach (var layerId in requestedLayerIds)
            yield return SendLayerState(runId, layerId, "ready", 100, elapsedMs, "layer ready");
    }

    private bool EnsureLayerTexture(LayerSlot slot, string runId)
    {
        if (slot.Metadata?.HasTileSource == true && _layerConfig.EnableTiledRaster)
        {
            ApplyOverlayMetadata(slot, slot.Metadata, runId);
            return true;
        }

        if (slot.Texture != null)
        {
            ApplyOverlayMetadata(slot, slot.Metadata, runId);
            slot.Overlay.ApplyTexture(slot.Texture);
            return true;
        }

        if (string.IsNullOrWhiteSpace(slot.RasterUri))
            return true;

        if (_textureCache.TryGet(slot.RasterUri, out var cached))
        {
            _diagnostics?.MarkCacheHit();
            slot.Texture = cached.Texture;
            MergeMetadata(slot.Metadata, cached.Metadata);
            ApplyOverlayMetadata(slot, slot.Metadata, runId);
            slot.Overlay.ApplyTexture(slot.Texture);
            return true;
        }

        if (TextureLoader.TryLoad(slot.RasterUri, _rasterConversion, out var loadResult, out var loadError))
        {
            _diagnostics?.MarkCacheMiss();
            slot.Texture = loadResult.Texture;
            _textureCache.Put(slot.RasterUri, loadResult);
            MergeMetadata(slot.Metadata, loadResult.Metadata);
            ApplyOverlayMetadata(slot, slot.Metadata, runId);
            slot.Overlay.ApplyTexture(slot.Texture);
            return true;
        }

        _diagnostics?.MarkCacheMiss();
        _diagnostics?.MarkStatus($"layer load failed: {slot.LayerId}");
        slot.Overlay.ApplyColor(slot.FallbackColor);
        slot.TiledSurface.DisableSource();
        ReportLayerError(runId, slot.LayerId, $"layer texture unreadable: {loadError}");
        return false;
    }

    private void RegisterLayer(string layerId, string displayName, Color color, Vector3 center, Vector3 size)
    {
        var layerGo = new GameObject($"Layer_{layerId}");
        layerGo.transform.SetParent(transform, false);
        layerGo.transform.position = center;

        var overlay = layerGo.AddComponent<LayerOverlay>();
        var terrain = _layerConfig.ConformToTerrain ? _terrain : null;
        overlay.Initialize(
            layerId,
            terrain,
            _performance.OverlayMeshLodNear,
            size.x,
            size.z,
            center.y + _layerConfig.DefaultOverlayHeight,
            _layerConfig.TerrainOffset,
            color);
        overlay.ConfigureLod(_performance.OverlayMeshLodNear, _performance.OverlayMeshLodFar, _performance.OverlayMeshLodDistance);

        overlay.SetRampTexture(GetOrCreateRamp(ResolvePalette(layerId, null)));
        overlay.SetOpacity(color.a);
        overlay.SetVisible(false);

        var tiledSurface = layerGo.AddComponent<TiledLayerSurface>();
        tiledSurface.Initialize(layerId, terrain, _layerConfig, _rasterConversion);
        tiledSurface.SetVisible(false);
        tiledSurface.SetOpacity(color.a);
        tiledSurface.SetRampTexture(GetOrCreateRamp(ResolvePalette(layerId, null)));

        _layers[layerId] = new LayerSlot(layerId, displayName, layerGo, overlay, tiledSurface, color)
        {
            Metadata = new RasterMetadata
            {
                Palette = ResolvePalette(layerId, null)
            },
            Visible = false,
            Opacity = color.a,
        };
    }

    private void SetLayerUri(string layerId, string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;
        if (!_layers.TryGetValue(layerId, out var slot))
            return;

        if (string.Equals(slot.RasterUri, uri, StringComparison.OrdinalIgnoreCase))
            return;

        slot.Texture = null;
        slot.RasterUri = uri;
    }

    private void TryApplyRasterLayerMetadata(JObject outputs)
    {
        var rasterLayers = (outputs["rasterLayers"] as JArray) ?? (outputs["raster_layers"] as JArray);
        if (rasterLayers == null)
            return;

        foreach (var token in rasterLayers)
        {
            if (token is not JObject item)
                continue;

            var layerId = item.Value<string>("layerId") ?? item.Value<string>("layer_id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(layerId))
                continue;
            if (!_layers.TryGetValue(layerId, out var slot))
                continue;

            var rasterUri = item.Value<string>("rasterUri") ?? item.Value<string>("raster_uri");
            if (!string.IsNullOrWhiteSpace(rasterUri))
                SetLayerUri(layerId, rasterUri);

            var metadata = slot.Metadata ?? new RasterMetadata();
            if (item["bounds"] is JObject boundsToken && TryParseBounds(boundsToken, out var bounds))
                metadata.Bounds = bounds;

            metadata.Crs = item.Value<string>("crs") ?? metadata.Crs;
            metadata.TileTemplateUri = item.Value<string>("tileTemplateUri") ?? item.Value<string>("tile_template_uri") ?? metadata.TileTemplateUri;
            metadata.MinZoom = item.Value<int?>("minZoom") ?? item.Value<int?>("min_zoom") ?? metadata.MinZoom;
            metadata.MaxZoom = item.Value<int?>("maxZoom") ?? item.Value<int?>("max_zoom") ?? metadata.MaxZoom;
            metadata.TileSize = item.Value<int?>("tileSize") ?? item.Value<int?>("tile_size") ?? metadata.TileSize;
            metadata.MinValue = item.Value<double?>("minValue") ?? item.Value<double?>("min_value") ?? metadata.MinValue;
            metadata.MaxValue = item.Value<double?>("maxValue") ?? item.Value<double?>("max_value") ?? metadata.MaxValue;
            metadata.NoDataValue = item.Value<double?>("noDataValue") ?? item.Value<double?>("nodata") ?? metadata.NoDataValue;
            metadata.ValueScale = item.Value<double?>("valueScale") ?? item.Value<double?>("value_scale") ?? metadata.ValueScale;
            metadata.ValueOffset = item.Value<double?>("valueOffset") ?? item.Value<double?>("value_offset") ?? metadata.ValueOffset;
            metadata.ClassBreaks = ParseClassBreaks(item, metadata.ClassBreaks);
            metadata.Unit = item.Value<string>("unit") ?? metadata.Unit;
            metadata.Palette = item.Value<string>("palette") ?? metadata.Palette;
            slot.Metadata = metadata;
            EmitPresentation(slot);
        }
    }

    private static IReadOnlyList<double> ParseClassBreaks(JObject item, IReadOnlyList<double> fallback)
    {
        var token = (item["classBreaks"] as JArray) ?? (item["class_breaks"] as JArray);
        if (token == null || token.Count == 0)
            return fallback;

        var values = token
            .Select(static t => t?.Value<double?>())
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .OrderBy(static value => value)
            .ToArray();
        return values.Length == 0 ? fallback : values;
    }

    private static bool TryParseBounds(JObject token, out RasterBounds bounds)
    {
        bounds = default;
        var minX = token.Value<double?>("minX") ?? token.Value<double?>("min_x") ?? token.Value<double?>("xmin");
        var maxX = token.Value<double?>("maxX") ?? token.Value<double?>("max_x") ?? token.Value<double?>("xmax");
        var minZ = token.Value<double?>("minZ") ?? token.Value<double?>("min_z") ?? token.Value<double?>("minY") ?? token.Value<double?>("min_y") ?? token.Value<double?>("ymin");
        var maxZ = token.Value<double?>("maxZ") ?? token.Value<double?>("max_z") ?? token.Value<double?>("maxY") ?? token.Value<double?>("max_y") ?? token.Value<double?>("ymax");
        if (minX == null || maxX == null || minZ == null || maxZ == null)
            return false;

        var parsed = new RasterBounds(minX.Value, minZ.Value, maxX.Value, maxZ.Value);
        if (!parsed.IsValid)
            return false;

        bounds = parsed;
        return true;
    }

    private void ApplyOverlayMetadata(LayerSlot slot, RasterMetadata? metadata, string runId)
    {
        if (metadata == null || _terrain == null)
            return;

        var palette = ResolvePalette(slot.LayerId, metadata);
        var ramp = GetOrCreateRamp(palette);
        slot.Overlay.SetRampTexture(ramp);
        slot.TiledSurface.SetRampTexture(ramp);
        slot.Overlay.SetValueMapping(
            metadata.MinValue,
            metadata.MaxValue,
            metadata.NoDataValue,
            metadata.ValueScale,
            metadata.ValueOffset,
            metadata.ClassBreaks);
        slot.TiledSurface.SetValueMapping(
            metadata.MinValue,
            metadata.MaxValue,
            metadata.NoDataValue,
            metadata.ValueScale,
            metadata.ValueOffset,
            metadata.ClassBreaks);
        slot.Overlay.SetOpacity(slot.Opacity);
        slot.TiledSurface.SetOpacity(slot.Opacity);

        if (metadata.Bounds == null || !metadata.Bounds.Value.IsValid)
        {
            slot.UseTiledSource = false;
            slot.TiledSurface.DisableSource();
            return;
        }

        var mapping = GeoReferenceMapper.MapToTerrain(
            metadata.Bounds.Value,
            metadata.Crs,
            _geoReference,
            _terrain);

        if (mapping.UsedFallback)
            ReportLayerWarning(runId, slot.LayerId, mapping.Message);
        else
            _diagnostics?.MarkStatus($"layer mapped: {slot.LayerId}");

        var useTiled = metadata.HasTileSource && _layerConfig.EnableTiledRaster;
        slot.UseTiledSource = useTiled;
        if (useTiled)
        {
            slot.Overlay.SetVisible(false);
            slot.TiledSurface.ConfigureSource(metadata, mapping.TerrainBounds, _layerConfig.ConformToTerrain);
            slot.TiledSurface.SetVisible(slot.Visible);
            return;
        }

        slot.TiledSurface.DisableSource();
        slot.TiledSurface.SetVisible(false);
        slot.Overlay.ApplyBounds(mapping.TerrainBounds, _layerConfig.ConformToTerrain);
        slot.Overlay.SetVisible(slot.Visible);
    }

    private static void MergeMetadata(RasterMetadata target, RasterMetadata source)
    {
        if (source.Bounds != null && source.Bounds.Value.IsValid)
            target.Bounds = source.Bounds;
        if (!string.IsNullOrWhiteSpace(source.Crs))
            target.Crs = source.Crs;
        if (source.MinValue != null)
            target.MinValue = source.MinValue;
        if (source.MaxValue != null)
            target.MaxValue = source.MaxValue;
        if (source.NoDataValue != null)
            target.NoDataValue = source.NoDataValue;
        if (source.ValueScale != null)
            target.ValueScale = source.ValueScale;
        if (source.ValueOffset != null)
            target.ValueOffset = source.ValueOffset;
        if (source.ClassBreaks.Count > 0)
            target.ClassBreaks = source.ClassBreaks.ToArray();
        if (!string.IsNullOrWhiteSpace(source.Unit))
            target.Unit = source.Unit;
        if (!string.IsNullOrWhiteSpace(source.Palette))
            target.Palette = source.Palette;
        if (!string.IsNullOrWhiteSpace(source.TileTemplateUri))
            target.TileTemplateUri = source.TileTemplateUri;
        if (source.MinZoom.HasValue)
            target.MinZoom = source.MinZoom;
        if (source.MaxZoom.HasValue)
            target.MaxZoom = source.MaxZoom;
        if (source.TileSize.HasValue)
            target.TileSize = source.TileSize;
    }

    private Texture2D GetOrCreateRamp(string palette)
    {
        if (_ramps.TryGetValue(palette, out var existing))
            return existing;

        var created = ColorRampFactory.Build(palette);
        _ramps[palette] = created;
        return created;
    }

    private static string ResolvePalette(string layerId, RasterMetadata? metadata)
    {
        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Palette))
            return metadata.Palette;

        return layerId switch
        {
            "reliability_95" => "reliability",
            "reliability_80" => "reliability",
            "interference" => "interference",
            "capacity" => "capacity",
            _ => "default",
        };
    }

    private IEnumerator SendLayerState(string runId, string layerId, string state, double? progress, double? transitionMs, string message)
    {
        if (_bridge == null)
            yield break;

        var payload = BridgeProtocol.CreateLayerStateChanged(runId, layerId, state, progress, transitionMs, message);
        _ = _bridge.SendAsync(payload);
    }

    private void ReportLayerError(string runId, string layerId, string message)
    {
        _ = SendLayerState(runId, layerId, "failed", null, null, message);
        if (_bridge != null)
            _ = _bridge.SendAsync(BridgeProtocol.CreateErrorReport("layer_error", layerId, message, runId));
    }

    private void ReportLayerWarning(string runId, string layerId, string message)
    {
        if (_bridge != null)
            _ = _bridge.SendAsync(BridgeProtocol.CreateErrorReport("layer_warning", layerId, message, runId));
    }

    private void EmitAllPresentation()
    {
        foreach (var slot in _layers.Values)
            EmitPresentation(slot);
    }

    private void EmitPresentation(LayerSlot slot)
    {
        LayerPresentationChanged?.Invoke(this, BuildPresentation(slot));
    }

    private LayerPresentationChangedEventArgs BuildPresentation(LayerSlot slot)
    {
        var metadata = slot.Metadata;
        return new LayerPresentationChangedEventArgs
        {
            LayerId = slot.LayerId,
            DisplayName = slot.DisplayName,
            Visible = slot.Visible,
            Opacity = slot.Opacity,
            IsActive = string.Equals(slot.LayerId, _activeLayerId, StringComparison.Ordinal),
            Unit = metadata?.Unit ?? string.Empty,
            MinValue = metadata?.MinValue,
            MaxValue = metadata?.MaxValue,
            Palette = ResolvePalette(slot.LayerId, metadata),
            ClassBreaksText = metadata != null && metadata.ClassBreaks.Count > 0
                ? string.Join(", ", metadata.ClassBreaks.Select(static value => value.ToString("F2")))
                : string.Empty,
        };
    }

    private sealed class LayerSlot
    {
        public LayerSlot(
            string layerId,
            string displayName,
            GameObject layerRoot,
            LayerOverlay overlay,
            TiledLayerSurface tiledSurface,
            Color fallbackColor)
        {
            LayerId = layerId;
            DisplayName = displayName;
            LayerRoot = layerRoot;
            Overlay = overlay;
            TiledSurface = tiledSurface;
            FallbackColor = fallbackColor;
        }

        public string LayerId { get; }
        public string DisplayName { get; }
        public GameObject LayerRoot { get; }
        public LayerOverlay Overlay { get; }
        public TiledLayerSurface TiledSurface { get; }
        public Color FallbackColor { get; }
        public string? RasterUri { get; set; }
        public Texture2D? Texture { get; set; }
        public RasterMetadata? Metadata { get; set; }
        public bool Visible { get; set; }
        public bool UseTiledSource { get; set; }
        public float Opacity { get; set; } = 0.6f;
    }
}
}

