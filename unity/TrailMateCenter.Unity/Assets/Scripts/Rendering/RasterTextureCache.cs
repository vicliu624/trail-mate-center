using System.Collections.Generic;
using UnityEngine;
namespace TrailMateCenter.Unity.Rendering
{
public sealed class RasterTextureCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, CacheEntry> _entries = new();
    private readonly LinkedList<string> _lru = new();

    public RasterTextureCache(int capacity)
    {
        _capacity = Mathf.Max(4, capacity);
    }

    public bool TryGet(string key, out RasterLoadResult result)
    {
        result = null!;
        if (!_entries.TryGetValue(key, out var entry))
            return false;

        Touch(entry.Node);
        result = entry.Result;
        return true;
    }

    public void Put(string key, RasterLoadResult result)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Result = result;
            Touch(existing.Node);
            return;
        }

        var node = new LinkedListNode<string>(key);
        _lru.AddFirst(node);
        _entries[key] = new CacheEntry { Node = node, Result = result };
        Trim();
    }

    private void Touch(LinkedListNode<string> node)
    {
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void Trim()
    {
        while (_entries.Count > _capacity)
        {
            var last = _lru.Last;
            if (last == null)
                break;

            _lru.RemoveLast();
            if (!_entries.Remove(last.Value, out var entry))
                continue;

            if (entry.Result?.Texture != null)
                Object.Destroy(entry.Result.Texture);
        }
    }

    private sealed class CacheEntry
    {
        public LinkedListNode<string> Node { get; set; } = null!;
        public RasterLoadResult Result { get; set; } = null!;
    }
}
}

