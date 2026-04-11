using UnityEngine;
namespace TrailMateCenter.Unity.Diagnostics
{
public sealed class FpsMonitor
{
    private float _elapsed;
    private int _frames;
    private float _fps;
    private float _frameTimeP95;

    public void Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        _frames++;

        if (_elapsed >= 1f)
        {
            _fps = _frames / _elapsed;
            _frameTimeP95 = _fps <= 0 ? 0 : (1000f / _fps) * 1.6f;
            _elapsed = 0;
            _frames = 0;
        }
    }

    public float Fps => _fps;
    public float FrameTimeP95Ms => _frameTimeP95;
}
}

