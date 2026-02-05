using Microsoft.Extensions.Logging;
using TrailMateCenter.Helpers;
using TrailMateCenter.Replay;

namespace TrailMateCenter.Transport;

public sealed class ReplayTransport : IHostLinkTransport
{
    private readonly ILogger _logger;
    private readonly ReplaySource _source = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private string? _filePath;

    public ReplayTransport(ILogger logger)
    {
        _logger = logger;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<TransportError>? TransportError;

    public bool IsOpen => _task is not null && !_task.IsCompleted;
    public string? ConnectionName => _filePath;

    public Task OpenAsync(TransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint is not ReplayEndpoint replayEndpoint)
            throw new ArgumentException("ReplayTransport requires ReplayEndpoint", nameof(endpoint));

        _filePath = replayEndpoint.FilePath;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = Task.Run(() => RunReplayAsync(replayEndpoint, _cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        if (_task is not null)
            await _task.WaitAsync(cancellationToken);
        _cts.Dispose();
        _cts = null;
        _task = null;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        // Replay mode ignores outgoing writes but keeps the API consistent.
        _logger.LogDebug("Replay transport ignoring outgoing {Length} bytes", data.Length);
        return Task.CompletedTask;
    }

    private async Task RunReplayAsync(ReplayEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset? firstTimestamp = null;
            var start = DateTimeOffset.UtcNow;

            await foreach (var record in _source.ReadAsync(endpoint.FilePath, cancellationToken))
            {
                if (!string.Equals(record.Direction, "rx", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (firstTimestamp is null)
                    firstTimestamp = record.Timestamp;

                var relative = record.Timestamp - firstTimestamp.Value;
                var delay = TimeSpan.FromMilliseconds(relative.TotalMilliseconds / Math.Max(0.01, endpoint.Speed));
                var targetTime = start + delay;
                var wait = targetTime - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken);

                var bytes = HexUtils.FromHex(record.Hex);
                DataReceived?.Invoke(this, bytes);
            }

            TransportError?.Invoke(this, new TransportError(TransportErrorType.ReplayCompleted, "Replay finished"));
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            TransportError?.Invoke(this, new TransportError(TransportErrorType.ReadError, ex.Message, ex));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }
}
