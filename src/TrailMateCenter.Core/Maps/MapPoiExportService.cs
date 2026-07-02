using TrailMateCenter.Osm;

namespace TrailMateCenter.Maps;

public sealed record PoiExportRequest
{
    public string MapsRoot { get; init; } = string.Empty;
    public string PbfPath { get; init; } = string.Empty;
    public GeoBounds Bounds { get; init; }
    public string BoundaryGeoJson { get; init; } = string.Empty;
    public string AreaName { get; init; } = "Selection";
    public int? AreaAdminLevel { get; init; }
    public string SourceProvider { get; init; } = "local";
    public string SourceDownloadUrl { get; init; } = string.Empty;
    public string NameLanguage { get; init; } = "default";
    public IReadOnlyCollection<string> SelectedPoiTypes { get; init; } = Array.Empty<string>();
    public PoiIndexOptions IndexOptions { get; init; } = new();
}

public sealed record PoiExportResult
{
    public bool Success { get; init; }
    public string PoiRoot { get; init; } = string.Empty;
    public long SourcePoiCount { get; init; }
    public long IndexRowsWritten { get; init; }
    public int TileFilesWritten { get; init; }
    public bool WasAnyTileClipped { get; init; }
    public string? ErrorMessage { get; init; }

    public static PoiExportResult Fail(string message)
    {
        return new PoiExportResult
        {
            Success = false,
            ErrorMessage = message,
        };
    }
}

public sealed class MapPoiExportService
{
    private readonly OsmPoiExtractor _extractor;
    private readonly PoiIndexWriter _writer;

    public MapPoiExportService(
        OsmPoiExtractor? extractor = null,
        PoiIndexWriter? writer = null)
    {
        _extractor = extractor ?? new OsmPoiExtractor();
        _writer = writer ?? new PoiIndexWriter();
    }

    public async Task<PoiExportResult> ExportFromPbfAsync(
        PoiExportRequest request,
        IProgress<OsmPoiExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var selectedTypes = request.SelectedPoiTypes
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectedTypes.Length == 0)
                return PoiExportResult.Fail("No selected POI types.");
            if (string.IsNullOrWhiteSpace(request.PbfPath) || !File.Exists(request.PbfPath))
                return PoiExportResult.Fail("PBF file missing.");
            if (!request.IndexOptions.GenerateFullPoisJsonl && !request.IndexOptions.GenerateTileIndex)
                return PoiExportResult.Fail("No POI output selected.");

            var indexOptions = request.IndexOptions.Normalize();
            var manifestInput = new PoiManifestInput
            {
                Source = new PoiSourceInfo
                {
                    Type = "osm-pbf",
                    Name = Path.GetFileName(request.PbfPath),
                    Provider = string.IsNullOrWhiteSpace(request.SourceProvider) ? "local" : request.SourceProvider.Trim(),
                    DownloadUrl = request.SourceDownloadUrl ?? string.Empty,
                    License = "ODbL",
                },
                Area = new PoiAreaInfo
                {
                    Name = string.IsNullOrWhiteSpace(request.AreaName) ? "Selection" : request.AreaName.Trim(),
                    AdminLevel = request.AreaAdminLevel,
                    Bounds = request.Bounds.Normalize(),
                },
                Index = indexOptions,
                PoiTypes = selectedTypes,
                NameLanguage = string.IsNullOrWhiteSpace(request.NameLanguage) ? "default" : request.NameLanguage.Trim(),
            };

            var summary = await _writer.WriteStreamingAsync(
                    request.MapsRoot,
                    manifestInput,
                    async onPoi =>
                    {
                        await _extractor.ExtractToAsync(
                                new OsmPoiExtractionOptions
                                {
                                    PbfPath = request.PbfPath,
                                    Bounds = request.Bounds,
                                    BoundaryGeoJson = request.BoundaryGeoJson,
                                    SelectedPoiTypes = selectedTypes,
                                    IncludeOriginalTags = indexOptions.IncludeOriginalTags,
                                    IncludeWays = true,
                                    NameLanguage = manifestInput.NameLanguage,
                                },
                                onPoi,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return new PoiExportResult
            {
                Success = true,
                PoiRoot = Path.Combine(request.MapsRoot, "poi"),
                SourcePoiCount = summary.SourcePoiCount,
                IndexRowsWritten = summary.IndexRowsWritten,
                TileFilesWritten = summary.TileFilesWritten,
                WasAnyTileClipped = summary.WasAnyTileClipped,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PoiExportResult.Fail(ex.Message);
        }
    }
}
