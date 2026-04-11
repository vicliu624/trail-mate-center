using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Bridge
{
public sealed class BridgeCoordinator : MonoBehaviour
{
    private AppConfig _config = null!;
    private BridgeTransport _transport = null!;
    private CancellationTokenSource? _cts;
    private SceneContext? _scene;
    private string _currentRunId = string.Empty;
    private string _activeLayerId = "coverage_mean";

    private void Awake()
    {
        _config = AppConfig.Load();
        _transport = new BridgeTransport(_config.Bridge);
        _transport.EnvelopeReceived += OnEnvelopeReceived;
        _transport.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunBridgeLoopAsync(_cts.Token);

        try
        {
            _scene = SceneFactory.Build(_config, this);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Debug.LogWarning("[Bridge] Scene bootstrap failed. Bridge transport will continue without visuals.");
        }
    }

    private void OnDestroy()
    {
        _transport.EnvelopeReceived -= OnEnvelopeReceived;
        _transport.ConnectionStateChanged -= OnConnectionStateChanged;
        _cts?.Cancel();
        _transport.Dispose();
    }

    public Task SendAsync(JObject payload)
    {
        if (_cts == null)
            return Task.CompletedTask;
        return _transport.SendAsync(payload, _cts.Token);
    }

    private void OnEnvelopeReceived(BridgeEnvelope envelope)
    {
        MainThreadDispatcher.Post(() => HandleEnvelope(envelope));
    }

    private void HandleEnvelope(BridgeEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "attach_viewport":
                HandleAttachViewport(envelope);
                break;
            case "push_request":
                HandlePushRequest(envelope);
                break;
            case "push_result":
                HandlePushResult(envelope);
                break;
            case "set_active_layer":
                HandleSetActiveLayer(envelope);
                break;
            case "set_camera_state":
                HandleSetCameraState(envelope);
                break;
            case "heartbeat":
                _ = SendAck(envelope, "heartbeat", envelope.RunId, "ok");
                break;
            default:
                _ = SendAck(envelope, envelope.Type, envelope.RunId, "unknown command");
                break;
        }
    }

    private void HandleAttachViewport(BridgeEnvelope envelope)
    {
        var viewportId = envelope.Payload.Value<string>("viewport_id") ?? string.Empty;
        _ = SendAck(envelope, "attach_viewport", envelope.RunId, $"viewport {viewportId}");
        _ = SendAsync(BridgeProtocol.CreateBridgeState(true, "viewport attached"));
    }

    private void HandlePushRequest(BridgeEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.RunId))
            _currentRunId = envelope.RunId;

        _ = SendAck(envelope, "push_request", envelope.RunId, "accepted");
    }

    private void HandlePushResult(BridgeEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.RunId))
            _currentRunId = envelope.RunId;

        _ = SendAck(envelope, "push_result", envelope.RunId, "accepted");

        if (_scene?.LayerManager != null)
        {
            _scene.LayerManager.ApplyResult(_currentRunId, envelope.Payload);
            if (!string.IsNullOrWhiteSpace(_activeLayerId))
                _scene.LayerManager.RequestActivateLayer(_activeLayerId, _currentRunId);
        }

        _scene?.GeometryRenderer?.ApplyResult(envelope.Payload);
        _scene?.Hud?.ApplyResult(envelope.Payload);
    }

    private void HandleSetActiveLayer(BridgeEnvelope envelope)
    {
        var layerIds = ParseLayerIds(envelope.Payload);
        var layerId = layerIds.Count > 0
            ? layerIds[0]
            : envelope.Payload.Value<string>("layer_id") ?? string.Empty;
        var runId = envelope.Payload.Value<string>("run_id") ?? envelope.RunId;

        if (!string.IsNullOrWhiteSpace(layerId))
            _activeLayerId = layerId;

        _ = SendAck(envelope, "set_active_layer", runId, $"layer {layerId}");
        if (layerIds.Count > 0)
            _scene?.LayerManager?.RequestActivateLayers(layerIds, runId);
        else
            _scene?.LayerManager?.RequestActivateLayer(layerId, runId);

        ApplyLayerOverrides(envelope.Payload);
    }

    private void HandleSetCameraState(BridgeEnvelope envelope)
    {
        var state = ParseCameraState(envelope.Payload);
        _ = SendAck(envelope, "set_camera_state", envelope.RunId, "camera applied");
        _scene?.CameraRig?.ApplyCameraState(state);
    }

    private async Task RunBridgeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _transport.StartAsync(token);
                if (_transport.ListenTask != null)
                    await _transport.ListenTask;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bridge] connection error: {ex.Message}");
            }
            finally
            {
                _transport.Stop();
            }

            if (token.IsCancellationRequested)
                break;

            try
            {
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnConnectionStateChanged(string state)
    {
        MainThreadDispatcher.Post(() => HandleConnectionStateChanged(state));
    }

    private void HandleConnectionStateChanged(string state)
    {
        if (state != "connected")
            return;

        _ = SendAsync(BridgeProtocol.CreateBridgeState(true, "bridge connected"));
        var camera = _scene?.CameraRig;
        if (camera == null)
            return;

        var snapshot = camera.CaptureState();
        _ = SendAsync(BridgeProtocol.CreateCameraStateChanged(snapshot, "camera ready"));
    }

    private Task SendAck(BridgeEnvelope envelope, string action, string runId, string detail)
    {
        var ack = BridgeProtocol.CreateAck(envelope.CorrelationId, action, runId, detail);
        return SendAsync(ack);
    }

    private static CameraState ParseCameraState(JObject payload)
    {
        return new CameraState
        {
            X = (float?)payload.Value<double?>("x") ?? 0,
            Y = (float?)payload.Value<double?>("y") ?? 0,
            Z = (float?)payload.Value<double?>("z") ?? 0,
            Pitch = (float?)payload.Value<double?>("pitch") ?? 0,
            Yaw = (float?)payload.Value<double?>("yaw") ?? 0,
            Roll = (float?)payload.Value<double?>("roll") ?? 0,
            Fov = (float?)payload.Value<double?>("fov") ?? 55
        };
    }

    private static List<string> ParseLayerIds(JObject payload)
    {
        if (payload["layer_ids"] is not JArray arr)
            return new List<string>();

        return arr
            .Select(token => token?.ToString() ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void ApplyLayerOverrides(JObject payload)
    {
        if (_scene?.LayerManager == null)
            return;

        if (payload["layer_visibility"] is JObject visibility)
        {
            foreach (var prop in visibility.Properties())
            {
                if (prop.Value.Type == JTokenType.Boolean)
                    _scene.LayerManager.SetLayerVisibility(prop.Name, prop.Value.Value<bool>());
            }
        }

        if (payload["layer_opacity"] is JObject opacity)
        {
            foreach (var prop in opacity.Properties())
            {
                if (prop.Value.Type != JTokenType.Float && prop.Value.Type != JTokenType.Integer)
                    continue;
                _scene.LayerManager.SetLayerOpacity(prop.Name, Mathf.Clamp01(prop.Value.Value<float>()));
            }
        }

        if (payload["layer_order"] is JArray order)
        {
            var list = order
                .Select(token => token?.ToString() ?? string.Empty)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            _scene.LayerManager.SetLayerOrder(list);
        }
    }
}
}



