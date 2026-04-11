using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public static class ColorRampFactory
{
    public static Texture2D Build(string palette, int width = 256)
    {
        var stops = ResolveStops(palette);
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, mipChain: false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        for (var i = 0; i < width; i++)
        {
            var t = i / (float)(width - 1);
            var color = Evaluate(stops, t);
            tex.SetPixel(i, 0, color);
        }

        tex.Apply();
        return tex;
    }

    private static Color Evaluate((float T, Color C)[] stops, float t)
    {
        if (t <= stops[0].T)
            return stops[0].C;
        if (t >= stops[^1].T)
            return stops[^1].C;

        for (var i = 0; i < stops.Length - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];
            if (t < a.T || t > b.T)
                continue;

            var local = Mathf.InverseLerp(a.T, b.T, t);
            return Color.Lerp(a.C, b.C, local);
        }

        return stops[^1].C;
    }

    private static (float T, Color C)[] ResolveStops(string palette)
    {
        var key = (palette ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "reliability" => new[]
            {
                (0f, new Color(0.45f, 0.16f, 0.18f, 1f)),
                (0.25f, new Color(0.85f, 0.33f, 0.18f, 1f)),
                (0.5f, new Color(0.96f, 0.76f, 0.24f, 1f)),
                (0.75f, new Color(0.36f, 0.75f, 0.46f, 1f)),
                (1f, new Color(0.20f, 0.56f, 0.92f, 1f))
            },
            "interference" => new[]
            {
                (0f, new Color(0.18f, 0.48f, 0.90f, 1f)),
                (0.4f, new Color(0.98f, 0.86f, 0.25f, 1f)),
                (0.7f, new Color(0.96f, 0.48f, 0.20f, 1f)),
                (1f, new Color(0.77f, 0.12f, 0.15f, 1f))
            },
            "capacity" => new[]
            {
                (0f, new Color(0.77f, 0.12f, 0.15f, 1f)),
                (0.5f, new Color(0.95f, 0.78f, 0.24f, 1f)),
                (1f, new Color(0.19f, 0.74f, 0.35f, 1f))
            },
            _ => new[]
            {
                (0f, new Color(0.76f, 0.16f, 0.18f, 1f)),
                (0.35f, new Color(0.96f, 0.58f, 0.18f, 1f)),
                (0.6f, new Color(0.97f, 0.86f, 0.22f, 1f)),
                (0.8f, new Color(0.35f, 0.78f, 0.41f, 1f)),
                (1f, new Color(0.18f, 0.60f, 0.92f, 1f))
            }
        };
    }
}
}

