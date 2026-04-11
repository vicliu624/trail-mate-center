using System;
using System.Collections.Generic;
using System.Linq;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class TiledLayerSurface : MonoBehaviour
{
    private const int MaxClassBreaks = 16;

    private readonly Dictionary<string, TileEntry> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedTexture> _cachedTextures = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _cacheLru = new();
    private Terrain? _terrain;
    private LayerConfig _layerConfig = new();
    private RasterConversionConfig _conversion = new();
    private string _layerId = string.Empty;
    private bool _visible;
    private bool _configured;
    private bool _conformToTerrain;
    private RasterBounds _sourceBounds;
    private string _tileTemplateUri = string.Empty;
    private int _minZoom;
    private int _maxZoom;
    private int _tileSize = 256;
    private float _nextRefreshAt;
    private Texture2D? _rampTexture;
    private float _opacity = 0.6f;
    private float _valueMin;
    private float _valueMax = 1f;
    private float _noData = -10f;
    private float _hasNoData;
    private float _valueScale = 1f;
    private float _valueOffset;
    private readonly float[] _classBreaks = new float[MaxClassBreaks];
    private int _classBreakCount;

    public void Initialize(
        string layerId,
        Terrain? terrain,
        LayerConfig layerConfig,
        RasterConversionConfig conversion)
    {
        _layerId = layerId;
        _terrain = terrain;
        _layerConfig = layerConfig;
        _conversion = conversion;
        _visible = false;
        _configured = false;
    }

    public void ConfigureSource(RasterMetadata metadata, RasterBounds sourceBounds, bool conformToTerrain)
    {
        if (!metadata.HasTileSource)
        {
            DisableSource();
            return;
        }

        _tileTemplateUri = metadata.TileTemplateUri;
        _minZoom = Math.Max(0, metadata.MinZoom ?? 0);
        _maxZoom = Math.Max(_minZoom, metadata.MaxZoom ?? _minZoom);
        _tileSize = Math.Max(32, metadata.TileSize ?? 256);
        _sourceBounds = sourceBounds;
        _conformToTerrain = conformToTerrain && _layerConfig.ConformToTerrain;
        _configured = true;
        _nextRefreshAt = 0f;
        ClearTiles();
    }

    public void DisableSource()
    {
        _configured = false;
        ClearTiles();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        foreach (var tile in _tiles.Values)
        {
            if (tile.GameObject != null)
                tile.GameObject.SetActive(visible);
        }
    }

    public void SetOpacity(float opacity)
    {
        _opacity = Mathf.Clamp01(opacity);
        foreach (var tile in _tiles.Values)
            ApplyMaterialStyle(tile.Material);
    }

    public void SetRampTexture(Texture2D ramp)
    {
        _rampTexture = ramp;
        foreach (var tile in _tiles.Values)
            ApplyMaterialStyle(tile.Material);
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
        foreach (var tile in _tiles.Values)
            ApplyMaterialStyle(tile.Material);
    }

    private void ApplyClassBreaks(IReadOnlyList<double>? classBreaks)
    {
        Array.Clear(_classBreaks, 0, _classBreaks.Length);
        if (classBreaks == null || classBreaks.Count == 0)
        {
            _classBreakCount = 0;
            return;
        }

        var sorted = classBreaks
            .Take(MaxClassBreaks)
            .OrderBy(static value => value)
            .ToArray();
        _classBreakCount = sorted.Length;
        for (var i = 0; i < sorted.Length; i++)
            _classBreaks[i] = (float)sorted[i];
    }

    private void Update()
    {
        if (!_visible || !_configured || !_sourceBounds.IsValid)
            return;

        if (Time.time < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.time + Mathf.Max(0.1f, _layerConfig.TileRefreshIntervalSeconds);
        RefreshVisibleTiles();
    }

    private void OnDestroy()
    {
        ClearTiles();
        foreach (var entry in _cachedTextures.Values)
        {
            if (entry.Texture != null)
                Destroy(entry.Texture);
        }
        _cachedTextures.Clear();
        _cacheLru.Clear();
    }

    private void RefreshVisibleTiles()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        var zoom = ResolveZoom(camera.transform.position.y);
        var gridSize = 1 << zoom;
        var sourceWidth = _sourceBounds.MaxX - _sourceBounds.MinX;
        var sourceDepth = _sourceBounds.MaxZ - _sourceBounds.MinZ;
        if (sourceWidth <= 1e-6 || sourceDepth <= 1e-6)
            return;

        var viewBounds = BuildCameraViewBounds(camera, _sourceBounds);
        var needed = BuildTileSetForBounds(viewBounds, zoom, gridSize, sourceWidth, sourceDepth);
        while (needed.Count > Math.Max(4, _layerConfig.MaxActiveTiles) && zoom > _minZoom)
        {
            zoom--;
            gridSize = 1 << zoom;
            needed = BuildTileSetForBounds(viewBounds, zoom, gridSize, sourceWidth, sourceDepth);
        }

        foreach (var key in needed)
        {
            if (_tiles.ContainsKey(key))
                continue;
            CreateTile(key, zoom, sourceWidth, sourceDepth);
        }

        foreach (var key in _tiles.Keys.Where(key => !needed.Contains(key)).ToList())
            RemoveTile(key);
    }

    private HashSet<string> BuildTileSetForBounds(
        RasterBounds viewBounds,
        int zoom,
        int gridSize,
        double sourceWidth,
        double sourceDepth)
    {
        var txMin = ClampTileIndex((viewBounds.MinX - _sourceBounds.MinX) / sourceWidth * gridSize, gridSize);
        var txMax = ClampTileIndex((viewBounds.MaxX - _sourceBounds.MinX) / sourceWidth * gridSize, gridSize);
        var tyMin = ClampTileIndex((viewBounds.MinZ - _sourceBounds.MinZ) / sourceDepth * gridSize, gridSize);
        var tyMax = ClampTileIndex((viewBounds.MaxZ - _sourceBounds.MinZ) / sourceDepth * gridSize, gridSize);

        var needed = new HashSet<string>(StringComparer.Ordinal);
        for (var tx = txMin; tx <= txMax; tx++)
        {
            for (var ty = tyMin; ty <= tyMax; ty++)
                needed.Add($"{zoom}/{tx}/{ty}");
        }

        return needed;
    }

    private static int ClampTileIndex(double raw, int gridSize)
    {
        var value = (int)Math.Floor(raw);
        if (value < 0)
            return 0;
        if (value >= gridSize)
            return gridSize - 1;
        return value;
    }

    private static RasterBounds BuildCameraViewBounds(Camera camera, RasterBounds sourceBounds)
    {
        var centerX = camera.transform.position.x;
        var centerZ = camera.transform.position.z;
        var radius = Mathf.Clamp(camera.transform.position.y * 1.2f, 180f, 4000f);
        var minX = Math.Max(sourceBounds.MinX, centerX - radius);
        var maxX = Math.Min(sourceBounds.MaxX, centerX + radius);
        var minZ = Math.Max(sourceBounds.MinZ, centerZ - radius);
        var maxZ = Math.Min(sourceBounds.MaxZ, centerZ + radius);
        if (maxX <= minX || maxZ <= minZ)
            return sourceBounds;
        return new RasterBounds(minX, minZ, maxX, maxZ);
    }

    private int ResolveZoom(float cameraAltitude)
    {
        if (_maxZoom <= _minZoom)
            return _minZoom;

        if (cameraAltitude <= 550f)
            return _maxZoom;
        if (cameraAltitude <= 1100f)
            return Math.Max(_minZoom, _maxZoom - 1);
        if (cameraAltitude <= 1800f)
            return Math.Max(_minZoom, _maxZoom - 2);
        return _minZoom;
    }

    private void CreateTile(string key, int zoom, double sourceWidth, double sourceDepth)
    {
        var tokens = key.Split('/');
        if (tokens.Length != 3)
            return;
        if (!int.TryParse(tokens[1], out var tx) || !int.TryParse(tokens[2], out var ty))
            return;

        var gridSize = 1 << zoom;
        var tileWidth = sourceWidth / gridSize;
        var tileDepth = sourceDepth / gridSize;
        var minX = _sourceBounds.MinX + tx * tileWidth;
        var maxX = minX + tileWidth;
        var minZ = _sourceBounds.MinZ + ty * tileDepth;
        var maxZ = minZ + tileDepth;
        var tileBounds = new RasterBounds(minX, minZ, maxX, maxZ);
        var tileUri = ResolveTileUri(zoom, tx, ty);

        var cached = TryAcquireCachedTexture(tileUri, out var texture);
        if (!cached)
            texture = BuildFallbackTileTexture();

        var tileGo = new GameObject($"tile_{_layerId}_{zoom}_{tx}_{ty}");
        tileGo.transform.SetParent(transform, false);
        tileGo.SetActive(_visible);

        var meshFilter = tileGo.AddComponent<MeshFilter>();
        var meshRenderer = tileGo.AddComponent<MeshRenderer>();
        var material = CreateMaterial();
        meshRenderer.sharedMaterial = material;

        var center = new Vector3((float)((minX + maxX) * 0.5), ResolveCenterHeight(tileBounds), (float)((minZ + maxZ) * 0.5));
        tileGo.transform.position = center;
        meshFilter.sharedMesh = BuildMesh(tileBounds, center);
        ApplyMaterialTexture(material, texture);
        ApplyMaterialStyle(material);

        _tiles[key] = new TileEntry
        {
            GameObject = tileGo,
            Material = material,
            Texture = texture,
            CacheKey = cached ? tileUri : string.Empty,
            IsFallbackTexture = !cached,
        };
    }

    private Mesh BuildMesh(RasterBounds tileBounds, Vector3 center)
    {
        var width = (float)(tileBounds.MaxX - tileBounds.MinX);
        var depth = (float)(tileBounds.MaxZ - tileBounds.MinZ);
        if (!_conformToTerrain || _terrain == null)
            return BuildQuad(width, depth);

        return BuildConformingMesh(
            _terrain,
            Math.Max(8, _layerConfig.TileConformingResolution),
            _layerConfig.TerrainOffset,
            center,
            tileBounds);
    }

    private float ResolveCenterHeight(RasterBounds bounds)
    {
        var centerX = (float)((bounds.MinX + bounds.MaxX) * 0.5);
        var centerZ = (float)((bounds.MinZ + bounds.MaxZ) * 0.5);
        if (_conformToTerrain && _terrain != null)
            return _terrain.SampleHeight(new Vector3(centerX, 0f, centerZ)) + _terrain.GetPosition().y;
        return _layerConfig.DefaultOverlayHeight;
    }

    private static Material? CreateMaterial()
    {
        return RuntimeMaterialFactory.CreateOverlayMaterial();
    }

    private void ApplyMaterialTexture(Material? material, Texture2D texture)
    {
        if (material == null)
            return;

        material.mainTexture = texture;
        material.color = Color.white;
    }

    private void ApplyMaterialStyle(Material? material)
    {
        if (material == null)
            return;
        if (_rampTexture != null && material.HasProperty("_RampTex"))
            material.SetTexture("_RampTex", _rampTexture);

        if (material.HasProperty("_Alpha"))
            material.SetFloat("_Alpha", _opacity);
        if (material.HasProperty("_ValueMin"))
            material.SetFloat("_ValueMin", _valueMin);
        if (material.HasProperty("_ValueMax"))
            material.SetFloat("_ValueMax", _valueMax);
        if (material.HasProperty("_NoData"))
            material.SetFloat("_NoData", _noData);
        if (material.HasProperty("_HasNoData"))
            material.SetFloat("_HasNoData", _hasNoData);
        if (material.HasProperty("_ValueScale"))
            material.SetFloat("_ValueScale", _valueScale);
        if (material.HasProperty("_ValueOffset"))
            material.SetFloat("_ValueOffset", _valueOffset);
        if (material.HasProperty("_ClassBreakCount"))
            material.SetFloat("_ClassBreakCount", _classBreakCount);
        if (material.HasProperty("_ClassBreaks"))
            material.SetFloatArray("_ClassBreaks", _classBreaks);
    }

    private string ResolveTileUri(int zoom, int x, int y)
    {
        return _tileTemplateUri
            .Replace("{z}", zoom.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString())
            .Replace("{zoom}", zoom.ToString());
    }

    private void RemoveTile(string key)
    {
        if (!_tiles.Remove(key, out var tile))
            return;

        if (tile.GameObject != null)
            Destroy(tile.GameObject);
        if (tile.Material != null)
            Destroy(tile.Material);
        if (!string.IsNullOrWhiteSpace(tile.CacheKey))
            ReleaseCachedTexture(tile.CacheKey);
        else if (tile.IsFallbackTexture && tile.Texture != null)
            Destroy(tile.Texture);
    }

    private void ClearTiles()
    {
        foreach (var key in _tiles.Keys.ToList())
            RemoveTile(key);
    }

    private bool TryAcquireCachedTexture(string tileUri, out Texture2D texture)
    {
        texture = null!;
        if (_cachedTextures.TryGetValue(tileUri, out var existing))
        {
            existing.RefCount++;
            TouchCacheEntry(existing.Node);
            texture = existing.Texture;
            return true;
        }

        if (!TextureLoader.TryLoad(tileUri, _conversion, out var loaded, out _))
            return false;

        var node = new LinkedListNode<string>(tileUri);
        _cacheLru.AddFirst(node);
        _cachedTextures[tileUri] = new CachedTexture
        {
            Texture = loaded.Texture,
            RefCount = 1,
            Node = node,
        };

        TrimCache();
        texture = loaded.Texture;
        return true;
    }

    private void ReleaseCachedTexture(string tileUri)
    {
        if (!_cachedTextures.TryGetValue(tileUri, out var cached))
            return;

        cached.RefCount = Math.Max(0, cached.RefCount - 1);
    }

    private void TouchCacheEntry(LinkedListNode<string> node)
    {
        _cacheLru.Remove(node);
        _cacheLru.AddFirst(node);
    }

    private void TrimCache()
    {
        var capacity = Math.Max(64, _layerConfig.MaxActiveTiles * 4);
        var guard = 0;
        while (_cachedTextures.Count > capacity && _cacheLru.Count > 0 && guard < 10000)
        {
            guard++;
            var tail = _cacheLru.Last;
            if (tail == null)
                break;

            var key = tail.Value;
            _cacheLru.RemoveLast();
            if (!_cachedTextures.TryGetValue(key, out var entry))
                continue;
            if (entry.RefCount > 0)
            {
                _cacheLru.AddFirst(tail);
                continue;
            }

            _cachedTextures.Remove(key);
            if (entry.Texture != null)
                Destroy(entry.Texture);
        }
    }

    private static Texture2D BuildFallbackTileTexture()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        var fallback = new Color(0.84f, 0.24f, 0.24f, 0.55f);
        texture.SetPixel(0, 0, fallback);
        texture.SetPixel(1, 0, fallback);
        texture.SetPixel(0, 1, fallback);
        texture.SetPixel(1, 1, fallback);
        texture.Apply();
        return texture;
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

    private static Mesh BuildConformingMesh(Terrain terrain, int resolution, float offset, Vector3 center, RasterBounds bounds)
    {
        var data = terrain.terrainData;
        var origin = terrain.GetPosition();
        var res = Mathf.Clamp(resolution, 8, 64);
        var vertexCount = (res + 1) * (res + 1);
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[res * res * 6];

        var index = 0;
        for (var z = 0; z <= res; z++)
        {
            var v = z / (float)res;
            var worldZ = Mathf.Lerp((float)bounds.MinZ, (float)bounds.MaxZ, v);
            for (var x = 0; x <= res; x++)
            {
                var u = x / (float)res;
                var worldX = Mathf.Lerp((float)bounds.MinX, (float)bounds.MaxX, u);
                var height = terrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + origin.y + offset;
                vertices[index] = new Vector3(worldX, height, worldZ) - center;
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

    private sealed class TileEntry
    {
        public GameObject GameObject { get; set; } = null!;
        public Material? Material { get; set; }
        public Texture2D Texture { get; set; } = null!;
        public string CacheKey { get; set; } = string.Empty;
        public bool IsFallbackTexture { get; set; }
    }

    private sealed class CachedTexture
    {
        public Texture2D Texture { get; set; } = null!;
        public int RefCount { get; set; }
        public LinkedListNode<string> Node { get; set; } = null!;
    }
}
}



