using Microsoft.Extensions.Logging;
using System.IO.Ports;

namespace TrailMateCenter.Transport;

public sealed class SerialTransport : IHostLinkTransport
{
    private readonly ILogger _logger;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;

    public SerialTransport(ILogger logger)
    {
        _logger = logger;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<TransportError>? TransportError;

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? ConnectionName => _port?.PortName;

    public async Task OpenAsync(TransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint is not SerialEndpoint serialEndpoint)
            throw new ArgumentException("SerialTransport requires SerialEndpoint", nameof(endpoint));

        if (IsOpen)
            return;

        try
        {
            _port = new SerialPort(serialEndpoint.PortName, serialEndpoint.BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true,
            };

            _port.Open();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            TransportError?.Invoke(this, new TransportError(TransportErrorType.OpenFailed, ex.Message, ex));
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_port is null)
            return;

        try
        {
            _cts?.Cancel();
            if (_readerTask is not null)
                await _readerTask.WaitAsync(cancellationToken);
        }
        catch
        {
            // ignored
        }
        finally
        {
            try { _port.Close(); } catch { }
            _port.Dispose();
            _port = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_port is null || !_port.IsOpen)
            throw new InvalidOperationException("Serial port not open");

        try
        {
            await _port.BaseStream.WriteAsync(data, cancellationToken);
            await _port.BaseStream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            TransportError?.Invoke(this, new TransportError(TransportErrorType.WriteError, ex.Message, ex));
            throw;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_port is null)
                    break;

                var read = await _port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                DataReceived?.Invoke(this, buffer.AsMemory(0, read));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Serial read error");
                TransportError?.Invoke(this, new TransportError(TransportErrorType.ReadError, ex.Message, ex));
                break;
            }
        }

        TransportError?.Invoke(this, new TransportError(TransportErrorType.Disconnected, "Serial port closed"));
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }
}
