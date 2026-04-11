using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TrailMateCenter.Propagation.Engine;

namespace TrailMateCenter.Services;

public sealed class InProcessPropagationSimulationService : IPropagationSimulationService
{
    private readonly IPropagationSolver _solver;
    private readonly ILogger<InProcessPropagationSimulationService> _logger;
    private readonly ConcurrentDictionary<string, RunContext> _runs = new(StringComparer.Ordinal);

    public InProcessPropagationSimulationService(
        IPropagationSolver solver,
        ILogger<InProcessPropagationSimulationService> logger)
    {
        _solver = solver;
        _logger = logger;
    }

    public Task<PropagationRunHandle> StartSimulationAsync(PropagationSimulationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runId = $"run_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Random.Shared.Next(1000, 9999)}";
        var context = new RunContext
        {
            Request = request,
            RunId = runId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            State = PropagationJobState.Queued,
            Stage = "queued",
            Message = "Queued",
        };

        if (!_runs.TryAdd(runId, context))
            throw new InvalidOperationException($"Failed to register run {runId}.");

        context.ExecutionTask = Task.Run(() => ExecuteAsync(context), CancellationToken.None);

        return Task.FromResult(new PropagationRunHandle
        {
            RunId = runId,
            InitialState = PropagationJobState.Queued,
        });
    }

    public async IAsyncEnumerable<PropagationSimulationUpdate> StreamSimulationUpdatesAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var context = GetRunOrThrow(runId);
        var lastFingerprint = string.Empty;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PropagationSimulationUpdate? snapshot;
            lock (context.SyncRoot)
            {
                snapshot = BuildUpdate(context);
            }

            var fingerprint = $"{snapshot.State}|{snapshot.ProgressPercent:F1}|{snapshot.Stage}|{snapshot.Message}";
            if (!string.Equals(fingerprint, lastFingerprint, StringComparison.Ordinal))
            {
                lastFingerprint = fingerprint;
                yield return snapshot;
            }

            if (snapshot.State is PropagationJobState.Completed or PropagationJobState.Failed or PropagationJobState.Canceled)
                yield break;

            await Task.Delay(120, cancellationToken);
        }
    }

    public Task<PropagationSimulationResult> GetSimulationResultAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = GetRunOrThrow(runId);

        lock (context.SyncRoot)
        {
            if (context.Result is not null)
                return Task.FromResult(context.Result);

            if (context.Failure is not null)
                throw new InvalidOperationException("Propagation run failed.", context.Failure);
        }

        throw new InvalidOperationException("Result is not ready yet.");
    }

    public Task PauseSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = GetRunOrThrow(runId);

        lock (context.SyncRoot)
        {
            if (context.State is PropagationJobState.Completed or PropagationJobState.Canceled or PropagationJobState.Failed)
                return Task.CompletedTask;

            context.IsPauseRequested = true;
            context.Message = "Paused by user";
            context.Stage = "paused";
            context.State = PropagationJobState.Paused;
        }

        return Task.CompletedTask;
    }

    public Task ResumeSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = GetRunOrThrow(runId);

        lock (context.SyncRoot)
        {
            if (context.State is PropagationJobState.Completed or PropagationJobState.Canceled or PropagationJobState.Failed)
                return Task.CompletedTask;

            context.IsPauseRequested = false;
            if (context.State == PropagationJobState.Paused)
            {
                context.State = PropagationJobState.Running;
                context.Message = "Resumed";
                context.Stage = ResolveStageName(context.ProgressPercent);
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelSimulationAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = GetRunOrThrow(runId);

        lock (context.SyncRoot)
        {
            if (context.State is PropagationJobState.Completed or PropagationJobState.Canceled or PropagationJobState.Failed)
                return Task.CompletedTask;

            context.State = PropagationJobState.Canceled;
            context.Stage = "canceled";
            context.Message = "Canceled";
            context.Cancellation.Cancel();
        }

        return Task.CompletedTask;
    }

    public Task<PropagationExportResult> ExportResultAsync(string runId, string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = GetRunOrThrow(runId);

        lock (context.SyncRoot)
        {
            if (context.Result is null)
                throw new InvalidOperationException("Result is not ready yet.");
        }

        var safeDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? "Documents/TrailMateCenter/exports"
            : outputDirectory.Trim();

        return Task.FromResult(new PropagationExportResult
        {
            RunId = runId,
            ExportPath = $"{safeDir.TrimEnd('/', '\\')}\\{runId}.zip",
            ExportedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private async Task ExecuteAsync(RunContext context)
    {
        try
        {
            await AdvanceStageAsync(context, 5, "queued", "Queued");
            await AdvanceStageAsync(context, 20, "terrain_sampling", "Sampling terrain");
            await AdvanceStageAsync(context, 40, "los_and_pathloss", "Evaluating LOS and path loss");
            await AdvanceStageAsync(context, 60, "diffraction_and_fresnel", "Evaluating diffraction and Fresnel clearance");

            var result = await _solver.SolveAsync(
                context.RunId,
                context.Request,
                context.StartedAtUtc,
                context.Cancellation.Token);

            lock (context.SyncRoot)
            {
                if (context.Cancellation.IsCancellationRequested)
                    return;

                context.Result = result;
                context.State = PropagationJobState.Completed;
                context.ProgressPercent = 100;
                context.Stage = "completed";
                context.Message = "Completed";
            }
        }
        catch (OperationCanceledException) when (context.Cancellation.IsCancellationRequested)
        {
            lock (context.SyncRoot)
            {
                context.State = PropagationJobState.Canceled;
                context.Stage = "canceled";
                context.Message = "Canceled";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "In-process propagation run failed. RunId={RunId}", context.RunId);
            lock (context.SyncRoot)
            {
                context.Failure = ex;
                context.State = PropagationJobState.Failed;
                context.Stage = "failed";
                context.Message = ex.Message;
            }
        }
    }

    private async Task AdvanceStageAsync(RunContext context, double progress, string stage, string message)
    {
        await WaitWhilePausedAsync(context, context.Cancellation.Token);
        context.Cancellation.Token.ThrowIfCancellationRequested();

        lock (context.SyncRoot)
        {
            context.State = PropagationJobState.Running;
            context.ProgressPercent = progress;
            context.Stage = stage;
            context.Message = message;
        }

        await Task.Delay(120, context.Cancellation.Token);
    }

    private static async Task WaitWhilePausedAsync(RunContext context, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool paused;
            lock (context.SyncRoot)
            {
                paused = context.IsPauseRequested;
            }

            if (!paused)
                return;

            await Task.Delay(120, cancellationToken);
        }
    }

    private static PropagationSimulationUpdate BuildUpdate(RunContext context)
    {
        return new PropagationSimulationUpdate
        {
            RunId = context.RunId,
            State = context.State,
            ProgressPercent = context.ProgressPercent,
            Stage = context.Stage,
            CacheHit = false,
            Message = context.Message,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string ResolveStageName(double progressPercent)
    {
        return progressPercent switch
        {
            <= 5 => "queued",
            <= 20 => "terrain_sampling",
            <= 40 => "los_and_pathloss",
            <= 60 => "diffraction_and_fresnel",
            < 100 => "finalize",
            _ => "completed",
        };
    }

    private RunContext GetRunOrThrow(string runId)
    {
        if (_runs.TryGetValue(runId, out var context))
            return context;

        throw new InvalidOperationException($"Run not found: {runId}");
    }

    private sealed class RunContext
    {
        public object SyncRoot { get; } = new();
        public required string RunId { get; init; }
        public required PropagationSimulationRequest Request { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public PropagationJobState State { get; set; }
        public double ProgressPercent { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsPauseRequested { get; set; }
        public PropagationSimulationResult? Result { get; set; }
        public Exception? Failure { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? ExecutionTask { get; set; }
    }
}
