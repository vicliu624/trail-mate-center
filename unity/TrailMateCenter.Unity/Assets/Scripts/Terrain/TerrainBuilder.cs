using System;
using System.IO;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.TerrainSystem
{
public sealed class TerrainBuilder : MonoBehaviour
{
    private Terrain? _terrain;
    private TerrainData? _terrainData;

    public Terrain Build(TerrainConfig config, Transform? parent = null)
    {
        _terrainData = new TerrainData
        {
            heightmapResolution = Mathf.Max(33, config.HeightmapResolution),
            size = new Vector3(config.SizeX, config.HeightScale, config.SizeZ)
        };

        var heights = LoadHeightmap(config, _terrainData.heightmapResolution);
        _terrainData.SetHeights(0, 0, heights);

        var terrainObj = Terrain.CreateTerrainGameObject(_terrainData);
        terrainObj.name = "PropagationTerrain";
        terrainObj.transform.position = new Vector3(0, config.OffsetY, 0);
        if (parent != null)
            terrainObj.transform.SetParent(parent, worldPositionStays: true);
        _terrain = terrainObj.GetComponent<Terrain>();
        if (_terrain != null)
        {
            _terrain.materialTemplate = CreateTerrainMaterial();
        }

        return _terrain!;
    }

    public Terrain? Terrain => _terrain;
    public TerrainData? TerrainData => _terrainData;

    private static Material? CreateTerrainMaterial()
    {
        return RuntimeMaterialFactory.CreateTerrainMaterial(new Color(0.22f, 0.24f, 0.26f, 1f));
    }

    private static float[,] LoadHeightmap(TerrainConfig config, int resolution)
    {
        if (string.IsNullOrWhiteSpace(config.HeightmapPath))
        {
            return GenerateFallbackHeights(resolution);
        }

        try
        {
            if (!File.Exists(config.HeightmapPath))
                return GenerateFallbackHeights(resolution);

            var bytes = File.ReadAllBytes(config.HeightmapPath);
            var expected = resolution * resolution * 2;
            if (bytes.Length < expected)
                return GenerateFallbackHeights(resolution);

            var heights = new float[resolution, resolution];
            var index = 0;
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var value = BitConverter.ToUInt16(bytes, index);
                    heights[y, x] = value / 65535f;
                    index += 2;
                }
            }
            return heights;
        }
        catch
        {
            return GenerateFallbackHeights(resolution);
        }
    }

    private static float[,] GenerateFallbackHeights(int resolution)
    {
        var heights = new float[resolution, resolution];
        for (var y = 0; y < resolution; y++)
        {
            for (var x = 0; x < resolution; x++)
            {
                var nx = (x / (float)resolution) * 4f;
                var ny = (y / (float)resolution) * 4f;
                var h = Mathf.PerlinNoise(nx, ny) * 0.6f + Mathf.PerlinNoise(nx * 0.5f, ny * 0.5f) * 0.4f;
                heights[y, x] = h * 0.5f;
            }
        }
        return heights;
    }
}
}



