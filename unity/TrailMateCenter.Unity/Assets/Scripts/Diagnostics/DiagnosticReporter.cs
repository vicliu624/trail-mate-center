using System.Collections;
using TrailMateCenter.Unity.Bridge;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Diagnostics
{
public sealed class DiagnosticReporter : MonoBehaviour
{
    [SerializeField] private float _gpuMemoryMb = 800;
    private BridgeCoordinator? _bridge;
    private readonly FpsMonitor _fpsMonitor = new();
    private float _lastLayerLoadMs = 180;
    private float _tileCacheHitRate = 0.85f;
    private int _cacheHits;
    private int _cacheMisses;
    private string _lastMessage = "stable";
    private float _intervalSeconds = 1f;

    public void Initialize(BridgeCoordinator bridge, DiagnosticsConfig config)
    {
        _bridge = bridge;
        _intervalSeconds = Mathf.Max(0.2f, config.IntervalMs / 1000f);
        StartCoroutine(ReportLoop());
    }

    public void UpdateLayerLoadMs(float ms)
    {
        _lastLayerLoadMs = ms;
    }

    public void MarkCacheHit()
    {
        _cacheHits++;
        RecomputeCacheHitRate();
    }

    public void MarkCacheMiss()
    {
        _cacheMisses++;
        RecomputeCacheHitRate();
    }

    public void MarkStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _lastMessage = message;
    }

    private void Update()
    {
        _fpsMonitor.Tick(Time.deltaTime);
    }

    private IEnumerator ReportLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(_intervalSeconds);
            if (_bridge == null)
                continue;

            var payload = BridgeProtocol.CreateDiagnosticSnapshot(
                fps: _fpsMonitor.Fps,
                frameTimeP95Ms: _fpsMonitor.FrameTimeP95Ms,
                gpuMemoryMb: _gpuMemoryMb,
                layerLoadMs: _lastLayerLoadMs,
                tileCacheHitRate: _tileCacheHitRate,
                message: _lastMessage);

            _ = _bridge.SendAsync(payload);
        }
    }

    private void RecomputeCacheHitRate()
    {
        var total = _cacheHits + _cacheMisses;
        if (total <= 0)
            return;
        _tileCacheHitRate = Mathf.Clamp01(_cacheHits / (float)total);
    }
}
}

