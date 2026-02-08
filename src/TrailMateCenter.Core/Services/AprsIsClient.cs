using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public enum AprsIsConnectionState
{
    Disabled,
    Connecting,
    Connected,
    Error,
    Disconnected,
}

public sealed record AprsIsStatus(
    AprsIsConnectionState State,
    string? Message,
    int QueueLength,
    long Sent,
    long Dropped);

public sealed class AprsIsClient : IAsyncDisposable
{
    private readonly ConcurrentQueue<AprsQueuedLine> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AprsSettings _settings = new();
    private AprsIsStatus _status = new(AprsIsConnectionState.Disabled, null, 0, 0, 0);
    private long _sent;
    private long _dropped;

    private const int MaxQueueSize = 2000;

    public event EventHandler<AprsIsStatus>? StatusChanged;

    public AprsIsStatus Status => _status;

    public void ApplySettings(AprsSettings settings)
    {
        _settings = settings;
        EnsureLoop();
    }

    public void Enqueue(string line, DateTimeOffset? expiresAt)
    {
        if (!_settings.Enabled)
            return;

        if (_queue.Count >= MaxQueueSize)
        {
            Interlocked.Increment(ref _dropped);
            UpdateStatus(_status with { Dropped = _dropped, QueueLength = _queue.Count });
            return;
        }

        _queue.Enqueue(new AprsQueuedLine(line, expiresAt));
        _signal.Release();
        UpdateStatus(_status with { QueueLength = _queue.Count });
    }

    private void EnsureLoop()
    {
        lock (_gate)
        {
            if (_loop is not null && !_loop.IsCompleted)
                return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_loop is not null)
                await _loop;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.IgateCallsign) || string.IsNullOrWhiteSpace(_settings.Passcode))
            {
                var reason = !_settings.Enabled
                    ? "APRS-IS disabled"
                    : string.IsNullOrWhiteSpace(_settings.IgateCallsign)
                        ? "APRS-IS callsign missing"
                        : "APRS-IS passcode missing";
                UpdateStatus(new AprsIsStatus(AprsIsConnectionState.Disabled, reason, _queue.Count, _sent, _dropped));
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            UpdateStatus(new AprsIsStatus(AprsIsConnectionState.Connecting, $"Connecting {_settings.ServerHost}:{_settings.ServerPort}", _queue.Count, _sent, _dropped));

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_settings.ServerHost, _settings.ServerPort, cancellationToken);
                using var stream = client.GetStream();
                var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                var reader = new StreamReader(stream, Encoding.ASCII);

                var login = BuildLoginLine();
                await writer.WriteLineAsync(login.AsMemory(), cancellationToken);

                UpdateStatus(new AprsIsStatus(AprsIsConnectionState.Connected, "Connected", _queue.Count, _sent, _dropped));

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var readTask = Task.Run(() => ReadLoopAsync(reader, linked.Token), linked.Token);

                while (!linked.Token.IsCancellationRequested)
                {
                    if (!_queue.TryDequeue(out var item))
                    {
                        await _signal.WaitAsync(TimeSpan.FromSeconds(2), linked.Token);
                        continue;
                    }

                    if (item.ExpiresAt.HasValue && DateTimeOffset.UtcNow > item.ExpiresAt.Value)
                    {
                        Interlocked.Increment(ref _dropped);
                        continue;
                    }

                    await writer.WriteLineAsync(item.Line.AsMemory(), linked.Token);
                    Interlocked.Increment(ref _sent);
                    UpdateStatus(_status with { QueueLength = _queue.Count, Sent = _sent, Dropped = _dropped });
                }

                linked.Cancel();
                await readTask;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                UpdateStatus(new AprsIsStatus(AprsIsConnectionState.Error, ex.Message, _queue.Count, _sent, _dropped));
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (line is null)
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private string BuildLoginLine()
    {
        var callsign = _settings.IgateSsid > 0 ? $"{_settings.IgateCallsign}-{_settings.IgateSsid}" : _settings.IgateCallsign;
        var filter = string.IsNullOrWhiteSpace(_settings.Filter) ? string.Empty : $" filter {_settings.Filter}";
        return $"user {callsign} pass {_settings.Passcode} vers TrailMateCenter 0.1{filter}";
    }

    private void UpdateStatus(AprsIsStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private sealed record AprsQueuedLine(string Line, DateTimeOffset? ExpiresAt);
}
