using System.Diagnostics;
using System.Text.Json;

namespace TrailMateCenter.Maps.Tmap;

public sealed record TmapRegionProductionPlan
{
    public string PbfPath { get; init; } = "";
    public MapPackAreaSelection Area { get; init; } = new();
    public string PythonExecutable { get; init; } = "python";
    public string SdkToolsDirectory { get; init; } = "";
    public string PreparationScript { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ElevationCacheDirectory { get; init; } = "";
    public double GridStepDegrees { get; init; } = 0.001;
    public int MaximumDemSamples { get; init; } = 4_000_000;
    public int ContourIntervalMeters { get; init; } = 10;
    public int Landmarks { get; init; } = 4;
    public bool ClipContoursToRegion { get; init; } = true;
    public bool ExcludeConditionalFeatures { get; init; }
}

/// <summary>Center-owned regional PBF preparation, multi-tile numeric DEM and SDK export.</summary>
public sealed class TmapRegionProductionService
{
    public async Task<string> ExportAsync(TmapRegionProductionPlan plan, CancellationToken token = default)
    {
        if (!File.Exists(plan.PbfPath) || !File.Exists(plan.PreparationScript))
            throw new FileNotFoundException("Local OSM source and Center preparation script are required.");
        if (plan.GridStepDegrees is < 0.0001 or > 0.01 || !double.IsFinite(plan.GridStepDegrees) ||
            plan.MaximumDemSamples is < 4 or > 4_000_000 || plan.ContourIntervalMeters is < 5 or > 1000 || plan.Landmarks is < 0 or > 8)
            throw new ArgumentException("Invalid regional terrain resolution or sample budget.");
        var bounds = plan.Area.Bounds;
        if (!double.IsFinite(bounds.West) || !double.IsFinite(bounds.East) || !double.IsFinite(bounds.South) || !double.IsFinite(bounds.North) ||
            bounds.West < -180 || bounds.East > 180 || bounds.South < -90 || bounds.North > 90 ||
            bounds.West >= bounds.East || bounds.South >= bounds.North || string.IsNullOrWhiteSpace(plan.ElevationCacheDirectory))
            throw new ArgumentException("Explicit WGS84 regional bounds and elevation cache are required.");
        var output = Path.GetFullPath(plan.OutputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Regional destination already exists; select a new destination.");
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".region-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var settings = Path.Combine(staging, "production-plan.json");
            await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(plan), token);
            var start = new ProcessStartInfo(plan.PythonExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.GetFullPath(plan.PreparationScript));
            start.ArgumentList.Add(settings);
            start.ArgumentList.Add(staging);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start Center regional producer.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            await Task.WhenAll(stdout, stderr);
            await File.WriteAllTextAsync(Path.Combine(staging, "preparation.log"), stdout.Result + stderr.Result, token);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"Center regional preparation failed ({process.ExitCode}): {stderr.Result}");
            var result = await new TmapNavigationExportService().ExportAsync(new()
            {
                PbfPath = Path.Combine(staging, "region.osm.pbf"),
                OutdoorDocumentPath = Path.Combine(staging, "terrain.json"),
                PythonExecutable = plan.PythonExecutable,
                ToolsDirectory = plan.SdkToolsDirectory,
                OutputDirectory = Path.Combine(staging, "outdoor"),
                TravelMode = "foot",
                PageBytes = 4096,
                Landmarks = plan.Landmarks,
                TopologyEpoch = "center-region-" + Path.GetFileName(output),
                RuntimeConditions = !plan.ExcludeConditionalFeatures
            }, token);
            // Release all producer handles before publishing the complete regional directory.
            Directory.Move(staging, output);
            return Path.Combine(output, "outdoor", Path.GetFileName(result));
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
