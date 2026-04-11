using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Bridge;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Interaction
{
public sealed class InteractionController : MonoBehaviour
{
    [SerializeField] private Camera _camera = null!;
    private BridgeCoordinator? _bridge;
    private Terrain? _terrain;
    private KeyCode _profileModifier = KeyCode.LeftShift;
    private Vector3? _profileStart;
    private LineRenderer? _profileLine;
    private LineRenderer? _profileTerrainLine;
    private Vector3? _measureStart;
    private LineRenderer? _measureLine;
    private int _annotationCounter;
    private Color _profileLineColor = new(0.4f, 0.78f, 1f, 0.9f);

    public void Initialize(BridgeCoordinator bridge, Camera camera, Terrain terrain, InteractionConfig config)
    {
        _bridge = bridge;
        _camera = camera;
        _terrain = terrain;
        _profileModifier = ParseModifier(config.ProfileLineModifier);
        _profileLineColor = ParseHex(config.ProfileLineColor, _profileLineColor);
        ConfigureLineRenderer();
    }

    private void Update()
    {
        if (_bridge == null || _camera == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            if (TryPickPoint(out var point))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    HandleMeasureClick(point);
                }
                else if (Input.GetKey(KeyCode.A))
                {
                    HandleAnnotation(point);
                }
                else if (Input.GetKey(KeyCode.H))
                {
                    HandleHotspot(point);
                }
                else if (Input.GetKey(_profileModifier))
                {
                    HandleProfileClick(point);
                }
                else
                {
                    var payload = BridgeProtocol.CreateMapPointSelected(point.x, point.z, "node_auto");
                    _ = _bridge.SendAsync(payload);
                }
            }
        }
    }

    private bool TryPickPoint(out Vector3 point)
    {
        point = Vector3.zero;
        var ray = _camera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 5000))
        {
            point = hit.point;
            return true;
        }
        return false;
    }

    private void HandleProfileClick(Vector3 point)
    {
        if (_profileStart == null)
        {
            _profileStart = point;
            UpdateProfileLine(point, point);
            return;
        }

        var start = _profileStart.Value;
        var end = point;
        _profileStart = null;
        UpdateProfileLine(start, end);

        if (_bridge == null)
            return;

        var payload = BridgeProtocol.CreateProfileLineChanged(start.x, start.z, end.x, end.z);
        _ = _bridge.SendAsync(payload);
    }

    private void ConfigureLineRenderer()
    {
        _profileLine = CreateLineRenderer("ProfileLine", 3f, _profileLineColor);
        _profileLine.positionCount = 2;
        _profileLine.enabled = false;

        _profileTerrainLine = CreateLineRenderer("ProfileTerrainLine", 1.5f, new Color(0.97f, 0.76f, 0.32f, 0.95f));
        _profileTerrainLine.positionCount = 0;
        _profileTerrainLine.enabled = false;

        _measureLine = CreateLineRenderer("MeasureLine", 2.5f, new Color(0.98f, 0.98f, 0.36f, 0.95f));
        _measureLine.positionCount = 2;
        _measureLine.enabled = false;
    }

    private LineRenderer CreateLineRenderer(string name, float width, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;

        var material = RuntimeMaterialFactory.CreateLineMaterial(color);
        if (material != null)
            line.material = material;

        line.startColor = color;
        line.endColor = color;
        return line;
    }

    private void UpdateProfileLine(Vector3 start, Vector3 end)
    {
        if (_profileLine == null)
            return;

        var offset = new Vector3(0, 2f, 0);
        _profileLine.SetPosition(0, start + offset);
        _profileLine.SetPosition(1, end + offset);
        _profileLine.enabled = true;
        UpdateProfileTerrainCurve(start, end);
        SendProfileCurveSummary(start, end);
    }

    private void HandleMeasureClick(Vector3 point)
    {
        if (_measureStart == null)
        {
            _measureStart = point;
            if (_measureLine != null)
            {
                _measureLine.enabled = true;
                _measureLine.SetPosition(0, point + new Vector3(0, 2f, 0));
                _measureLine.SetPosition(1, point + new Vector3(0, 2f, 0));
            }
            return;
        }

        var start = _measureStart.Value;
        var end = point;
        _measureStart = null;
        if (_measureLine != null)
        {
            _measureLine.SetPosition(0, start + new Vector3(0, 2f, 0));
            _measureLine.SetPosition(1, end + new Vector3(0, 2f, 0));
        }

        var distance = Vector3.Distance(start, end);
        var payload = BridgeProtocol.CreateInteractionEvent("measurement_completed", new JObject
        {
            ["start_x"] = start.x,
            ["start_y"] = start.z,
            ["end_x"] = end.x,
            ["end_y"] = end.z,
            ["distance_m"] = distance
        });
        _ = _bridge?.SendAsync(payload);
    }

    private void HandleAnnotation(Vector3 point)
    {
        _annotationCounter++;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = $"annotation_{_annotationCounter}";
        marker.transform.SetParent(transform, worldPositionStays: true);
        marker.transform.position = point + new Vector3(0, 3f, 0);
        marker.transform.localScale = new Vector3(4f, 6f, 4f);
        if (marker.TryGetComponent<MeshRenderer>(out var renderer))
        {
            var material = RuntimeMaterialFactory.CreateSurfaceMaterial(new Color(0.97f, 0.42f, 0.22f, 1f));
            if (material != null)
                renderer.material = material;
        }

        var payload = BridgeProtocol.CreateInteractionEvent("annotation_added", new JObject
        {
            ["id"] = marker.name,
            ["x"] = point.x,
            ["y"] = point.z,
            ["z"] = point.y
        });
        _ = _bridge?.SendAsync(payload);
    }

    private void HandleHotspot(Vector3 point)
    {
        if (_terrain == null)
            return;

        var radius = 32f;
        var samples = 16;
        var sum = 0f;
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < samples; i++)
        {
            var angle = (Mathf.PI * 2f) * i / samples;
            var p = new Vector3(
                point.x + Mathf.Cos(angle) * radius,
                0,
                point.z + Mathf.Sin(angle) * radius);
            var h = _terrain.SampleHeight(p) + _terrain.GetPosition().y;
            sum += h;
            min = Mathf.Min(min, h);
            max = Mathf.Max(max, h);
        }

        var avg = sum / samples;
        var payload = BridgeProtocol.CreateInteractionEvent("hotspot_stats", new JObject
        {
            ["x"] = point.x,
            ["y"] = point.z,
            ["radius_m"] = radius,
            ["elevation_min"] = min,
            ["elevation_max"] = max,
            ["elevation_avg"] = avg
        });
        _ = _bridge?.SendAsync(payload);
    }

    private void UpdateProfileTerrainCurve(Vector3 start, Vector3 end)
    {
        if (_terrain == null || _profileTerrainLine == null)
            return;

        const int samples = 64;
        _profileTerrainLine.positionCount = samples;
        _profileTerrainLine.enabled = true;

        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)(samples - 1);
            var x = Mathf.Lerp(start.x, end.x, t);
            var z = Mathf.Lerp(start.z, end.z, t);
            var y = _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.GetPosition().y + 4f;
            _profileTerrainLine.SetPosition(i, new Vector3(x, y, z));
        }
    }

    private void SendProfileCurveSummary(Vector3 start, Vector3 end)
    {
        if (_terrain == null || _bridge == null)
            return;

        const int samples = 64;
        var min = float.MaxValue;
        var max = float.MinValue;
        var sum = 0f;

        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)(samples - 1);
            var x = Mathf.Lerp(start.x, end.x, t);
            var z = Mathf.Lerp(start.z, end.z, t);
            var y = _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.GetPosition().y;
            min = Mathf.Min(min, y);
            max = Mathf.Max(max, y);
            sum += y;
        }

        var avg = sum / samples;
        var payload = BridgeProtocol.CreateInteractionEvent("profile_curve_summary", new JObject
        {
            ["start_x"] = start.x,
            ["start_y"] = start.z,
            ["end_x"] = end.x,
            ["end_y"] = end.z,
            ["elevation_min"] = min,
            ["elevation_max"] = max,
            ["elevation_avg"] = avg,
            ["distance_m"] = Vector3.Distance(start, end)
        });
        _ = _bridge.SendAsync(payload);
    }

    private static KeyCode ParseModifier(string name)
    {
        if (Enum.TryParse(name, ignoreCase: true, out KeyCode key))
            return key;
        return KeyCode.LeftShift;
    }

    private static Color ParseHex(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        var trimmed = hex.TrimStart('#');
        if (trimmed.Length != 6 && trimmed.Length != 8)
            return fallback;

        if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return fallback;

        var r = ((value >> 24) & 0xFF) / 255f;
        var g = ((value >> 16) & 0xFF) / 255f;
        var b = ((value >> 8) & 0xFF) / 255f;
        var a = (value & 0xFF) / 255f;

        if (trimmed.Length == 6)
        {
            r = ((value >> 16) & 0xFF) / 255f;
            g = ((value >> 8) & 0xFF) / 255f;
            b = (value & 0xFF) / 255f;
            a = 1f;
        }

        return new Color(r, g, b, a);
    }
}
}



