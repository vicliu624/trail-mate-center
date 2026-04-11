using System;
using System.Collections.Generic;
using System.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class LayerOverlay : MonoBehaviour
{
    private const int MaxClassBreaks = 16;
    private MeshRenderer? _renderer;
    private Material? _material;
    private MeshFilter? _meshFilter;
    private string _layerId = string.Empty;
    private Texture2D? _rampTexture;
    private Terrain? _terrain;
    private int _resolution;
    private int _resolutionNear;
    private int _resolutionFar;
    private float _lodDistance;
    private int _lastAppliedResolution;
    private float _offset;
    private float _height;
    private float _alpha = 0.6f;
    private float _valueMin = 0f;
    private float _valueMax = 1f;
    private float _noData = -10f;
    private float _hasNoData;
    private float _valueScale = 1f;
    private float _valueOffset;
    private readonly float[] _classBreaks = new float[MaxClassBreaks];
    private int _classBreakCount;
    private RasterBounds? _activeBounds;

    public void Initialize(string layerId, Terrain? terrain, int resolution, float width, float depth, float height, float offset, Color color)
    {
        _layerId = layerId;
        _terrain = terrain;
        _resolution = resolution;
        _resolutionNear = resolution;
        _resolutionFar = Mathf.Max(16, resolution / 2);
        _lodDistance = 0f;
        _lastAppliedResolution = resolution;
        _offset = offset;
        _height = height;
        _alpha = color.a;

        _meshFilter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();
        var center = transform.position;
        _meshFilter.sharedMesh = terrain == null
            ? BuildQuad(width, depth)
            : BuildConformingMesh(terrain, resolution, offset, center, null);

        _material = CreateMaterial();
        ApplyColor(color);
        _renderer.sharedMaterial = _material;

        if (terrain == null)
        {
            var pos = transform.position;
            transform.position = new Vector3(pos.x, height, pos.z);
        }
    }

    public string LayerId => _layerId;

    public void SetVisible(bool visible)
    {
        if (_renderer != null)
            _renderer.enabled = visible;
    }

    public void SetRampTexture(Texture2D ramp)
    {
        _rampTexture = ramp;
        if (_material != null && _material.HasProperty("_RampTex"))
            _material.SetTexture("_RampTex", ramp);
    }

    public void SetOpacity(float alpha)
    {
        _alpha = alpha;
        if (_material != null && _material.HasProperty("_Alpha"))
            _material.SetFloat("_Alpha", alpha);
    }

    public void ConfigureLod(int nearResolution, int farResolution, float distanceThreshold)
    {
        _resolutionNear = Mathf.Clamp(nearResolution, 16, 512);
        _resolutionFar = Mathf.Clamp(farResolution, 16, 512);
        _lodDistance = Mathf.Max(100f, distanceThreshold);
    }

    public void SetValueMapping(
        double? minValue,
        double? maxValue,
        double? noDataValue,
        double? valueScale,
        double? valueOffset,
        IReadOnlyList<double>? classBreaks)
    {
        _valueMin = minValue.HasValue ? (float)minValue.Value : 0f;
        _valueMax = maxValue.HasValue ? (float)maxValue.Value : 1f;
        if (_valueMax <= _valueMin)
            _valueMax = _valueMin + 1e-4f;

        if (valueScale.HasValue)
        {
            _valueScale = (float)valueScale.Value;
            _valueOffset = (float)(valueOffset ?? 0);
        }
        else if (_valueMin < 0f || _valueMax > 1f)
        {
            _valueScale = _valueMax - _valueMin;
            _valueOffset = _valueMin;
        }
        else
        {
            _valueScale = 1f;
            _valueOffset = 0f;
        }

        if (noDataValue.HasValue)
        {
            _hasNoData = 1f;
            _noData = (float)noDataValue.Value;
        }
        else
        {
            _hasNoData = 0f;
            _noData = -10f;
        }

        ApplyClassBreaks(classBreaks);

        if (_material == null)
            return;

        if (_material.HasProperty("_ValueMin"))
            _material.SetFloat("_ValueMin", _valueMin);
        if (_material.HasProperty("_ValueMax"))
            _material.SetFloat("_ValueMax", _valueMax);
        if (_material.HasProperty("_NoData"))
            _material.SetFloat("_NoData", _noData);
        if (_material.HasProperty("_HasNoData"))
            _material.SetFloat("_HasNoData", _hasNoData);
        if (_material.HasProperty("_ValueScale"))
            _material.SetFloat("_ValueScale", _valueScale);
        if (_material.HasProperty("_ValueOffset"))
            _material.SetFloat("_ValueOffset", _valueOffset);
        if (_material.HasProperty("_ClassBreakCount"))
            _material.SetFloat("_ClassBreakCount", _classBreakCount);
        if (_material.HasProperty("_ClassBreaks"))
            _material.SetFloatArray("_ClassBreaks", _classBreaks);
    }

    private void ApplyClassBreaks(IReadOnlyList<double>? classBreaks)
    {
        Array.Clear(_classBreaks, 0, _classBreaks.Length);
        if (classBreaks == null || classBreaks.Count == 0)
        {
            _classBreakCount = 0;
            return;
        }

        var ordered = classBreaks
            .Take(MaxClassBreaks)
            .OrderBy(static value => value)
            .ToArray();
        _classBreakCount = ordered.Length;
        for (var i = 0; i < ordered.Length; i++)
            _classBreaks[i] = (float)ordered[i];
    }

    public void ApplyBounds(RasterBounds bounds, bool conformToTerrain)
    {
        if (_meshFilter == null)
            return;

        var width = (float)(bounds.MaxX - bounds.MinX);
        var depth = (float)(bounds.MaxZ - bounds.MinZ);
        var center = new Vector3(
            (float)((bounds.MinX + bounds.MaxX) * 0.5),
            transform.position.y,
            (float)((bounds.MinZ + bounds.MaxZ) * 0.5));
        _activeBounds = bounds;

        if (conformToTerrain && _terrain != null)
        {
            var resolution = ResolveRuntimeResolution();
            _lastAppliedResolution = resolution;
            _meshFilter.sharedMesh = BuildConformingMesh(_terrain, resolution, _offset, center, bounds);
            transform.position = center;
        }
        else
        {
            _meshFilter.sharedMesh = BuildQuad(width, depth);
            transform.position = new Vector3(center.x, _height, center.z);
        }
    }

    private void Update()
    {
        if (_terrain == null || _meshFilter == null || _lodDistance <= 0f)
            return;

        var camera = Camera.main;
        if (camera == null)
            return;

        var resolution = ResolveRuntimeResolution();
        if (resolution == _lastAppliedResolution)
            return;

        var center = transform.position;
        _meshFilter.sharedMesh = BuildConformingMesh(_terrain, resolution, _offset, center, _activeBounds);
        _lastAppliedResolution = resolution;
    }

    private int ResolveRuntimeResolution()
    {
        var camera = Camera.main;
        if (camera == null)
            return _resolutionNear;

        var distance = Vector3.Distance(camera.transform.position, transform.position);
        return distance <= _lodDistance ? _resolutionNear : _resolutionFar;
    }

    public void ApplyColor(Color color)
    {
        if (_material == null)
            return;

        var shader = Shader.Find("Unlit/Color");
        if (shader != null)
            _material.shader = shader;
        _material.color = color;
    }

    public void ApplyTexture(Texture2D texture)
    {
        if (_material == null)
            return;

        var shader = Shader.Find("TrailMateCenter/OverlayRamp")
                     ?? Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Unlit/Texture")
                     ?? _material.shader;
        _material.shader = shader;
        _material.mainTexture = texture;
        _material.color = Color.white;
        if (_rampTexture != null && _material.HasProperty("_RampTex"))
            _material.SetTexture("_RampTex", _rampTexture);
        if (_material.HasProperty("_Alpha"))
            _material.SetFloat("_Alpha", _alpha);
        if (_material.HasProperty("_ValueMin"))
            _material.SetFloat("_ValueMin", _valueMin);
        if (_material.HasProperty("_ValueMax"))
            _material.SetFloat("_ValueMax", _valueMax);
        if (_material.HasProperty("_NoData"))
            _material.SetFloat("_NoData", _noData);
        if (_material.HasProperty("_HasNoData"))
            _material.SetFloat("_HasNoData", _hasNoData);
        if (_material.HasProperty("_ValueScale"))
            _material.SetFloat("_ValueScale", _valueScale);
        if (_material.HasProperty("_ValueOffset"))
            _material.SetFloat("_ValueOffset", _valueOffset);
        if (_material.HasProperty("_ClassBreakCount"))
            _material.SetFloat("_ClassBreakCount", _classBreakCount);
        if (_material.HasProperty("_ClassBreaks"))
            _material.SetFloatArray("_ClassBreaks", _classBreaks);
    }

    private static Mesh BuildQuad(float width, float depth)
    {
        var mesh = new Mesh();
        var halfX = width / 2f;
        var halfZ = depth / 2f;
        mesh.vertices = new[]
        {
            new Vector3(-halfX, 0, -halfZ),
            new Vector3(halfX, 0, -halfZ),
            new Vector3(-halfX, 0, halfZ),
            new Vector3(halfX, 0, halfZ),
        };
        mesh.uv = new[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh BuildConformingMesh(Terrain terrain, int resolution, float offset, Vector3 center, RasterBounds? bounds)
    {
        var data = terrain.terrainData;
        var size = data.size;
        var origin = terrain.GetPosition();
        var res = Mathf.Clamp(resolution, 16, 512);
        var vertexCount = (res + 1) * (res + 1);
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[res * res * 6];

        var minX = (float)(bounds?.MinX ?? origin.x);
        var maxX = (float)(bounds?.MaxX ?? (origin.x + size.x));
        var minZ = (float)(bounds?.MinZ ?? origin.z);
        var maxZ = (float)(bounds?.MaxZ ?? (origin.z + size.z));

        var index = 0;
        for (var z = 0; z <= res; z++)
        {
            var v = z / (float)res;
            var worldZ = Mathf.Lerp(minZ, maxZ, v);
            for (var x = 0; x <= res; x++)
            {
                var u = x / (float)res;
                var worldX = Mathf.Lerp(minX, maxX, u);
                var height = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + origin.y + offset;
                var worldPos = new Vector3(worldX, height, worldZ);
                vertices[index] = worldPos - center;
                uvs[index] = new Vector2(u, v);
                index++;
            }
        }

        var tri = 0;
        for (var z = 0; z < res; z++)
        {
            for (var x = 0; x < res; x++)
            {
                var i = z * (res + 1) + x;
                triangles[tri++] = i;
                triangles[tri++] = i + res + 1;
                triangles[tri++] = i + 1;
                triangles[tri++] = i + 1;
                triangles[tri++] = i + res + 1;
                triangles[tri++] = i + res + 2;
            }
        }

        var mesh = new Mesh();
        if (vertexCount > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Material? CreateMaterial()
    {
        return RuntimeMaterialFactory.CreateOverlayMaterial();
    }

}
}






