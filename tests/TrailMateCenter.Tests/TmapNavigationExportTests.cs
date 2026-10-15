using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Tests;

public sealed class TmapNavigationExportTests
{
    [Fact]
    public async Task HgtRequiresValidCropAndCannotBeCombinedWithPreparedOutdoor()
    {
        var service = new TmapNavigationExportService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(new() { HgtPath = "source.hgt" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(new() { HgtPath = "source.hgt", OutdoorDocumentPath = "prepared.json" }));
        foreach (var bounds in new[] { new TrailMateCenter.Maps.GeoBounds(1, 1, 1, 2), new(double.NaN, 1, 2, 3), new(1, -91, 2, 3) })
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(new() { HgtPath = "source.hgt", TerrainBounds = bounds }));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task ContourIntervalIsValidatedBeforeOpeningAnyInput(int interval)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(new() { ContourIntervalMeters = interval }));
    }
    [Fact]
    public async Task InvalidRegionalBudgetsAreRejectedBeforeSourceAccess()
    {
        foreach (var plan in new[] { new TmapNavigationExportPlan { MaximumRegionEntities = 0 },
                     new TmapNavigationExportPlan { MaximumRegionReferences = 0 },
                     new TmapNavigationExportPlan { RegionalPbfPaths = null! },
                     new TmapNavigationExportPlan { RegionalPbfPaths = new string[33] } })
            await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(plan));
    }

    [Fact]
    public async Task DuplicateRegionalPathsAreRejectedBeforeProducerAccess()
    {
        var path = Path.Combine(Path.GetTempPath(), "same-regional-source.pbf");
        await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(new()
        {
            PbfPath = path,
            RegionalPbfPaths = [path],
            TopologyEpoch = "same-source",
            ToolsDirectory = Path.GetTempPath(),
            OutputDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        }));
    }
    [Theory]
    [InlineData(512, 0, "foot")]
    [InlineData(4096, 9, "foot")]
    [InlineData(4096, 0, "car")]
    public async Task InvalidProfileIsRejectedBeforePublication(int pages, int landmarks, string mode)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(
            new() { PageBytes = pages, Landmarks = landmarks, TravelMode = mode }));
    }

    [Fact]
    public async Task InvalidConditionInstantIsRejectedBeforeSourceAccess()
    {
        foreach (var instant in new[] { "", "2026-10-12T09:00:00", "2026-02-30T09:00:00+02:00", "2026-10-12T09:00:00+25:00" })
            await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(
                new() { ConditionAt = instant }));
    }
    [Fact]
    public async Task RuntimeAndSnapshotConditionsAreMutuallyExclusive()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => new TmapNavigationExportService().ExportAsync(
            new() { RuntimeConditions = true, ConditionAt = "2026-10-12T09:00:00+02:00" }));
        Assert.Contains("cannot be combined", exception.Message);
    }

    [Fact]
    public async Task ProducerStartFailureDiscardsOnlyItsStagingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", "navigation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sdk = Path.Combine(root, "sdk"); Directory.CreateDirectory(sdk);
            foreach (var name in new[] { "import_osm.py", "osm_pbf.py", "osm_conditions.py", "tmap_pack.py" })
                await File.WriteAllTextAsync(Path.Combine(sdk, name), "fixture");
            var input = Path.Combine(root, "source.pbf"); await File.WriteAllTextAsync(input, "source retained");
            var output = Path.Combine(root, "output");
            var plan = new TmapNavigationExportPlan
            {
                PbfPath = input,
                ToolsDirectory = sdk,
                PythonExecutable = Path.Combine(root, "missing-interpreter"),
                OutputDirectory = output,
                TopologyEpoch = "fixture"
            };
            await Assert.ThrowsAsync<FileNotFoundException>(() => new TmapNavigationExportService().ExportAsync(
                plan with { OutdoorDocumentPath = Path.Combine(root, "missing-outdoor.json") }));
            Assert.Empty(Directory.EnumerateDirectories(root, ".navigation-*"));
            await Assert.ThrowsAnyAsync<Exception>(() => new TmapNavigationExportService().ExportAsync(plan));
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.EnumerateDirectories(root, ".navigation-*"));
            Assert.Equal("source retained", await File.ReadAllTextAsync(input));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TmapNavigationExportService().ExportAsync(plan, cancelled.Token));
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.EnumerateDirectories(root, ".navigation-*"));
            Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "keep.txt"), "keep");
            await Assert.ThrowsAsync<IOException>(() => new TmapNavigationExportService().ExportAsync(plan));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
