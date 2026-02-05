using TrailMateCenter.Protocol;

namespace TrailMateCenter.StateMachine;

public sealed class PendingRequest
{
    public ushort Seq { get; init; }
    public HostLinkFrameType CommandType { get; init; }
    public byte[] FrameBytes { get; set; } = Array.Empty<byte>();
    public DateTimeOffset LastSendAt { get; set; } = DateTimeOffset.UtcNow;
    public int Retries { get; set; }
    public int MaxRetries { get; init; }
    public TimeSpan AckTimeout { get; init; }
    public bool IsAcked { get; set; }

    public TaskCompletionSource<HostLinkErrorCode> Acked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<Models.TxResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
