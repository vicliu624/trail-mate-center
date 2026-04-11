using System.Collections.Concurrent;
using System.Threading.Channels;
using TrailMateCenter.Propagation.Engine;
using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Service.Runtime;

internal sealed class PropagationJobStore
{
    private readonly IPropagationSolver _solver;
    private readonly ConcurrentDictionary<string, PropagationJobContext> _jobs = new(StringComparer.Ordinal);

    public PropagationJobStore(IPropagationSolver solver)
    {
        _solver = solver;
    }

    public PropagationJobContext Start(PropagationSimulationRequest request)
    {
        var runId = $"prop_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Random.Shared.Next(1000, 9999)}";
        var startedAt = DateTimeOffset.UtcNow;
        var channel = Channel.CreateUnbounded<PropagationSimulationUpdate>();
        var cts = new CancellationTokenSource();
        var context = new PropagationJobContext(runId, request, startedAt, channel, cts);
        _jobs[runId] = context;
        _ = RunAsync(context);
        return context;
    }

    public PropagationJobContext Get(string runId)
    {
        if (_jobs.TryGetValue(runId, out var context))
            return context;

        throw new InvalidOperationException($"Propagation run not found: {runId}");
    }

    public async Task PauseAsync(string runId, CancellationToken cancellationToken)
    {
        var context = Get(runId);
        context.IsPaused = true;
        await PublishAsync(context, PropagationJobState.Paused, context.ProgressPercent, "paused", "Paused by user", cancellationToken);
    }

    public async Task ResumeAsync(string runId, CancellationToken cancellationToken)
    {
        var context = Get(runId);
        context.IsPaused = false;
        await PublishAsync(context, PropagationJobState.Running, Math.Max(context.ProgressPercent, 1), "resume", "Resumed", cancellationToken);
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        var context = Get(runId);
        context.IsCanceled = true;
        context.Cancellation.Cancel();
        await PublishAsync(context, PropagationJobState.Canceled, context.ProgressPercent, "canceled", "Canceled", cancellationToken);
        context.Channel.Writer.TryComplete();
    }

    private async Task RunAsync(PropagationJobContext context)
    {
        try
        {
            await PublishAsync(context, PropagationJobState.Queued, 0, "queued", "Task queued", CancellationToken.None);

            var stages = new[]
            {
                ("data_alignment", 12d, "Preparing DEM, landcover, and aligned grids"),
                ("profile_sampling", 28d, "Sampling terrain profiles and LOS geometry"),
                ("loss_and_probability", 48d, "Evaluating path loss, reliability, and coverage probability"),
                ("network_and_capacity", 68d, "Evaluating SINR, ALOHA load, and capacity"),
                ("relay_and_uncertainty", 84d, "Evaluating relay optimization, uncertainty, and calibration"),
                ("finalize", 96d, "Packaging rasters, geometry, and evidence outputs"),
            };

            foreach (var (stage, progress, message) in stages)
            {
                while (context.IsPaused && !context.IsCanceled)
                    await Task.Delay(150, context.Cancellation.Token);

                context.Cancellation.Token.ThrowIfCancellationRequested();
                context.ProgressPercent = progress;
                context.State = PropagationJobState.Running;
                await PublishAsync(context, PropagationJobState.Running, progress, stage, message, context.Cancellation.Token);
                await Task.Delay(120, context.Cancellation.Token);
            }

            context.Result = await _solver.SolveAsync(context.RunId, context.Request, context.StartedAtUtc, context.Cancellation.Token);
            context.ProgressPercent = 100;
            context.State = PropagationJobState.Completed;
            await PublishAsync(context, PropagationJobState.Completed, 100, "completed", "Completed", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            context.State = PropagationJobState.Canceled;
        }
        catch (Exception ex)
        {
            context.State = PropagationJobState.Failed;
            await PublishAsync(context, PropagationJobState.Failed, context.ProgressPercent, "failed", ex.Message, CancellationToken.None);
        }
        finally
        {
            context.Channel.Writer.TryComplete();
        }
    }

    private static ValueTask PublishAsync(
        PropagationJobContext context,
        PropagationJobState state,
        double progress,
        string stage,
        string message,
        CancellationToken cancellationToken)
    {
        return context.Channel.Writer.WriteAsync(new PropagationSimulationUpdate
        {
            RunId = context.RunId,
            State = state,
            ProgressPercent = progress,
            Stage = stage,
            CacheHit = false,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }
}

internal sealed class PropagationJobContext
{
    public PropagationJobContext(
        string runId,
        PropagationSimulationRequest request,
        DateTimeOffset startedAtUtc,
        Channel<PropagationSimulationUpdate> channel,
        CancellationTokenSource cancellation)
    {
        RunId = runId;
        Request = request;
        StartedAtUtc = startedAtUtc;
        Channel = channel;
        Cancellation = cancellation;
    }

    public string RunId { get; }
    public PropagationSimulationRequest Request { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public Channel<PropagationSimulationUpdate> Channel { get; }
    public CancellationTokenSource Cancellation { get; }
    public PropagationJobState State { get; set; } = PropagationJobState.Queued;
    public double ProgressPercent { get; set; }
    public bool IsPaused { get; set; }
    public bool IsCanceled { get; set; }
    public PropagationSimulationResult? Result { get; set; }
}
