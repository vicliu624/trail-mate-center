using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.StateMachine;

public sealed class RequestTracker
{
    private readonly Dictionary<ushort, PendingRequest> _pending = new();
    private ushort _seq;

    public IReadOnlyCollection<PendingRequest> Pending => _pending.Values;

    public ushort NextSeq()
    {
        _seq++;
        if (_seq == 0)
            _seq = 1;
        return _seq;
    }

    public PendingRequest Register(HostLinkFrameType commandType, TimeSpan ackTimeout, int maxRetries)
    {
        var seq = NextSeq();
        var pending = new PendingRequest
        {
            Seq = seq,
            CommandType = commandType,
            AckTimeout = ackTimeout,
            MaxRetries = maxRetries,
            LastSendAt = DateTimeOffset.UtcNow,
        };
        _pending[seq] = pending;
        return pending;
    }

    public bool TryGet(ushort seq, out PendingRequest pending) => _pending.TryGetValue(seq, out pending!);

    public void HandleAck(ushort seq, HostLinkErrorCode code)
    {
        if (_pending.TryGetValue(seq, out var pending))
        {
            pending.IsAcked = true;
            pending.Acked.TrySetResult(code);
        }
    }

    public void HandleResult(TxResult result)
    {
        foreach (var pending in _pending.Values)
        {
            pending.Result.TrySetResult(result);
            break;
        }
    }

    public void Complete(ushort seq)
    {
        _pending.Remove(seq);
    }

    public IEnumerable<PendingRequest> GetTimedOut(DateTimeOffset now)
    {
        foreach (var pending in _pending.Values)
        {
            if (!pending.IsAcked && now - pending.LastSendAt >= pending.AckTimeout)
                yield return pending;
        }
    }
}
