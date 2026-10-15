using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Tests;

public sealed class TmapRegionProductionTests
{
    [Fact]
    public async Task InvalidBoundsAndLandmarksFailBeforeStartingProducer()
    {
        var root = Path.Combine(Path.GetTempPath(), "center-region-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "source.pbf"); await File.WriteAllTextAsync(input, "source retained");
            var script = Path.Combine(root, "prepare.py"); await File.WriteAllTextAsync(script, "not executed");
            var plan = new TmapRegionProductionPlan
            {
                PbfPath = input,
                PreparationScript = script,
                Area = new() { Bounds = new GeoBounds(1, 1, 2, 2) },
                ElevationCacheDirectory = root,
                OutputDirectory = Path.Combine(root, "new-output")
            };
            var service = new TmapRegionProductionService();
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(plan with { Landmarks = 9 }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(plan with { ElevationCacheDirectory = "" }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(plan with { Area = new() { Bounds = new GeoBounds(1, 1, 1, 2) } }));
            Assert.Empty(Directory.GetDirectories(root, ".region-*"));
            Assert.Equal("source retained", await File.ReadAllTextAsync(input));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ProcessStartFailureCleansOnlyOwnedStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "center-region-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "source.pbf"); await File.WriteAllTextAsync(input, "preserve");
            var script = Path.Combine(root, "prepare.py"); await File.WriteAllTextAsync(script, "fixture");
            var sentinel = Path.Combine(root, "unrelated"); Directory.CreateDirectory(sentinel);
            await Assert.ThrowsAnyAsync<Exception>(() => new TmapRegionProductionService().ExportAsync(new()
            {
                PbfPath = input,
                PreparationScript = script,
                PythonExecutable = Path.Combine(root, "missing-python"),
                Area = new() { Bounds = new GeoBounds(1, 1, 2, 2) },
                ElevationCacheDirectory = root,
                OutputDirectory = Path.Combine(root, "new-output")
            }));
            Assert.Empty(Directory.GetDirectories(root, ".region-*"));
            Assert.True(Directory.Exists(sentinel));
            Assert.False(Directory.Exists(Path.Combine(root, "new-output")));
            Assert.Equal("preserve", await File.ReadAllTextAsync(input));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
