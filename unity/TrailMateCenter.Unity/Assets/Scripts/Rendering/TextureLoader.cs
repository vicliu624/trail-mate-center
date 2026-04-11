using System;
using System.IO;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public static class TextureLoader
{
    public static bool TryLoad(string uri, RasterConversionConfig conversion, out RasterLoadResult result)
    {
        return TryLoad(uri, conversion, out result, out _);
    }

    public static bool TryLoad(string uri, RasterConversionConfig conversion, out RasterLoadResult result, out string error)
    {
        result = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(uri))
        {
            error = "raster uri is empty";
            return false;
        }

        var sourcePath = ResolvePath(uri);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            error = $"raster file not found: {sourcePath}";
            return false;
        }

        var metadata = RasterMetadataExtractor.Extract(sourcePath, conversion);
        var path = sourcePath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".tif" || ext == ".tiff")
        {
            if (!RasterConverter.TryConvert(path, conversion, out var converted))
            {
                error = "GeoTIFF conversion failed";
                return false;
            }
            path = converted;
            ext = Path.GetExtension(path).ToLowerInvariant();
            metadata = RasterMetadataExtractor.Extract(path, conversion);
        }
        else if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
        {
            if (!conversion.Enable || !RasterConverter.TryConvert(path, conversion, out var converted))
            {
                error = $"raster conversion failed for format: {ext}";
                return false;
            }
            path = converted;
            ext = Path.GetExtension(path).ToLowerInvariant();
            metadata = RasterMetadataExtractor.Extract(path, conversion);
        }

        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
        {
            error = $"unsupported raster format: {ext}";
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: conversion.GenerateMipMaps);
            if (!UnityEngine.ImageConversion.LoadImage(tex, bytes))
            {
                error = "image decode failed";
                return false;
            }

            if (conversion.MaxTextureSize > 0 &&
                (tex.width > conversion.MaxTextureSize || tex.height > conversion.MaxTextureSize))
            {
                var resized = DownscaleTexture(tex, conversion.MaxTextureSize, conversion.GenerateMipMaps);
                if (!ReferenceEquals(resized, tex))
                {
                    UnityEngine.Object.Destroy(tex);
                    tex = resized;
                }
            }

            result = new RasterLoadResult
            {
                SourcePath = sourcePath,
                TexturePath = path,
                Texture = tex,
                Metadata = metadata,
            };
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Layer] failed to load texture {path}: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    private static string ResolvePath(string uri)
    {
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new Uri(uri).LocalPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        return uri;
    }

    private static Texture2D DownscaleTexture(Texture2D source, int maxSize, bool mipMaps)
    {
        var maxDim = Math.Max(source.width, source.height);
        if (maxDim <= maxSize)
            return source;

        var scale = maxSize / (float)maxDim;
        var targetWidth = Math.Max(1, Mathf.RoundToInt(source.width * scale));
        var targetHeight = Math.Max(1, Mathf.RoundToInt(source.height * scale));

        var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var output = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, mipMaps);
            output.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            output.Apply(updateMipmaps: mipMaps, makeNoLongerReadable: false);
            RenderTexture.active = previous;
            return output;
        }
        catch
        {
            return source;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
}

