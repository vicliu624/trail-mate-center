using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

public interface IPropagationSolver
{
    Task<PropagationSimulationResult> SolveAsync(
        string runId,
        PropagationSimulationRequest request,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken);
}
