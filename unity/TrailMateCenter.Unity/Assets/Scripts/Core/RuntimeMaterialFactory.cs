using System.Collections.Generic;
using UnityEngine;
namespace TrailMateCenter.Unity.Core
{
public static class RuntimeMaterialFactory
{
    private static readonly HashSet<string> ReportedMissingShaders = new();

    public static Material? CreateTerrainMaterial(Color color)
    {
        return CreateMaterial(
            "terrain",
            color,
            "Nature/Terrain/Standard",
            "Universal Render Pipeline/Terrain/Lit",
            "Universal Render Pipeline/Lit",
            "Standard",
            "Legacy Shaders/Diffuse",
            "Sprites/Default");
    }

    public static Material? CreateSurfaceMaterial(Color color)
    {
        return CreateMaterial(
            "surface",
            color,
            "Universal Render Pipeline/Lit",
            "Standard",
            "Legacy Shaders/Diffuse",
            "Unlit/Color",
            "Sprites/Default");
    }

    public static Material? CreateLineMaterial(Color color)
    {
        return CreateMaterial(
            "line",
            color,
            "Sprites/Default",
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Texture",
            "Hidden/Internal-Colored");
    }

    public static Material? CreateOverlayMaterial()
    {
        return CreateMaterial(
            "overlay",
            Color.white,
            "TrailMateCenter/OverlayRamp",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Texture",
            "Unlit/Color",
            "Sprites/Default",
            "Hidden/Internal-Colored");
    }

    public static Shader? FindFirstShader(params string[] shaderNames)
    {
        foreach (var shaderName in shaderNames)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
                continue;

            var shader = Shader.Find(shaderName);
            if (shader != null)
                return shader;
        }

        return null;
    }

    private static Material? CreateMaterial(string purpose, Color color, params string[] shaderNames)
    {
        var shader = FindFirstShader(shaderNames);
        if (shader == null)
        {
            ReportMissingShaders(purpose, shaderNames);
            return null;
        }

        var material = new Material(shader);
        ApplyColor(material, color);
        return material;
    }

    private static void ApplyColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.color = color;
    }

    private static void ReportMissingShaders(string purpose, string[] shaderNames)
    {
        var key = purpose + ":" + string.Join("|", shaderNames);
        if (!ReportedMissingShaders.Add(key))
            return;

        Debug.LogWarning($"[Materials] No compatible {purpose} shader found. Tried: {string.Join(", ", shaderNames)}");
    }
}
}
