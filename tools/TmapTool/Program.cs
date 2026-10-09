using System.Text.Json;
using System.Text.Json.Serialization;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;
using TrailMateCenter.Maps.Rendering;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (args.Length is 3 or 4 && args[0] == "extensions-install")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapExtensionInstaller.Install(args[1], args[2], cancel.Token,
            args.Length == 4 ? args[3] : null), json)); return 0;
    }
    if (args.Length == 3 && args[0] == "fonts-upgrade")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapFontSections.Upgrade(args[1], args[2], cancel.Token), json)); return 0;
    }
    if (args.Length == 3 && args[0] == "admin-upgrade")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapAdministrativeSections.Upgrade(args[1], args[2], cancel.Token), json)); return 0;
    }
    if (args.Length == 2 && args[0] == "register")
    {
        TmapPackageRegistry.Register(args[1]); Console.WriteLine(JsonSerializer.Serialize(TmapPackageRegistry.Paths(), json)); return 0;
    }
    if (args.Length == 3 && args[0] == "fast-labels-install")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapFastLabels.Install(args[1], args[2], cancel.Token), json)); return 0;
    }
    if (args.Length == 2 && args[0] == "fast-labels-upgrade")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapFastLabels.Upgrade(args[1], cancel.Token), json)); return 0;
    }
    if (args.Length == 3 && args[0] == "fast-labels")
    {
        Console.WriteLine(JsonSerializer.Serialize(TmapFastLabels.Create(args[1], args[2], cancel.Token), json)); return 0;
    }
    if (args.Length == 2 && args[0] == "china-search-check")
    {
        using var reader = new TmapReader(args[1]); var queries = CountryVerification.CheckCities(reader, cancel.Token);
        var path = Path.ChangeExtension(args[1], ".verification.json");
        var report = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing verification report.");
        report["queries"] = JsonSerializer.SerializeToNode(queries, json); report["cityLocationsValidated"] = true;
        report["searchValidation"] = "Native/alternate source names, up to 1000 candidates ranked with city coordinates; geographical radius checked to exclude unrelated namesakes";
        File.WriteAllText(path, report.ToJsonString(json)); Console.WriteLine(JsonSerializer.Serialize(queries, json)); return 0;
    }
    if (args.Length == 3 && args[0] == "china-check")
    {
        var report = JsonSerializer.Serialize(CountryVerification.Check(args[1], args[2], cancel.Token), json);
        File.WriteAllText(Path.ChangeExtension(args[1], ".verification.json"), report);
        Console.WriteLine(report); return 0;
    }
    if (args.Length == 4 && args[0] == "china-osm")
    {
        Console.WriteLine(JsonSerializer.Serialize(OsmWorldOverview.BuildChina(args[1], args[2], args[3], cancel.Token), json)); return 0;
    }
    if (args.Length == 2 && args[0] == "china-plan")
    {
        Console.WriteLine(JsonSerializer.Serialize(new CountryCoverage(args[1]).Estimate(), json)); return 0;
    }
    if (args.Length == 2 && args[0] == "world-check")
    {
        var report = JsonSerializer.Serialize(OsmWorldOverview.Check(args[1], cancel.Token), json);
        File.WriteAllText(Path.ChangeExtension(args[1], ".verification.json"), report);
        Console.WriteLine(report); return 0;
    }
    if (args.Length == 3 && args[0] == "world-osm")
    {
        Console.WriteLine(JsonSerializer.Serialize(OsmWorldOverview.Build(args[1], args[2], cancel.Token), json));
        return 0;
    }
    if (args.Length == 3 && args[0] == "world")
    {
        Console.WriteLine(JsonSerializer.Serialize(WorldOverview.Build(args[1], args[2], cancel.Token), json));
        return 0;
    }
    if (args.Length == 2 && args[0] == "build")
    {
        var path = Path.GetFullPath(args[1]);
        var plan = JsonSerializer.Deserialize<MapPackExportPlan>(File.ReadAllText(path), json) ?? throw new ArgumentException("Missing build plan.");
        var parent = Path.GetDirectoryName(path)!;
        plan = plan with { OutputDirectory = Path.GetFullPath(plan.OutputDirectory, parent), Poi = plan.Poi with { PbfPath = Path.GetFullPath(plan.Poi.PbfPath, parent) } };
        var result = await new TmapPackExporter().ExportAsync(plan, new ConsoleProgress(), cancellationToken: cancel.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, json)); return 0;
    }
    if (args.Length == 3 && args[0] == "batch")
    {
        // Each configured country/admin region has its own source and bounds. Do not infer boundaries from a country name.
        var path = Path.GetFullPath(args[1]); var parent = Path.GetDirectoryName(path)!;
        var plans = JsonSerializer.Deserialize<List<MapPackExportPlan>>(File.ReadAllText(path), json) ?? throw new ArgumentException("Missing plans.");
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packageKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            var options = plan.Tmap ?? throw new ArgumentException("Each batch entry requires TMAP options.");
            if (!filenames.Add(options.FileName) || !packageKeys.Add(options.PackageKey))
                throw new ArgumentException("Batch filenames and package keys must be unique.");
        }
        foreach (var p in plans)
        {
            var plan = p with { OutputDirectory = Path.GetFullPath(args[2]), Poi = p.Poi with { PbfPath = Path.GetFullPath(p.Poi.PbfPath, parent) } };
            Console.WriteLine(JsonSerializer.Serialize(await new TmapPackExporter().ExportAsync(plan, new ConsoleProgress(), cancellationToken: cancel.Token), json));
        }
        return 0;
    }
    if (args.Length == 2 && args[0] == "verify")
    {
        using var reader = new TmapReader(args[1]); reader.ValidateAllPages(cancel.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { valid = true, reader.Capabilities, reader.ZoomMask, reader.PoiCount, reader.PageReads }, json)); return 0;
    }
    if (args.Length >= 3 && args[0] == "search")
    {
        using var reader = new TmapReader(args[1]);
        var mode = args.Length > 3 ? Enum.Parse<TmapSearchMode>(args[3], true) : TmapSearchMode.Substring;
        Console.WriteLine(JsonSerializer.Serialize(reader.Search(args[2], mode, token: cancel.Token), json)); return 0;
    }
    if (args.Length == 4 && args[0] == "import-rasters")
    {
        // Explicitly requires text-free 256px PNG input. Optional POI construction is available through the streaming builder API.
        var plan = JsonSerializer.Deserialize<MapPackExportPlan>(File.ReadAllText(args[3]), json) ?? throw new ArgumentException("Missing import metadata.");
        var options = plan.Tmap ?? throw new ArgumentException("TMAP options required.");
        using var builder = new TmapBuilder(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, options.MaximumOutputBytes, cancel.Token);
        TmapPackExporter.ImportRasterDirectory(builder, args[1], cancel.Token);
        Console.WriteLine(JsonSerializer.Serialize(builder.Complete(args[2], plan.Area, options), json)); return 0;
    }
    Console.Error.WriteLine("TMAP: world-osm source.mbtiles output.tmap | build plan.json | batch plans.json output-folder | verify file.tmap | search file.tmap text [Exact|Prefix|Substring] | register file.tmap | fast-labels-upgrade file.tmap | fast-labels-install prepared.tmap destination.tmap | import-rasters maps-folder file.tmap metadata.json");
    return 2;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Canceled; unpublished output was discarded."); return 130; }
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }

sealed class ConsoleProgress : IProgress<LocalMapPackProgress>
{
    private long _last; private string _stage = "";
    public void Report(LocalMapPackProgress p)
    {
        var now = Environment.TickCount64;
        if (_stage == p.Stage && now - _last < 1000 && p.Completed != p.Total) return;
        _last = now; _stage = p.Stage; Console.Error.WriteLine($"{p.Stage}: {p.Completed}/{p.Total} z{p.Zoom}");
    }
}
