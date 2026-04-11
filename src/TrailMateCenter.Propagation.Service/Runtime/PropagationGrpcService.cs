using Grpc.Core;
using TrailMateCenter.Propagation.Grpc;

namespace TrailMateCenter.Propagation.Service.Runtime;

internal sealed class PropagationGrpcService : PropagationService.PropagationServiceBase
{
    private readonly PropagationJobStore _jobs;

    public PropagationGrpcService(PropagationJobStore jobs)
    {
        _jobs = jobs;
    }

    public override Task<StartSimulationResponse> StartSimulation(StartSimulationRequest request, ServerCallContext context)
    {
        var contract = PropagationGrpcMapper.ToContract(request);
        var job = _jobs.Start(contract);
        return Task.FromResult(new StartSimulationResponse
        {
            RunId = job.RunId,
            InitialState = JobState.Queued,
        });
    }

    public override async Task StreamSimulationUpdates(StreamSimulationUpdatesRequest request, IServerStreamWriter<SimulationUpdateEvent> responseStream, ServerCallContext context)
    {
        var job = _jobs.Get(request.RunId);
        await foreach (var update in job.Channel.Reader.ReadAllAsync(context.CancellationToken))
            await responseStream.WriteAsync(PropagationGrpcMapper.ToGrpc(update));
    }

    public override Task<SimulationResultResponse> GetSimulationResult(GetSimulationResultRequest request, ServerCallContext context)
    {
        var job = _jobs.Get(request.RunId);
        if (job.Result is null)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Result is not ready yet."));

        return Task.FromResult(PropagationGrpcMapper.ToGrpc(job.Result));
    }

    public override async Task<OperationAck> PauseSimulation(PauseSimulationRequest request, ServerCallContext context)
    {
        await _jobs.PauseAsync(request.RunId, context.CancellationToken);
        return new OperationAck { Success = true, Message = "paused" };
    }

    public override async Task<OperationAck> ResumeSimulation(ResumeSimulationRequest request, ServerCallContext context)
    {
        await _jobs.ResumeAsync(request.RunId, context.CancellationToken);
        return new OperationAck { Success = true, Message = "resumed" };
    }

    public override async Task<CancelJobResponse> CancelJob(CancelJobRequest request, ServerCallContext context)
    {
        await _jobs.CancelAsync(request.JobId, context.CancellationToken);
        return new CancelJobResponse { Canceled = true };
    }

    public override Task<ExportResultResponse> ExportResult(ExportResultRequest request, ServerCallContext context)
    {
        var job = _jobs.Get(request.RunId);
        if (job.Result is null)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Result is not ready yet."));

        var path = Path.Combine(
            string.IsNullOrWhiteSpace(request.OutputDirectory) ? "exports" : request.OutputDirectory,
            $"{request.RunId}.zip");

        return Task.FromResult(new ExportResultResponse
        {
            RunId = request.RunId,
            ExportPath = path,
            ExportedAtUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        });
    }
}
