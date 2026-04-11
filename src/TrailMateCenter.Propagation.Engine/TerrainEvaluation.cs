using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

internal sealed class TerrainEvaluation
{
    public required ScenarioSite Tx { get; init; }
    public required ScenarioSite Rx { get; init; }
    public double DistanceKm { get; init; }
    public double ObstructionAboveLosM { get; init; }
    public double DiffractionDb { get; init; }
    public double FresnelAdditionalLossDb { get; init; }
    public double VegetationDb { get; init; }
    public double FresnelClearanceRatio { get; init; }
    public double MinimumClearanceM { get; init; }
    public double DominantObstacleDistanceKm { get; init; }
    public double DominantObstacleHeightM { get; init; }
    public double SampleStepM { get; init; }
    public required IReadOnlyList<PropagationScenePoint> RidgeCandidates { get; init; }
    public required PropagationProfileOutput ProfileOutput { get; init; }
    public required PropagationTerrainOutput TerrainOutput { get; init; }
    public required PropagationFresnelOutput FresnelOutput { get; init; }
}
