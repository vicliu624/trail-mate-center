using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace TrailMateCenter.Unity.Core
{
[Serializable]
public sealed class AppConfig
{
    public BridgeConfig Bridge { get; set; } = new();
    public TerrainConfig Terrain { get; set; } = new();
    public LayerConfig Layers { get; set; } = new();
    public GeoReferenceConfig GeoReference { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public CameraConfig Camera { get; set; } = new();
    public DiagnosticsConfig Diagnostics { get; set; } = new();
    public InteractionConfig Interaction { get; set; } = new();

    public static AppConfig Load()
    {
        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, "trailmate_unity_config.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Config] missing config at {path}, using defaults.");
                return ApplyEnvironmentOverrides(new AppConfig());
            }

            var json = File.ReadAllText(path);
            var config = JsonConvert.DeserializeObject<AppConfig>(json);
            return ApplyEnvironmentOverrides(config ?? new AppConfig());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Config] failed to load config: {ex.Message}");
            return ApplyEnvironmentOverrides(new AppConfig());
        }
    }

    private static AppConfig ApplyEnvironmentOverrides(AppConfig config)
    {
        if (config == null)
            config = new AppConfig();

        if (config.Bridge == null)
            config.Bridge = new BridgeConfig();

        var bridgeMode = ReadEnvironmentValue("TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE");
        if (!string.IsNullOrWhiteSpace(bridgeMode))
            config.Bridge.Transport = bridgeMode;

        var pipeName = ReadEnvironmentValue("TRAILMATE_PROPAGATION_UNITY_PIPE_NAME");
        if (!string.IsNullOrWhiteSpace(pipeName))
            config.Bridge.PipeName = pipeName;

        var tcpHost = ReadEnvironmentValue("TRAILMATE_PROPAGATION_UNITY_TCP_HOST");
        if (!string.IsNullOrWhiteSpace(tcpHost))
            config.Bridge.TcpHost = tcpHost;

        var tcpPortRaw = ReadEnvironmentValue("TRAILMATE_PROPAGATION_UNITY_TCP_PORT");
        if (int.TryParse(tcpPortRaw, out var tcpPort) && tcpPort > 0)
            config.Bridge.TcpPort = tcpPort;

        var viewportId = ReadEnvironmentValue("TRAILMATE_PROPAGATION_UNITY_VIEWPORT_ID");
        if (!string.IsNullOrWhiteSpace(viewportId))
            config.Bridge.ViewportId = viewportId;

        return config;
    }

    private static string ReadEnvironmentValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

[Serializable]
public sealed class BridgeConfig
{
    public string Transport { get; set; } = "namedpipe"; // namedpipe | tcp
    public string PipeName { get; set; } = "TrailMateCenter.Propagation.Bridge";
    public string TcpHost { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 51110;
    public string ViewportId { get; set; } = "propagation-main-slot";
}

[Serializable]
public sealed class TerrainConfig
{
    public string HeightmapPath { get; set; } = string.Empty; // optional
    public int HeightmapResolution { get; set; } = 513;
    public float SizeX { get; set; } = 3000;
    public float SizeZ { get; set; } = 3000;
    public float HeightScale { get; set; } = 600;
    public float OffsetY { get; set; } = 0;
}

[Serializable]
public sealed class LayerConfig
{
    public float DefaultOverlayHeight { get; set; } = 2.5f;
    public string Palette { get; set; } = "default";
    public int OverlayResolution { get; set; } = 256;
    public bool ConformToTerrain { get; set; } = true;
    public float TerrainOffset { get; set; } = 1.5f;
    public bool EnableTiledRaster { get; set; } = true;
    public float TileRefreshIntervalSeconds { get; set; } = 0.4f;
    public int TileConformingResolution { get; set; } = 24;
    public int MaxActiveTiles { get; set; } = 160;
    public bool AllowLayerStacking { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public bool ShowEvidenceCards { get; set; } = true;
    public RasterConversionConfig RasterConversion { get; set; } = new();
}

[Serializable]
public sealed class GeoReferenceConfig
{
    public string TerrainCrs { get; set; } = "LOCAL_TERRAIN_M";
    public double TerrainMinX { get; set; } = 0;
    public double TerrainMinZ { get; set; } = 0;
    public double TerrainMaxX { get; set; } = 3000;
    public double TerrainMaxZ { get; set; } = 3000;
    public bool EnableAoiBoundaryMask { get; set; }
    public double AoiMinX { get; set; } = 0;
    public double AoiMinZ { get; set; } = 0;
    public double AoiMaxX { get; set; } = 3000;
    public double AoiMaxZ { get; set; } = 3000;
}

[Serializable]
public sealed class PerformanceConfig
{
    public int TextureCacheCapacity { get; set; } = 24;
    public int OverlayMeshLodNear { get; set; } = 256;
    public int OverlayMeshLodFar { get; set; } = 96;
    public float OverlayMeshLodDistance { get; set; } = 1800f;
    public bool EnableProgressiveLayerLoading { get; set; } = true;
}

[Serializable]
public sealed class CameraConfig
{
    public Vector3 StartPosition { get; set; } = new(1200, 1800, 900);
    public EulerAngles StartRotation { get; set; } = new();
    public float Fov { get; set; } = 55;
}

[Serializable]
public sealed class DiagnosticsConfig
{
    public int IntervalMs { get; set; } = 1000;
}

[Serializable]
public sealed class InteractionConfig
{
    public string ProfileLineModifier { get; set; } = "LeftShift";
    public string ProfileLineColor { get; set; } = "#66C6FF";
}

[Serializable]
public sealed class RasterConversionConfig
{
    public bool Enable { get; set; } = false;
    public string GdalTranslatePath { get; set; } = "gdal_translate";
    public string GdalInfoPath { get; set; } = "gdalinfo";
    public string GdalWarpPath { get; set; } = "gdalwarp";
    public string OutputFormat { get; set; } = "png";
    public string CacheDirectory { get; set; } = string.Empty;
    public int TimeoutMs { get; set; } = 10000;
    public bool EnableReprojection { get; set; } = true;
    public string TargetCrs { get; set; } = "LOCAL_TERRAIN_M";
    public int PreferredBand { get; set; } = 1;
    public bool AutoScaleToByte { get; set; } = true;
    public int MaxTextureSize { get; set; } = 4096;
    public bool GenerateMipMaps { get; set; } = true;
}

[Serializable]
public struct EulerAngles
{
    public float Pitch;
    public float Yaw;
    public float Roll;
}
}

