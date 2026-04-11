using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class SceneGeometryRenderer : MonoBehaviour
{
    private readonly List<GameObject> _spawned = new();
    private Terrain? _terrain;

    public void Initialize(Terrain terrain)
    {
        _terrain = terrain;
    }

    public void ApplyResult(JObject payload)
    {
        Clear();

        var geometry = (payload["sceneGeometry"] as JObject) ?? (payload["scene_geometry"] as JObject);
        if (geometry == null)
            return;

        AddPoints((geometry["relayCandidates"] as JArray) ?? (geometry["relay_candidates"] as JArray), new Color(0.95f, 0.74f, 0.20f, 1f), 9f);
        AddPoints((geometry["relayRecommendations"] as JArray) ?? (geometry["relay_recommendations"] as JArray), new Color(0.20f, 0.85f, 0.34f, 1f), 13f);
        AddPoints((geometry["profileObstacles"] as JArray) ?? (geometry["profile_obstacles"] as JArray), new Color(0.92f, 0.25f, 0.28f, 1f), 11f);
        AddLines((geometry["profileLines"] as JArray) ?? (geometry["profile_lines"] as JArray), new Color(0.35f, 0.76f, 0.98f, 0.95f), 2.2f);
    }

    private void AddPoints(JArray? points, Color color, float size)
    {
        if (points == null)
            return;

        foreach (var item in points)
        {
            if (item is not JObject point)
                continue;

            var x = point.Value<float?>("x") ?? 0f;
            var z = point.Value<float?>("z") ?? 0f;
            var y = point.Value<float?>("y");
            var worldY = y ?? ResolveGroundY(x, z) + 2f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"GeoPoint_{point.Value<string>("id") ?? "node"}";
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = new Vector3(x, worldY, z);
            go.transform.localScale = Vector3.one * size;

            if (go.TryGetComponent<MeshRenderer>(out var renderer))
            {
                var material = RuntimeMaterialFactory.CreateSurfaceMaterial(color);
                if (material != null)
                    renderer.material = material;
            }

            _spawned.Add(go);
        }
    }

    private void AddLines(JArray? lines, Color color, float width)
    {
        if (lines == null)
            return;

        foreach (var item in lines)
        {
            if (item is not JObject lineObj)
                continue;

            var points = lineObj["points"] as JArray;
            if (points == null || points.Count < 2)
                continue;

            var go = new GameObject("GeoLine");
            go.transform.SetParent(transform, worldPositionStays: true);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = points.Count;
            lr.startWidth = width;
            lr.endWidth = width;

            var material = RuntimeMaterialFactory.CreateLineMaterial(color);
            if (material != null)
                lr.material = material;

            lr.startColor = color;
            lr.endColor = color;

            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is not JObject p)
                    continue;

                var x = p.Value<float?>("x") ?? 0f;
                var z = p.Value<float?>("z") ?? 0f;
                var y = p.Value<float?>("y") ?? (ResolveGroundY(x, z) + 3f);
                lr.SetPosition(i, new Vector3(x, y, z));
            }

            _spawned.Add(go);
        }
    }

    private float ResolveGroundY(float x, float z)
    {
        if (_terrain == null)
            return 0f;

        var origin = _terrain.GetPosition();
        return _terrain.SampleHeight(new Vector3(x, 0, z)) + origin.y;
    }

    private void Clear()
    {
        foreach (var go in _spawned)
        {
            if (go != null)
                Destroy(go);
        }

        _spawned.Clear();
    }
}
}
