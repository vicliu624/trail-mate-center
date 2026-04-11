namespace TrailMateCenter.Services;

public static class PropagationLandcoverModel
{
    // The effective fill factors keep the synthetic preview and solver aligned with
    // the current scenario scale while still following length-based accumulation.
    private const double SparseForestFillFactor = 0.012d;
    private const double DenseForestFillFactor = 0.018d;
    private const double WaterSurfaceLossDbPerMeter = 0.002d;

    public static double ResolvePathLossDb(
        PropagationLandcoverClass landcoverClass,
        double segmentLengthM,
        double vegetationAlphaSparse,
        double vegetationAlphaDense)
    {
        var clampedSegmentLengthM = Math.Max(0d, segmentLengthM);
        if (clampedSegmentLengthM <= 0d)
            return 0d;

        return landcoverClass switch
        {
            PropagationLandcoverClass.SparseForest => vegetationAlphaSparse * clampedSegmentLengthM * SparseForestFillFactor,
            PropagationLandcoverClass.DenseForest => vegetationAlphaDense * clampedSegmentLengthM * DenseForestFillFactor,
            PropagationLandcoverClass.Water => clampedSegmentLengthM * WaterSurfaceLossDbPerMeter,
            _ => 0d,
        };
    }
}
