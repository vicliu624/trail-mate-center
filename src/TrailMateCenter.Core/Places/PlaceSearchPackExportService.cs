namespace TrailMateCenter.Places;

public sealed class PlaceSearchPackExportService
{
    private readonly OsmPlaceExtractor _extractor;
    private readonly PlaceSearchPackWriter _writer;

    public PlaceSearchPackExportService(
        OsmPlaceExtractor? extractor = null,
        PlaceSearchPackWriter? writer = null)
    {
        _extractor = extractor ?? new OsmPlaceExtractor();
        _writer = writer ?? new PlaceSearchPackWriter();
    }

    public async Task<PlaceSearchPackExportResult> ExportFromPbfAsync(
        PlaceSearchPackExportRequest request,
        IProgress<PlaceExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            if (string.IsNullOrWhiteSpace(request.OutputRoot))
                return PlaceSearchPackExportResult.Fail("Output root is missing.");
            if (string.IsNullOrWhiteSpace(request.PbfPath) || !File.Exists(request.PbfPath))
                return PlaceSearchPackExportResult.Fail("PBF file missing.");

            var areaName = string.IsNullOrWhiteSpace(request.AreaName) ? "Selection" : request.AreaName.Trim();
            var packId = PlaceSearchPackWriter.BuildPackId(BuildStablePackSeed(request, areaName), areaName);
            var manifestInput = new PlaceSearchPackManifestInput
            {
                PackId = packId,
                Source = new PlaceSourceInfo
                {
                    Type = "osm-pbf",
                    Name = Path.GetFileName(request.PbfPath),
                    Provider = string.IsNullOrWhiteSpace(request.SourceProvider) ? "local" : request.SourceProvider.Trim(),
                    DownloadUrl = request.SourceDownloadUrl ?? string.Empty,
                    License = "ODbL-1.0",
                },
                Area = new PlaceAreaInfo
                {
                    Name = areaName,
                    AdminLevel = request.AreaAdminLevel,
                    Bounds = request.Bounds.Normalize(),
                },
                NameLanguage = string.IsNullOrWhiteSpace(request.NameLanguage) ? "default" : request.NameLanguage.Trim(),
            };

            var summary = await _writer.WriteStreamingAsync(
                    request.OutputRoot,
                    manifestInput,
                    async onPlace =>
                    {
                        await _extractor.ExtractToAsync(
                                new PlaceExtractionOptions
                                {
                                    PbfPath = request.PbfPath,
                                    Bounds = request.Bounds,
                                    BoundaryGeoJson = request.BoundaryGeoJson,
                                    NameLanguage = manifestInput.NameLanguage,
                                    IncludeOriginalTags = request.IncludeOriginalTags,
                                },
                                onPlace,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return new PlaceSearchPackExportResult
            {
                Success = true,
                PlaceRoot = Path.Combine(request.OutputRoot, "places", "packs", summary.PackId),
                PlaceCount = summary.PlaceCount,
                NameRowsWritten = summary.NameRowsWritten,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PlaceSearchPackExportResult.Fail(ex.Message);
        }
    }

    private static string BuildStablePackSeed(PlaceSearchPackExportRequest request, string areaName)
    {
        var bounds = request.Bounds.Normalize();
        return string.Join(
            "|",
            areaName,
            request.AreaAdminLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            FormatCoordinate(bounds.West),
            FormatCoordinate(bounds.South),
            FormatCoordinate(bounds.East),
            FormatCoordinate(bounds.North),
            request.SourceProvider?.Trim() ?? string.Empty,
            request.SourceDownloadUrl?.Trim() ?? string.Empty,
            Path.GetFileName(request.PbfPath));
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
