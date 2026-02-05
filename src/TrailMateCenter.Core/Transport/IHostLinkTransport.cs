namespace TrailMateCenter.Transport;

public interface IHostLinkTransport : IAsyncDisposable
{
    event EventHandler<ReadOnlyMemory<byte>> DataReceived;
    event EventHandler<TransportError> TransportError;

    bool IsOpen { get; }
    string? ConnectionName { get; }

    Task OpenAsync(TransportEndpoint endpoint, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
