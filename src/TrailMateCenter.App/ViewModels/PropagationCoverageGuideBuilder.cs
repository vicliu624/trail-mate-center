using System;
using System.Collections.Generic;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

internal static class PropagationCoverageGuideBuilder
{
    public static IReadOnlyList<PropagationScenePolyline> BuildMarginIsolines(
        IReadOnlyList<PropagationCoverageCellViewModel> cells,
        int columns,
        int rows,
        double thresholdDb)
    {
        if (columns < 2 || rows < 2 || cells.Count < columns * rows)
            return Array.Empty<PropagationScenePolyline>();

        var lines = new List<PropagationScenePolyline>();
        for (var row = 0; row < rows - 1; row++)
        {
            for (var col = 0; col < columns - 1; col++)
            {
                var c00 = cells[(row * columns) + col];
                var c10 = cells[(row * columns) + col + 1];
                var c01 = cells[((row + 1) * columns) + col];
                var c11 = cells[((row + 1) * columns) + col + 1];

                var intersections = new List<PropagationScenePoint>(4);
                TryAddIntersection(intersections, c00, c10, thresholdDb);
                TryAddIntersection(intersections, c10, c11, thresholdDb);
                TryAddIntersection(intersections, c11, c01, thresholdDb);
                TryAddIntersection(intersections, c01, c00, thresholdDb);

                if (intersections.Count == 2)
                {
                    lines.Add(CreateSegment(intersections[0], intersections[1]));
                }
                else if (intersections.Count == 4)
                {
                    lines.Add(CreateSegment(intersections[0], intersections[1]));
                    lines.Add(CreateSegment(intersections[2], intersections[3]));
                }
            }
        }

        return lines;
    }

    private static void TryAddIntersection(
        ICollection<PropagationScenePoint> intersections,
        PropagationCoverageCellViewModel start,
        PropagationCoverageCellViewModel end,
        double thresholdDb)
    {
        if (!start.IsComputed || !end.IsComputed)
            return;

        var startDelta = start.MarginDb - thresholdDb;
        var endDelta = end.MarginDb - thresholdDb;
        if ((startDelta < 0d && endDelta < 0d) || (startDelta > 0d && endDelta > 0d))
            return;

        if (Math.Abs(startDelta - endDelta) < 1e-9)
            return;

        var t = Math.Clamp((thresholdDb - start.MarginDb) / (end.MarginDb - start.MarginDb), 0d, 1d);
        intersections.Add(new PropagationScenePoint
        {
            X = Lerp(start.X, end.X, t),
            Z = Lerp(start.Z, end.Z, t),
        });
    }

    private static PropagationScenePolyline CreateSegment(PropagationScenePoint start, PropagationScenePoint end)
    {
        return new PropagationScenePolyline
        {
            Points = new[]
            {
                new PropagationScenePolylinePoint { X = start.X, Z = start.Z },
                new PropagationScenePolylinePoint { X = end.X, Z = end.Z },
            },
        };
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
