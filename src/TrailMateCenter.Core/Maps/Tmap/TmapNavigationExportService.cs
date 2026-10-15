using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TrailMateCenter.Maps.Tmap;

/// <summary>Explicit SDK producer integration; never downloads or guesses an OSM region.</summary>
public sealed record TmapNavigationExportPlan
{
    public string PbfPath { get; init; } = "";
    public string[] RegionalPbfPaths { get; init; } = [];
    public int MaximumRegionEntities { get; init; } = 1_000_000;
    public int MaximumRegionReferences { get; init; } = 8_000_000;
    public string ToolsDirectory { get; init; } = "";
    public string PythonExecutable { get; init; } = "python";
    public string OutputDirectory { get; init; } = "";
    public int PageBytes { get; init; } = 4096;
    public int Landmarks { get; init; }
    public string TopologyEpoch { get; init; } = "";
    public string TravelMode { get; init; } = "both";
    public string? ConditionAt { get; init; }
    public bool RuntimeConditions { get; init; }
    public string? OutdoorDocumentPath { get; init; }
    public string? HgtPath { get; init; }
    public GeoBounds? TerrainBounds { get; init; }
    public int ContourIntervalMeters { get; init; } = 10;
    public uint TerrainVerticalDatum { get; init; }
    public string TerrainAttribution { get; init; } = "";
}

public sealed class TmapNavigationExportService
{
    public async Task<string> ExportAsync(TmapNavigationExportPlan plan, CancellationToken token = default,
        IProgress<TmapNavigationExportProgress>? progress = null)
    {
        progress?.Report(new(TmapNavigationExportStage.Validating));
        if (plan.HgtPath is not null && plan.OutdoorDocumentPath is not null)
            throw new ArgumentException("Choose an HGT source or a prepared outdoor document, not both.");
        if (plan.ContourIntervalMeters is < 5 or > 1000)
            throw new ArgumentException("Contour interval must be between 5 and 1000 metres.");
        if (plan.HgtPath is not null && (plan.TerrainBounds is not { } bounds ||
            !double.IsFinite(bounds.West) || !double.IsFinite(bounds.East) ||
            !double.IsFinite(bounds.South) || !double.IsFinite(bounds.North) ||
            bounds.West < -180 || bounds.East > 180 || bounds.South < -90 || bounds.North > 90 ||
            bounds.West >= bounds.East || bounds.South >= bounds.North))
            throw new ArgumentException("A valid geographic terrain crop is required for HGT input.");
        if (plan.RegionalPbfPaths is null || plan.RegionalPbfPaths.Length > 32 ||
            plan.MaximumRegionEntities < 1 || plan.MaximumRegionReferences < 1)
            throw new ArgumentException("Invalid regional source or entity/reference budget.");
        if (plan.PageBytes is not (1024 or 2048 or 4096) || plan.Landmarks is < 0 or > 8)
            throw new ArgumentException("Invalid page capacity or landmark count.");
        if (plan.TravelMode is not ("foot" or "bicycle" or "both"))
            throw new ArgumentException("TravelMode must be foot, bicycle or both.");
        if (plan.RuntimeConditions && plan.ConditionAt is not null)
            throw new ArgumentException("RuntimeConditions cannot be combined with ConditionAt.");
        if (plan.ConditionAt is not null &&
            (!Regex.IsMatch(plan.ConditionAt, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,6})?(Z|[+-]\d{2}:\d{2})$") ||
             !DateTimeOffset.TryParse(plan.ConditionAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new ArgumentException("ConditionAt must be a local ISO instant with seconds and an explicit UTC offset.");
        if (string.IsNullOrWhiteSpace(plan.TopologyEpoch) || string.IsNullOrWhiteSpace(plan.OutputDirectory))
            throw new ArgumentException("Explicit topology epoch and output directory are required.");
        var input = Path.GetFullPath(plan.PbfPath);
        var regionInputs = plan.RegionalPbfPaths.Select(Path.GetFullPath).ToArray();
        if (regionInputs.Append(input).Distinct(StringComparer.OrdinalIgnoreCase).Count() != regionInputs.Length + 1)
            throw new ArgumentException("Regional source paths must be distinct.");
        var tools = Path.GetFullPath(plan.ToolsDirectory);
        var output = Path.GetFullPath(plan.OutputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Output artifact directory already exists; choose a new destination.");
        foreach (var name in new[] { "import_osm.py", "osm_pbf.py", "osm_conditions.py", "tmap_pack.py" })
            if (!File.Exists(Path.Combine(tools, name))) throw new FileNotFoundException($"Missing TMAP SDK producer {name}.");
        if (plan.RuntimeConditions)
            foreach (var name in new[] { "compile_calendar.py", "compile_road_conditions.py", "compile_barrier_conditions.py", "road_rule_sections.py",
                "compile_turn_conditions.py", "turn_rule_sections.py", "osm_conditional_turns.py" })
                if (!File.Exists(Path.Combine(tools, name))) throw new FileNotFoundException($"Missing runtime-condition producer {name}.");
        if (!File.Exists(input)) throw new FileNotFoundException("Missing local PBF source.", input);
        foreach (var path in regionInputs)
            if (!File.Exists(path)) throw new FileNotFoundException("Missing regional PBF source.", path);
        if (regionInputs.Length > 0 && !File.Exists(Path.Combine(tools, "merge_osm_regions.py")))
            throw new FileNotFoundException("Missing raw regional OSM union producer.");
        var outdoorPath = plan.OutdoorDocumentPath is null ? null : Path.GetFullPath(plan.OutdoorDocumentPath);
        var hgtPath = plan.HgtPath is null ? null : Path.GetFullPath(plan.HgtPath);
        if (hgtPath is not null)
        {
            if (!File.Exists(hgtPath)) throw new FileNotFoundException("Missing local HGT elevation source.", hgtPath);
            if (!File.Exists(Path.Combine(tools, "import_hgt.py"))) throw new FileNotFoundException("Missing SDK HGT importer.");
        }
        if (outdoorPath is not null && !File.Exists(outdoorPath))
            throw new FileNotFoundException("Missing local outdoor production document.", outdoorPath);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".navigation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var regionalLeases = new List<FileStream>();
        try
        {
            // Readers may open it; on Windows replacement/writes are excluded during build.
            await using var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source, token)).ToLowerInvariant();
            var regionalSources = new List<object>();
            foreach (var path in regionInputs)
            {
                var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                regionalLeases.Add(lease);
                regionalSources.Add(new
                {
                    path,
                    bytes = lease.Length,
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(lease, token)).ToLowerInvariant()
                });
            }
            await using var hgt = hgtPath is null ? null : new FileStream(hgtPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hgtHash = hgt is null ? null : Convert.ToHexString(await SHA256.HashDataAsync(hgt, token)).ToLowerInvariant();
            if (hgtPath is not null)
            {
                progress?.Report(new(TmapNavigationExportStage.PreparingTerrain));
                outdoorPath = Path.Combine(staging, "terrain.json");
                var crop = plan.TerrainBounds!.Value;
                await RunAsync(plan.PythonExecutable, tools, [Path.Combine(tools, "import_hgt.py"), hgtPath, outdoorPath,
                    "--bounds", crop.West.ToString("R", CultureInfo.InvariantCulture), crop.South.ToString("R", CultureInfo.InvariantCulture),
                    crop.East.ToString("R", CultureInfo.InvariantCulture), crop.North.ToString("R", CultureInfo.InvariantCulture),
                    "--source", plan.TerrainAttribution + " HGT SHA256:" + hgtHash,
                    "--vertical-datum", plan.TerrainVerticalDatum.ToString(CultureInfo.InvariantCulture)], token);
                var terrain = JsonNode.Parse(await File.ReadAllTextAsync(outdoorPath, token))
                    ?? throw new InvalidDataException("Terrain importer returned an empty document.");
                terrain["contour_interval_m"] = plan.ContourIntervalMeters;
                await File.WriteAllTextAsync(outdoorPath, terrain.ToJsonString(), token);
            }
            await using var outdoor = outdoorPath is null ? null : new FileStream(outdoorPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var outdoorHash = outdoor is null ? null : Convert.ToHexString(await SHA256.HashDataAsync(outdoor, token)).ToLowerInvariant();
            var graphPath = Path.Combine(staging, "graph.json");
            var importArguments = new List<string> { Path.Combine(tools, "import_osm.py"), input, graphPath,
                "--topology-epoch", plan.TopologyEpoch, "--travel-mode", plan.TravelMode };
            if (plan.ConditionAt is not null) importArguments.AddRange(["--condition-at", plan.ConditionAt]);
            if (plan.RuntimeConditions) importArguments.Add("--runtime-conditions");
            if (outdoorPath is not null) importArguments.AddRange(["--terrain-document", outdoorPath]);
            foreach (var path in regionInputs) importArguments.AddRange(["--region-input", path]);
            if (regionInputs.Length > 0)
                importArguments.AddRange(["--max-region-entities", plan.MaximumRegionEntities.ToString(CultureInfo.InvariantCulture),
                    "--max-region-references", plan.MaximumRegionReferences.ToString(CultureInfo.InvariantCulture)]);
            progress?.Report(new(TmapNavigationExportStage.Importing));
            await RunAsync(plan.PythonExecutable, tools, importArguments.ToArray(), token);
            var document = JsonNode.Parse(await File.ReadAllTextAsync(graphPath, token))
                ?? throw new InvalidDataException("Producer returned an empty graph document.");
            document["page_bytes"] = plan.PageBytes;
            document["graph"]!["landmarks"] = plan.Landmarks;
            await File.WriteAllTextAsync(graphPath, document.ToJsonString(), token);
            var packagePath = Path.Combine(staging, "navigation.tmap");
            progress?.Report(new(TmapNavigationExportStage.Packing));
            await RunAsync(plan.PythonExecutable, tools,
                [Path.Combine(tools, "tmap_pack.py"), graphPath, packagePath, "--report", Path.Combine(staging, "sections.json")], token);
            progress?.Report(new(TmapNavigationExportStage.Verifying));
            var toolHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(tools, "*.py").Order(StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(path);
                toolHashes[Path.GetFileName(path)] = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            }
            await using var package = File.OpenRead(packagePath);
            var manifest = new
            {
                producer = "TrailMateCenter.TmapNavigationExportService",
                format = "TMAP outdoor v2 draft",
                source = new { path = input, bytes = source.Length, sha256 = sourceHash },
                regionalSources,
                regionalPolicy = regionInputs.Length == 0 ? null : "Original stable OSM identity union before graph segmentation; explicit compatible epoch, overlapping E7/tags/members must agree, no coordinate proximity joins.",
                plan.MaximumRegionEntities,
                plan.MaximumRegionReferences,
                outdoorSource = outdoor is null ? null : new { path = hgtPath is null ? outdoorPath : Path.Combine(output, "terrain.json"), bytes = outdoor.Length, sha256 = outdoorHash },
                terrainSource = hgt is null ? null : new
                {
                    path = hgtPath,
                    bytes = hgt.Length,
                    sha256 = hgtHash,
                    plan.TerrainBounds,
                    plan.ContourIntervalMeters,
                    plan.TerrainVerticalDatum,
                    plan.TerrainAttribution
                },
                sdkToolsSha256 = toolHashes,
                output = new
                {
                    file = "navigation.tmap",
                    bytes = package.Length,
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(package, token)).ToLowerInvariant()
                },
                plan.PageBytes,
                plan.Landmarks,
                plan.TopologyEpoch,
                plan.TravelMode,
                plan.ConditionAt,
                plan.RuntimeConditions,
                conditionPolicy = plan.RuntimeConditions ? "Runtime calendar road, endpoint-barrier and supported node/via-way turn rules; requires caller-owned readers and immutable RuntimeRoutingRules with explicit query UTC/local offset. Unsupported expressions/member forms fail explicitly."
                    : plan.ConditionAt is null ? "No time snapshot; applicable unsupported conditions rejected."
                    : "Static local-time snapshot; runtime does not enforce validity. Preserve this manifest and restrict use to represented conditions.",
                scope = (plan.RuntimeConditions ? "Runtime-condition foot/bicycle graph" : "Static foot/bicycle graph") +
                    (outdoor is null ? "; no outdoor document supplied." : " composed with explicit outdoor document; supplied DEM/contours/administration packed by SDK. No raster conversion."),
                attribution = "OpenStreetMap contributors; source licensing and attribution remain required."
            };
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), token);
            token.ThrowIfCancellationRequested();
            // Close output handle before publishing the directory on Windows.
            await package.DisposeAsync();
            if (hgtPath is not null && outdoor is not null) await outdoor.DisposeAsync();
            Directory.Move(staging, output);
            progress?.Report(new(TmapNavigationExportStage.Completed));
            return Path.Combine(output, "navigation.tmap");
        }
        finally
        {
            foreach (var lease in regionalLeases) await lease.DisposeAsync();
            // Only the uniquely created sibling staging directory is removed.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task RunAsync(string executable, string workingDirectory, string[] arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Cannot start TMAP SDK producer.");
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
        if (process.ExitCode != 0) throw new InvalidDataException($"TMAP SDK producer failed ({process.ExitCode}): {stderr.Result}");
    }
}

public enum TmapNavigationExportStage { Validating, PreparingTerrain, Importing, Packing, Verifying, Completed }
public sealed record TmapNavigationExportProgress(TmapNavigationExportStage Stage);
