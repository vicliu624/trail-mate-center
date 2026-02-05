using TrailMateCenter.Models;

namespace TrailMateCenter.Storage;

public sealed class SessionStore
{
    private readonly List<MessageEntry> _messages = new();
    private readonly List<HostLinkEvent> _events = new();
    private readonly List<TacticalEvent> _tacticalEvents = new();
    private readonly List<PositionUpdate> _positions = new();
    private readonly Dictionary<uint, NodeInfoUpdate> _nodeInfos = new();
    private readonly List<RawFrameRecord> _rawFrames = new();
    private readonly object _gate = new();

    public event EventHandler<MessageEntry>? MessageAdded;
    public event EventHandler<MessageEntry>? MessageUpdated;
    public event EventHandler<HostLinkEvent>? EventAdded;
    public event EventHandler<TacticalEvent>? TacticalEventAdded;
    public event EventHandler<PositionUpdate>? PositionUpdated;
    public event EventHandler<NodeInfoUpdate>? NodeInfoUpdated;
    public event EventHandler<RawFrameRecord>? RawFrameAdded;

    public void AddMessage(MessageEntry message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
        MessageAdded?.Invoke(this, message);
    }

    public void UpdateMessage(MessageEntry message)
    {
        MessageUpdated?.Invoke(this, message);
    }

    public void AddEvent(HostLinkEvent ev)
    {
        lock (_gate)
        {
            _events.Add(ev);
        }
        EventAdded?.Invoke(this, ev);
    }

    public void AddTacticalEvent(TacticalEvent ev)
    {
        lock (_gate)
        {
            _tacticalEvents.Add(ev);
        }
        TacticalEventAdded?.Invoke(this, ev);
    }

    public void AddPositionUpdate(PositionUpdate update)
    {
        lock (_gate)
        {
            _positions.Add(update);
        }
        PositionUpdated?.Invoke(this, update);
    }

    public void AddOrUpdateNodeInfo(NodeInfoUpdate info)
    {
        lock (_gate)
        {
            _nodeInfos[info.NodeId] = info;
        }
        NodeInfoUpdated?.Invoke(this, info);
    }

    public void AddRawFrame(RawFrameRecord frame)
    {
        lock (_gate)
        {
            _rawFrames.Add(frame);
            if (_rawFrames.Count > 5000)
                _rawFrames.RemoveAt(0);
        }
        RawFrameAdded?.Invoke(this, frame);
    }

    public IReadOnlyList<MessageEntry> SnapshotMessages()
    {
        lock (_gate)
        {
            return _messages.ToList();
        }
    }

    public IReadOnlyList<HostLinkEvent> SnapshotEvents()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<TacticalEvent> SnapshotTacticalEvents()
    {
        lock (_gate)
        {
            return _tacticalEvents.ToList();
        }
    }

    public IReadOnlyList<PositionUpdate> SnapshotPositions()
    {
        lock (_gate)
        {
            return _positions.ToList();
        }
    }

    public IReadOnlyList<NodeInfoUpdate> SnapshotNodeInfos()
    {
        lock (_gate)
        {
            return _nodeInfos.Values.ToList();
        }
    }

    public IReadOnlyList<RawFrameRecord> SnapshotRawFrames()
    {
        lock (_gate)
        {
            return _rawFrames.ToList();
        }
    }
}
