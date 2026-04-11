using System.Collections.Concurrent;
using System.Globalization;
using Mapsui.Projections;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Services;

internal sealed class PropagationTerrainInputBuilder
{
    private const int TargetLongEdgeSamples = 96;
    private const int MinSamplesPerAxis = 24;
    private const int MaxSamplesPerAxis = 128;

    private static readonly string CacheRoot = Path.Combine(ContourPaths.Root, "propagation-grid");

    private readonly EarthdataClient _earthdata = new(null);
    private readonly GdalRunner _gdal = new();
    private readonly ConcurrentDictionary<string, PropagationTerrainInput> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);

    public async Task<PropagationTerrainInput> BuildForCurrentViewportAsync(
        MapViewModel map,
        IReadOnlyList<PropagationSiteInput> sites,
        CancellationToken cancellationToken)
    {
        if (!map.TryGetViewportWorldBounds(out var viewportBounds))
            throw new InvalidOperationException("Map viewport is not ready. Open the propagation map and wait for the basemap to initialize before running propagation.");

        var contourSettings = map.GetContourSettingsSnapshot();
        _earthdata.UpdateCredentials(contourSettings.Earthdata);
        if (!_earthdata.HasCredentials)
            throw new InvalidOperationException("Earthdata token is missing. Real propagation terrain sampling now requires a valid Earthdata token.");

        if (!await _gdal.EnsureAvailableAsync(cancellationToken))
            throw new InvalidOperationException("GDAL is not available. Real propagation terrain sampling requires a working GDAL installation.");

        var expandedBounds = ExpandWorldBounds(viewportBounds, sites);
        var layout = ResolveGridLayout(expandedBounds);
        var cacheKey = BuildCacheKey(expandedBounds, layout.Columns, layout.Rows);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var cacheLock = _cacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
                return cached;

            var lonLatBounds = ToLonLatBounds(expandedBounds);
            var demFiles = await _earthdata.EnsureDemFilesAsync(lonLatBounds, cancellationToken);
            if (demFiles.Count == 0)
                throw new InvalidOperationException("No DEM tiles were available for the current propagation AOI.");

            Directory.CreateDirectory(CacheRoot);
            var workDir = Path.Combine(CacheRoot, cacheKey);
            Directory.CreateDirectory(workDir);

            var sourceVrtPath = Path.Combine(workDir, "source.vrt");
            var warpedRasterPath = Path.Combine(workDir, "terrain_3857.tif");
            var xyzPath = Path.Combine(workDir, "terrain.xyz");

            if (!File.Exists(sourceVrtPath))
            {
                var sourceArgs = string.Join(' ', demFiles.Select(Quote));
                await RunOrThrowAsync(
                    "gdalbuildvrt",
                    $"-q -overwrite {Quote(sourceVrtPath)} {sourceArgs}",
                    "gdalbuildvrt failed while preparing the propagation DEM mosaic.",
                    cancellationToken);
            }

            await RunOrThrowAsync(
                "gdalwarp",
                $"-q -overwrite -t_srs EPSG:3857 -te {Fmt(expandedBounds.MinX)} {Fmt(expandedBounds.MinZ)} {Fmt(expandedBounds.MaxX)} {Fmt(expandedBounds.MaxZ)} -ts {layout.Columns} {layout.Rows} -r bilinear -dstnodata -32768 {Quote(sourceVrtPath)} {Quote(warpedRasterPath)}",
                "gdalwarp failed while resampling the propagation DEM into the current map viewport.",
                cancellationToken);

            await RunOrThrowAsync(
                "gdal_translate",
                $"-q -of XYZ {Quote(warpedRasterPath)} {Quote(xyzPath)}",
                "gdal_translate failed while exporting sampled propagation terrain values.",
                cancellationToken);

            var terrainInput = ParseTerrainGrid(
                xyzPath,
                expandedBounds,
                layout.Columns,
                layout.Rows,
                BuildAoiId(expandedBounds));
            _cache[cacheKey] = terrainInput;
            return terrainInput;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task RunOrThrowAsync(
        string toolName,
        string arguments,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var exitCode = await _gdal.RunAsync(toolName, arguments, cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException($"{failureMessage} Exit code: {exitCode}.");
    }

    private static PropagationTerrainInput ParseTerrainGrid(
        string xyzPath,
        (double MinX, double MinZ, double MaxX, double MaxZ) bounds,
        int columns,
        int rows,
        string aoiId)
    {
        var expectedCount = columns * rows;
        var elevations = new double[expectedCount];
        var lineIndex = 0;

        foreach (var rawLine in File.ReadLines(xyzPath))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
                throw new InvalidOperationException($"Unexpected XYZ row format while reading {xyzPath}.");

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var elevation))
                throw new InvalidOperationException($"Failed to parse DEM elevation value from {xyzPath}.");

            if (lineIndex >= expectedCount)
                throw new InvalidOperationException($"The sampled DEM grid from {xyzPath} was larger than expected.");

            var sourceRow = lineIndex / columns;
            var column = lineIndex % columns;
            var targetRow = rows - 1 - sourceRow;
            if (double.IsNaN(elevation) || double.IsInfinity(elevation) || elevation <= -32000d)
                throw new InvalidOperationException("The sampled DEM grid contains no-data cells. Move to a valid land DEM coverage area before running propagation.");

            elevations[(targetRow * columns) + column] = elevation;
            lineIndex++;
        }

        if (lineIndex != expectedCount)
            throw new InvalidOperationException($"The sampled DEM grid from {xyzPath} was incomplete. Expected {expectedCount} samples but read {lineIndex}.");

        var finiteElevations = elevations.Where(static value => !double.IsNaN(value) && !double.IsInfinity(value)).ToArray();
        if (finiteElevations.Length == 0)
            throw new InvalidOperationException("The sampled DEM grid did not contain any usable elevation values.");

        return new PropagationTerrainInput
        {
            AoiId = aoiId,
            Crs = "EPSG:3857",
            MinX = bounds.MinX,
            MinZ = bounds.MinZ,
            MaxX = bounds.MaxX,
            MaxZ = bounds.MaxZ,
            SampleStepM = Math.Max((bounds.MaxX - bounds.MinX) / Math.Max(1, columns), (bounds.MaxZ - bounds.MinZ) / Math.Max(1, rows)),
            Columns = columns,
            Rows = rows,
            ElevationSamples = elevations,
            LandcoverSamples = Enumerable.Repeat(PropagationLandcoverClass.BareGround, expectedCount).ToArray(),
        };
    }

    private static (double MinX, double MinZ, double MaxX, double MaxZ) ExpandWorldBounds(
        (double MinX, double MinZ, double MaxX, double MaxZ) viewportBounds,
        IReadOnlyList<PropagationSiteInput> sites)
    {
        var minX = viewportBounds.MinX;
        var minZ = viewportBounds.MinZ;
        var maxX = viewportBounds.MaxX;
        var maxZ = viewportBounds.MaxZ;

        foreach (var site in sites)
        {
            minX = Math.Min(minX, site.X);
            minZ = Math.Min(minZ, site.Z);
            maxX = Math.Max(maxX, site.X);
            maxZ = Math.Max(maxZ, site.Z);
        }

        var width = Math.Max(1d, maxX - minX);
        var height = Math.Max(1d, maxZ - minZ);
        var margin = Math.Max(120d, Math.Max(width, height) * 0.05d);
        return (minX - margin, minZ - margin, maxX + margin, maxZ + margin);
    }

    private static GridLayout ResolveGridLayout((double MinX, double MinZ, double MaxX, double MaxZ) bounds)
    {
        var width = Math.Max(1d, bounds.MaxX - bounds.MinX);
        var height = Math.Max(1d, bounds.MaxZ - bounds.MinZ);
        var longEdge = Math.Max(width, height);
        var cellSize = Math.Max(20d, longEdge / TargetLongEdgeSamples);

        var columns = Math.Clamp((int)Math.Ceiling(width / cellSize), MinSamplesPerAxis, MaxSamplesPerAxis);
        var rows = Math.Clamp((int)Math.Ceiling(height / cellSize), MinSamplesPerAxis, MaxSamplesPerAxis);
        return new GridLayout(columns, rows);
    }

    private static (double West, double South, double East, double North) ToLonLatBounds(
        (double MinX, double MinZ, double MaxX, double MaxZ) worldBounds)
    {
        var (west, south) = SphericalMercator.ToLonLat(worldBounds.MinX, worldBounds.MinZ);
        var (east, north) = SphericalMercator.ToLonLat(worldBounds.MaxX, worldBounds.MaxZ);
        return (
            Math.Min(west, east),
            Math.Min(south, north),
            Math.Max(west, east),
            Math.Max(south, north));
    }

    private static string BuildCacheKey((double MinX, double MinZ, double MaxX, double MaxZ) bounds, int columns, int rows)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"x{Math.Round(bounds.MinX, 1):F1}_{Math.Round(bounds.MaxX, 1):F1}_z{Math.Round(bounds.MinZ, 1):F1}_{Math.Round(bounds.MaxZ, 1):F1}_{columns}x{rows}");
    }

    private static string BuildAoiId((double MinX, double MinZ, double MaxX, double MaxZ) bounds)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"aoi_3857_{Math.Round(bounds.MinX):F0}_{Math.Round(bounds.MinZ):F0}_{Math.Round(bounds.MaxX):F0}_{Math.Round(bounds.MaxZ):F0}");
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private readonly record struct GridLayout(int Columns, int Rows);
}
