using TrailMateCenter.Transport;

namespace TrailMateCenter.Tests;

public sealed class FakeTransport : IHostLinkTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<TransportError>? TransportError;

    public bool IsOpen { get; private set; }
    public string? ConnectionName { get; private set; }

    public int OpenCount { get; private set; }
    public List<byte[]> Writes { get; } = new();
    public Func<ReadOnlyMemory<byte>, Task>? OnWriteAsync { get; set; }

    public Task OpenAsync(TransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        ConnectionName = endpoint.ToString();
        IsOpen = true;
        OpenCount++;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        Writes.Add(data.ToArray());
        if (OnWriteAsync is not null)
            await OnWriteAsync(data);
    }

    public void Inject(ReadOnlyMemory<byte> data)
    {
        DataReceived?.Invoke(this, data);
    }

    public void RaiseError(TransportError error)
    {
        TransportError?.Invoke(this, error);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }
}
