using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.StateMachine;

public sealed class RequestTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<ushort, PendingRequest> _pending = new();
    private ushort _seq;

    public IReadOnlyCollection<PendingRequest> Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Values.ToList();
            }
        }
    }

    public ushort NextSeq()
    {
        lock (_gate)
        {
            _seq++;
            if (_seq == 0)
                _seq = 1;
            return _seq;
        }
    }

    public PendingRequest Register(HostLinkFrameType commandType, TimeSpan ackTimeout, int maxRetries)
    {
        lock (_gate)
        {
            _seq++;
            if (_seq == 0)
                _seq = 1;

            var pending = new PendingRequest
            {
                Seq = _seq,
                CommandType = commandType,
                AckTimeout = ackTimeout,
                MaxRetries = maxRetries,
                LastSendAt = DateTimeOffset.UtcNow,
            };
            _pending[_seq] = pending;
            return pending;
        }
    }

    public bool TryGet(ushort seq, out PendingRequest pending)
    {
        lock (_gate)
        {
            return _pending.TryGetValue(seq, out pending!);
        }
    }

    public void HandleAck(ushort seq, HostLinkErrorCode code)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(seq, out var pending))
            {
                pending.IsAcked = true;
                pending.Acked.TrySetResult(code);
            }
        }
    }

    public void HandleResult(TxResult result)
    {
        lock (_gate)
        {
            foreach (var pending in _pending.Values)
            {
                pending.Result.TrySetResult(result);
                break;
            }
        }
    }

    public void Complete(ushort seq)
    {
        lock (_gate)
        {
            _pending.Remove(seq);
        }
    }

    public IEnumerable<PendingRequest> GetTimedOut(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _pending.Values
                .Where(pending => !pending.IsAcked && now - pending.LastSendAt >= pending.AckTimeout)
                .ToList();
        }
    }
}
