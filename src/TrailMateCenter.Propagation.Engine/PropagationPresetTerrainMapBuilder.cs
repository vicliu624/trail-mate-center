using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

public static class PropagationPresetTerrainMapBuilder
{
    public static PropagationTerrainMapOutput Build(string presetName, IReadOnlyList<PropagationSiteInput>? scenarioSites = null)
    {
        var dataset = ScenarioTerrainDataset.Create(presetName);
        const double sampleStepM = 50d;
        var columns = (int)Math.Round(dataset.WidthM / sampleStepM) + 1;
        var rows = (int)Math.Round(dataset.HeightM / sampleStepM) + 1;
        var elevations = new double[columns * rows];
        var landcoverSamples = new PropagationLandcoverClass[columns * rows];
        var offset = 0;

        for (var row = 0; row < rows; row++)
        {
            var z = Math.Min(dataset.HeightM, row * sampleStepM);
            for (var col = 0; col < columns; col++)
            {
                var x = Math.Min(dataset.WidthM, col * sampleStepM);
                elevations[offset++] = dataset.ElevationAt(x, z);
                landcoverSamples[(row * columns) + col] = dataset.LandcoverAt(x, z);
            }
        }

        return new PropagationTerrainMapOutput
        {
            Crs = dataset.TargetCrs,
            WidthM = dataset.WidthM,
            HeightM = dataset.HeightM,
            SampleStepM = sampleStepM,
            Columns = columns,
            Rows = rows,
            MinElevationM = dataset.MinElevationM,
            MaxElevationM = dataset.MaxElevationM,
            ElevationSamples = elevations,
            LandcoverSamples = landcoverSamples,
            ContourLines = BuildContourLines(dataset, sampleStepM, columns, rows, elevations),
            Sites = ResolveSites(dataset, scenarioSites),
        };
    }

    private static IReadOnlyList<PropagationScenePoint> ResolveSites(ScenarioTerrainDataset dataset, IReadOnlyList<PropagationSiteInput>? scenarioSites)
    {
        if (scenarioSites is null || scenarioSites.Count == 0)
            return Array.Empty<PropagationScenePoint>();

        return scenarioSites.Select(site =>
        {
            var x = Math.Clamp(site.X, 0d, dataset.WidthM);
            var z = Math.Clamp(site.Z, 0d, dataset.HeightM);
            return new PropagationScenePoint
            {
                Id = site.Id,
                Label = site.Label,
                ColorHex = site.ColorHex,
                X = x,
                Z = z,
                Y = site.ElevationM ?? dataset.ElevationAt(x, z),
            };
        }).ToArray();
    }

    private static IReadOnlyList<PropagationScenePolyline> BuildContourLines(
        ScenarioTerrainDataset dataset,
        double sampleStepM,
        int columns,
        int rows,
        IReadOnlyList<double> elevations)
    {
        var contours = new List<PropagationScenePolyline>();
        var contourIndex = 0;

        foreach (var level in dataset.ContourBreaks)
        {
            for (var row = 0; row < rows - 1; row++)
            {
                for (var col = 0; col < columns - 1; col++)
                {
                    var corners = new[]
                    {
                        new ContourCorner(col * sampleStepM, row * sampleStepM, elevations[row * columns + col]),
                        new ContourCorner((col + 1) * sampleStepM, row * sampleStepM, elevations[row * columns + col + 1]),
                        new ContourCorner((col + 1) * sampleStepM, (row + 1) * sampleStepM, elevations[(row + 1) * columns + col + 1]),
                        new ContourCorner(col * sampleStepM, (row + 1) * sampleStepM, elevations[(row + 1) * columns + col]),
                    };

                    var intersections = new List<PropagationScenePolylinePoint>(4);
                    TryAddIntersection(intersections, corners[0], corners[1], level);
                    TryAddIntersection(intersections, corners[1], corners[2], level);
                    TryAddIntersection(intersections, corners[2], corners[3], level);
                    TryAddIntersection(intersections, corners[3], corners[0], level);

                    if (intersections.Count < 2)
                        continue;

                    for (var index = 0; index + 1 < intersections.Count; index += 2)
                    {
                        contours.Add(new PropagationScenePolyline
                        {
                            Id = $"preview_contour_{contourIndex++:0000}",
                            Points =
                            [
                                intersections[index],
                                intersections[index + 1],
                            ],
                        });
                    }
                }
            }
        }

        return contours;
    }

    private static void TryAddIntersection(List<PropagationScenePolylinePoint> intersections, ContourCorner a, ContourCorner b, double level)
    {
        var deltaA = a.Elevation - level;
        var deltaB = b.Elevation - level;

        if (Math.Abs(deltaA) < 0.001 && Math.Abs(deltaB) < 0.001)
            return;
        if (Math.Abs(deltaA) < 0.001)
        {
            intersections.Add(new PropagationScenePolylinePoint { X = a.X, Z = a.Z, Y = a.Elevation });
            return;
        }
        if (Math.Abs(deltaB) < 0.001)
        {
            intersections.Add(new PropagationScenePolylinePoint { X = b.X, Z = b.Z, Y = b.Elevation });
            return;
        }
        if ((deltaA < 0 && deltaB < 0) || (deltaA > 0 && deltaB > 0))
            return;

        var t = (level - a.Elevation) / (b.Elevation - a.Elevation);
        intersections.Add(new PropagationScenePolylinePoint
        {
            X = a.X + ((b.X - a.X) * t),
            Z = a.Z + ((b.Z - a.Z) * t),
            Y = level,
        });
    }

    private readonly record struct ContourCorner(double X, double Z, double Elevation);
}
