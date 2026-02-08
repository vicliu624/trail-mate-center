using System.Collections;
using TrailMateCenter.Models;

namespace TrailMateCenter.Services;

public sealed class AppDataReassembler
{
    private readonly Dictionary<AppDataKey, AppDataAssembly> _assemblies = new();
    private readonly object _gate = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(20);

    public IReadOnlyList<AppDataPacket> Accept(AppDataEvent ev)
    {
        if (ev.TotalLength == 0)
        {
            var payload = (ev.ChunkLength > 0 && ev.Chunk.Length > 0) ? ev.Chunk : Array.Empty<byte>();
            return new[]
            {
                new AppDataPacket(
                    ev.Portnum,
                    ev.From,
                    ev.To,
                    ev.Channel,
                    ev.Flags,
                    ev.TeamId,
                    ev.TeamKeyId,
                    ev.DeviceUptimeSeconds,
                    payload,
                    ev.RxMeta)
            };
        }

        if (ev.ChunkLength == 0 || ev.Chunk.Length == 0)
            return Array.Empty<AppDataPacket>();

        var now = DateTimeOffset.UtcNow;
        List<AppDataPacket> completed = new();

        lock (_gate)
        {
            PruneStale(now);
            var key = new AppDataKey(ev.Portnum, ev.From, ev.To, ev.Channel, ev.TeamKeyId, ev.TeamId, ev.TotalLength, ev.DeviceUptimeSeconds);
            if (!_assemblies.TryGetValue(key, out var assembly))
            {
                assembly = new AppDataAssembly(ev.TotalLength)
                {
                    RxMeta = ev.RxMeta
                };
                _assemblies[key] = assembly;
            }
            else if (assembly.RxMeta is null && ev.RxMeta is not null)
            {
                assembly.RxMeta = ev.RxMeta;
            }

            if (ev.Offset < ev.TotalLength)
            {
                var remaining = ev.TotalLength - ev.Offset;
                var chunkLen = Math.Min((uint)ev.Chunk.Length, remaining);
                assembly.Write(ev.Offset, ev.Chunk.AsSpan(0, (int)chunkLen));
                assembly.LastUpdated = now;
            }

            if (assembly.IsComplete)
            {
                _assemblies.Remove(key);
                completed.Add(new AppDataPacket(
                    ev.Portnum,
                    ev.From,
                    ev.To,
                    ev.Channel,
                    ev.Flags,
                    ev.TeamId,
                    ev.TeamKeyId,
                    ev.DeviceUptimeSeconds,
                    assembly.Buffer,
                    assembly.RxMeta));
            }
        }

        return completed;
    }

    private void PruneStale(DateTimeOffset now)
    {
        var staleKeys = _assemblies.Where(kvp => now - kvp.Value.LastUpdated > _ttl).Select(kvp => kvp.Key).ToList();
        foreach (var key in staleKeys)
        {
            _assemblies.Remove(key);
        }
    }

    private sealed record AppDataKey(uint Portnum, uint From, uint To, byte Channel, uint TeamKeyId, byte[] TeamId, uint TotalLength, uint DeviceUptimeSeconds)
    {
        public bool Equals(AppDataKey? other)
        {
            if (other is null) return false;
            return Portnum == other.Portnum &&
                   From == other.From &&
                   To == other.To &&
                   Channel == other.Channel &&
                   TeamKeyId == other.TeamKeyId &&
                   TotalLength == other.TotalLength &&
                   DeviceUptimeSeconds == other.DeviceUptimeSeconds &&
                   TeamId.AsSpan().SequenceEqual(other.TeamId);
        }

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Portnum, From, To, Channel, TeamKeyId, TotalLength, DeviceUptimeSeconds);
            for (var i = 0; i < TeamId.Length; i++)
                hash = HashCode.Combine(hash, TeamId[i]);
            return hash;
        }
    }

    private sealed class AppDataAssembly
    {
        private readonly BitArray _received;
        private int _receivedCount;

        public AppDataAssembly(uint totalLength)
        {
            Buffer = new byte[totalLength];
            _received = new BitArray((int)totalLength);
            LastUpdated = DateTimeOffset.UtcNow;
        }

        public byte[] Buffer { get; }
        public DateTimeOffset LastUpdated { get; set; }
        public bool IsComplete => _receivedCount == _received.Length;
        public RxMetadata? RxMeta { get; set; }

        public void Write(uint offset, ReadOnlySpan<byte> data)
        {
            var start = (int)offset;
            if (start < 0 || start >= Buffer.Length)
                return;
            var count = Math.Min(data.Length, Buffer.Length - start);
            data[..count].CopyTo(Buffer.AsSpan(start, count));
            for (var i = 0; i < count; i++)
            {
                var idx = start + i;
                if (!_received[idx])
                {
                    _received[idx] = true;
                    _receivedCount++;
                }
            }
        }
    }
}
