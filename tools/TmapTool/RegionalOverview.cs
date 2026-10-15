using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed record RegionalOverviewPlan(string Name, string CountryCode, string PackageKey,
    string CoverageDirectory, string[] PolygonFiles, string VersatilesExecutable,
    string SourceExtractPath, string OutputPath);

internal static class RegionalOverview
{
    public static async Task<object> BuildAsync(RegionalOverviewPlan plan, CancellationToken token)
    {
        if (plan.PolygonFiles.Length == 0 || plan.PackageKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            string.IsNullOrWhiteSpace(plan.PackageKey) || string.IsNullOrWhiteSpace(plan.CountryCode))
            throw new ArgumentException("Explicit country identity and extraction polygons are required.");
        var coverage = new CountryCoverage(Path.GetFullPath(plan.CoverageDirectory), plan.PolygonFiles);
        if (!File.Exists(plan.SourceExtractPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plan.SourceExtractPath))!);
            var temporary = Path.GetFullPath(plan.SourceExtractPath) + "." + Guid.NewGuid().ToString("N") + ".mbtiles";
            try
            {
                var start = new ProcessStartInfo(plan.VersatilesExecutable) { UseShellExecute = false, CreateNoWindow = true };
                var b = coverage.Bounds;
                var bounds = FormattableString.Invariant($"{b.West:R},{b.South:R},{b.East:R},{b.North:R}");
                foreach (var argument in new[] { "convert", "https://download.versatiles.org/osm.20260608.versatiles", temporary,
                    "--min-zoom", "8", "--max-zoom", "12", "--bbox", bounds, "--compress", "gzip" }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Cannot start regional vector extraction.");
                try { await process.WaitForExitAsync(token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
                if (process.ExitCode != 0) throw new IOException($"Regional vector extraction failed ({process.ExitCode}).");
                File.Move(temporary, plan.SourceExtractPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var result = OsmWorldOverview.BuildCountry(plan.SourceExtractPath, plan.OutputPath, coverage,
            plan.Name, plan.CountryCode, plan.PackageKey, token);
        await using var source = File.OpenRead(plan.SourceExtractPath);
        var provenance = new
        {
            producer = "TrailMateCenter.TmapTool.RegionalOverview",
            plan,
            sourceSnapshot = "2026-06-08",
            sourceExtractSha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, token)).ToLowerInvariant(),
            result
        };
        await File.WriteAllTextAsync(plan.OutputPath + ".production.json", JsonSerializer.Serialize(provenance,
            new JsonSerializerOptions { WriteIndented = true }), token);
        return result;
    }
}
