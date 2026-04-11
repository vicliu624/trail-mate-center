using System;
using Newtonsoft.Json.Linq;
namespace TrailMateCenter.Unity.Bridge
{
public static class BridgeProtocol
{
    public static JObject CreateAck(string correlationId, string action, string runId, string detail)
    {
        return new JObject
        {
            ["type"] = "ack",
            ["correlation_id"] = correlationId ?? string.Empty,
            ["payload"] = new JObject
            {
                ["action"] = action ?? string.Empty,
                ["run_id"] = runId ?? string.Empty,
                ["detail"] = detail ?? string.Empty,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
    }

    public static JObject CreateBridgeState(bool attached, string message)
    {
        return new JObject
        {
            ["type"] = "bridge_state",
            ["payload"] = new JObject
            {
                ["attached"] = attached,
                ["message"] = message ?? string.Empty,
            },
        };
    }

    public static JObject CreateLayerStateChanged(
        string runId,
        string layerId,
        string state,
        double? progress,
        double? transitionMs,
        string message)
    {
        return new JObject
        {
            ["type"] = "layer_state_changed",
            ["payload"] = new JObject
            {
                ["run_id"] = runId ?? string.Empty,
                ["layer_id"] = layerId ?? string.Empty,
                ["state"] = state ?? string.Empty,
                ["progress"] = progress,
                ["transition_ms"] = transitionMs,
                ["message"] = message ?? string.Empty,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
    }

    public static JObject CreateDiagnosticSnapshot(
        double fps,
        double frameTimeP95Ms,
        double gpuMemoryMb,
        double layerLoadMs,
        double tileCacheHitRate,
        string message)
    {
        return new JObject
        {
            ["type"] = "diagnostic_snapshot",
            ["payload"] = new JObject
            {
                ["fps"] = fps,
                ["frame_time_p95_ms"] = frameTimeP95Ms,
                ["gpu_memory_mb"] = gpuMemoryMb,
                ["layer_load_ms"] = layerLoadMs,
                ["tile_cache_hit_rate"] = tileCacheHitRate,
                ["message"] = message ?? string.Empty,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
    }

    public static JObject CreateCameraStateChanged(CameraState state, string message)
    {
        return new JObject
        {
            ["type"] = "camera_state_changed",
            ["payload"] = new JObject
            {
                ["x"] = state.X,
                ["y"] = state.Y,
                ["z"] = state.Z,
                ["pitch"] = state.Pitch,
                ["yaw"] = state.Yaw,
                ["roll"] = state.Roll,
                ["fov"] = state.Fov,
                ["message"] = message ?? string.Empty,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
    }

    public static JObject CreateMapPointSelected(double x, double y, string nodeId)
    {
        return new JObject
        {
            ["type"] = "map_point_selected",
            ["payload"] = new JObject
            {
                ["x"] = x,
                ["y"] = y,
                ["node_id"] = nodeId ?? string.Empty,
            },
        };
    }

    public static JObject CreateProfileLineChanged(double sx, double sy, double ex, double ey)
    {
        return new JObject
        {
            ["type"] = "profile_line_changed",
            ["payload"] = new JObject
            {
                ["start_x"] = sx,
                ["start_y"] = sy,
                ["end_x"] = ex,
                ["end_y"] = ey,
            },
        };
    }

    public static JObject CreateInteractionEvent(string eventType, JObject payload)
    {
        return new JObject
        {
            ["type"] = "interaction_event",
            ["payload"] = new JObject
            {
                ["event_type"] = eventType ?? string.Empty,
                ["data"] = payload ?? new JObject(),
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            }
        };
    }

    public static JObject CreateErrorReport(string category, string source, string message, string runId)
    {
        return new JObject
        {
            ["type"] = "error_report",
            ["payload"] = new JObject
            {
                ["category"] = category ?? string.Empty,
                ["source"] = source ?? string.Empty,
                ["message"] = message ?? string.Empty,
                ["run_id"] = runId ?? string.Empty,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
        };
    }
}

public struct CameraState
{
    public float X;
    public float Y;
    public float Z;
    public float Pitch;
    public float Yaw;
    public float Roll;
    public float Fov;
}
}

