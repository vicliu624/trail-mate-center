using System.Collections.Concurrent;
using TrailMateCenter.Models;

namespace TrailMateCenter.Services;

public sealed class LogStore
{
    private readonly ConcurrentQueue<HostLinkLogEntry> _entries = new();

    public event EventHandler<HostLinkLogEntry>? EntryAdded;

    public void Add(HostLinkLogEntry entry)
    {
        _entries.Enqueue(entry);
        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<HostLinkLogEntry> Snapshot()
    {
        return _entries.ToArray();
    }
}
