using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Tests;

public sealed class TmapElevationProfileTests
{
    [Fact]
    public void SamplesBilinearTerrainAndSeparatesAscentDescentFromEndpointDifference()
    {
        WithTerrain(false, path =>
        {
            var profile = TmapOutdoorPreviewReader.ReadElevationProfile(path,
                [new(0, 0), new(.001, 0), new(.002, 0)], spacingMeters: 1000, noiseThresholdMeters: 0);
            Assert.Equal(3, profile.Samples.Count);
            Assert.Equal(100, profile.Samples[0].ElevationMeters);
            Assert.Equal(130, profile.Samples[1].ElevationMeters);
            Assert.Equal(110, profile.Samples[2].ElevationMeters);
            Assert.Equal(30, profile.AscentMeters, 6);
            Assert.Equal(20, profile.DescentMeters, 6);
            Assert.Equal(100d, profile.MinimumMeters);
            Assert.Equal(130d, profile.MaximumMeters);
            Assert.Equal(profile.DistanceMeters, profile.ValidDistanceMeters, 6);
            var middle = TmapOutdoorPreviewReader.ReadElevationProfile(path,
                [new(.0005, .0005), new(.0015, .0005)], spacingMeters: 1000);
            Assert.Equal(115d, middle.Samples[0].ElevationMeters);
            Assert.Equal(120d, middle.Samples[1].ElevationMeters);
        });
    }
    [Fact]
    public void MissingSamplesDoNotCreateInventedClimbOrConnections()
    {
        WithTerrain(true, path =>
        {
            var profile = TmapOutdoorPreviewReader.ReadElevationProfile(path,
                [new(0, 0), new(.001, 0), new(.002, 0)], spacingMeters: 1000);
            Assert.Null(profile.Samples[1].ElevationMeters);
            Assert.Equal(0, profile.AscentMeters);
            Assert.Equal(0, profile.DescentMeters);
            Assert.Equal(0, profile.ValidDistanceMeters);
            Assert.Equal(100d, profile.Samples[0].ElevationMeters);
            Assert.Equal(110d, profile.Samples[2].ElevationMeters);
        });
    }
    [Fact]
    public void HonorsBudgetsCancellationAndNoiseThreshold()
    {
        WithTerrain(false, path =>
        {
            TmapPreviewPoint[] points = [new(0, 0), new(.002, 0)];
            Assert.Throws<InvalidDataException>(() => TmapOutdoorPreviewReader.ReadElevationProfile(path, points, 1, maximumSamples: 2));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => TmapOutdoorPreviewReader.ReadElevationProfile(path, points, token: canceled.Token));
            var quiet = TmapOutdoorPreviewReader.ReadElevationProfile(path, points, 1000, 20);
            Assert.Equal(0, quiet.AscentMeters);
            Assert.Throws<ArgumentException>(() => TmapOutdoorPreviewReader.ReadElevationProfile(path, [new(double.NaN, 0), new(0, 0)]));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
    }
    private static void WithTerrain(bool missing, Action<string> action)
    {
        const int page = 1024;
        var info = new byte[64];
        TmapFormat.PutI32(info, 8, 10000); TmapFormat.PutI32(info, 12, 10000);
        TmapFormat.Put32(info, 16, 3); TmapFormat.Put32(info, 20, 2);
        TmapFormat.Put16(info, 24, 4); TmapFormat.Put16(info, 26, 1); TmapFormat.Put32(info, 28, 30);
        var index = new byte[32]; TmapFormat.Put16(index, 8, 3); TmapFormat.Put16(index, 10, 2); TmapFormat.Put32(index, 24, 12);
        var data = new byte[12];
        short[] heights = [100, missing ? short.MinValue : (short)130, 110, 100, 130, 110];
        for (var i = 0; i < heights.Length; i++) TmapFormat.Put16(data, i * 2, unchecked((ushort)heights[i]));
        var parts = new[] { (1000U, info, 1UL), (1001U, index, 1UL), (1002U, data, 6UL) };
        var raw = new byte[5 * page]; TmapFormat.Magic.CopyTo(raw);
        TmapFormat.Put16(raw, 8, 2); TmapFormat.Put32(raw, 12, 256); TmapFormat.Put32(raw, 16, 0x01020304);
        TmapFormat.Put32(raw, 20, page); TmapFormat.Put64(raw, 32, (ulong)raw.Length); TmapFormat.Put64(raw, 40, page);
        TmapFormat.Put32(raw, 48, 3); TmapFormat.Put32(raw, 52, 64);
        TmapFormat.PutI32(raw, 128, 20000); TmapFormat.PutI32(raw, 132, 10000);
        for (var i = 0; i < parts.Length; i++)
        {
            var (type, bytes, count) = parts[i]; var entry = page + i * 64; var at = (i + 2) * page;
            TmapFormat.Put32(raw, entry, type); TmapFormat.Put16(raw, entry + 4, 1); TmapFormat.Put16(raw, entry + 6, 2);
            TmapFormat.Put32(raw, entry + 8, type); TmapFormat.Put64(raw, entry + 16, (ulong)at);
            TmapFormat.Put64(raw, entry + 24, page); TmapFormat.Put64(raw, entry + 32, count); TmapFormat.Put64(raw, entry + 48, (ulong)bytes.Length);
            "TPG2"u8.CopyTo(raw.AsSpan(at)); TmapFormat.Put32(raw, at + 4, type); TmapFormat.Put32(raw, at + 12, (uint)bytes.Length);
            bytes.CopyTo(raw, at + 32); TmapFormat.Put32(raw, at + 16, TmapFormat.Crc32C(raw.AsSpan(at, page)));
        }
        TmapFormat.Put32(raw, 164, TmapFormat.Crc32C(raw.AsSpan(page, 3 * 64)));
        TmapFormat.Put32(raw, 160, TmapFormat.Crc32C(raw.AsSpan(0, 256)));
        var path = Path.Combine(Path.GetTempPath(), "tmap-profile-" + Guid.NewGuid().ToString("N") + ".tmap");
        try { File.WriteAllBytes(path, raw); action(path); }
        finally { File.Delete(path); }
    }
}
