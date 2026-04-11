using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class AoiBoundaryRenderer : MonoBehaviour
{
    private LineRenderer? _line;

    public void Initialize(GeoReferenceConfig config, Terrain terrain)
    {
        if (!config.EnableAoiBoundaryMask)
            return;

        var color = new Color(0.95f, 0.88f, 0.22f, 0.95f);
        var go = new GameObject("AoiBoundary");
        go.transform.SetParent(transform, false);
        _line = go.AddComponent<LineRenderer>();
        _line.positionCount = 5;
        _line.startWidth = 3f;
        _line.endWidth = 3f;

        var material = RuntimeMaterialFactory.CreateLineMaterial(color);
        if (material != null)
            _line.material = material;

        _line.startColor = color;
        _line.endColor = color;

        var y = terrain.GetPosition().y + 5f;
        var p0 = new Vector3((float)config.AoiMinX, y, (float)config.AoiMinZ);
        var p1 = new Vector3((float)config.AoiMaxX, y, (float)config.AoiMinZ);
        var p2 = new Vector3((float)config.AoiMaxX, y, (float)config.AoiMaxZ);
        var p3 = new Vector3((float)config.AoiMinX, y, (float)config.AoiMaxZ);
        _line.SetPosition(0, p0);
        _line.SetPosition(1, p1);
        _line.SetPosition(2, p2);
        _line.SetPosition(3, p3);
        _line.SetPosition(4, p0);
    }
}
}
