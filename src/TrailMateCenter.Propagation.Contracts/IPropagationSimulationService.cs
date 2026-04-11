namespace TrailMateCenter.Services;

public interface IPropagationSimulationService
{
    Task<PropagationRunHandle> StartSimulationAsync(PropagationSimulationRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<PropagationSimulationUpdate> StreamSimulationUpdatesAsync(string runId, CancellationToken cancellationToken);

    Task<PropagationSimulationResult> GetSimulationResultAsync(string runId, CancellationToken cancellationToken);

    Task PauseSimulationAsync(string runId, CancellationToken cancellationToken);

    Task ResumeSimulationAsync(string runId, CancellationToken cancellationToken);

    Task CancelSimulationAsync(string runId, CancellationToken cancellationToken);

    Task<PropagationExportResult> ExportResultAsync(string runId, string outputDirectory, CancellationToken cancellationToken);
}
