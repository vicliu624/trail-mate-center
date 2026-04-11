using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.Bridge
{
public sealed class BridgeTransport : IDisposable
{
    private readonly BridgeConfig _config;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Stream? _stream;
    private NamedPipeServerStream? _pipeServer;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;

    public event Action<BridgeEnvelope>? EnvelopeReceived;
    public event Action<string>? ConnectionStateChanged;

    public BridgeTransport(BridgeConfig config)
    {
        _config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Stop();
        _listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (string.Equals(_config.Transport, "tcp", StringComparison.OrdinalIgnoreCase))
            _stream = await AcceptTcpAsync(_listenCts.Token);
        else
            _stream = await AcceptNamedPipeAsync(_listenCts.Token);

        _reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), bufferSize: 16 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };

        ConnectionStateChanged?.Invoke("connected");
        _listenTask = Task.Run(() => ListenLoopAsync(_listenCts.Token), CancellationToken.None);
    }

    public void Stop()
    {
        try { _listenCts?.Cancel(); } catch { /* ignore */ }
        try { _tcpListener?.Stop(); } catch { /* ignore */ }
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _pipeServer?.Dispose(); } catch { /* ignore */ }

        _listenTask = null;
        _reader = null;
        _writer = null;
        _stream = null;
        _pipeServer = null;
        _tcpListener = null;
        _listenCts = null;
    }

    public async Task SendAsync(JObject payload, CancellationToken cancellationToken)
    {
        if (_writer == null)
            return;

        var json = payload.ToString(Formatting.None);
        if (cancellationToken.IsCancellationRequested)
            return;
        await _writer.WriteLineAsync(json);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_reader == null)
                    break;

                string? line;
                try
                {
                    line = await _reader.ReadLineAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var envelope = BridgeEnvelope.FromJson(line);
                EnvelopeReceived?.Invoke(envelope);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Bridge] listen loop error: {ex.Message}");
        }
        finally
        {
            ConnectionStateChanged?.Invoke("disconnected");
        }
    }

    private async Task<Stream> AcceptNamedPipeAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeServerStream(
            _config.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _pipeServer = pipe;
        try
        {
            Debug.Log($"[Bridge] waiting on named pipe '{_config.PipeName}'");
            using (cancellationToken.Register(() =>
                   {
                       try { pipe.Dispose(); } catch { /* ignore */ }
                   }))
            {
                await pipe.WaitForConnectionAsync();
            }

            Debug.Log($"[Bridge] named pipe connected '{_config.PipeName}'");
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        return pipe;
    }

    private async Task<Stream> AcceptTcpAsync(CancellationToken cancellationToken)
    {
        var ip = IPAddress.TryParse(_config.TcpHost, out var parsed) ? parsed : IPAddress.Loopback;
        var listener = new TcpListener(ip, _config.TcpPort);
        _tcpListener = listener;
        listener.Start();

        TcpClient? client = null;
        try
        {
            using (cancellationToken.Register(() =>
                   {
                       try { listener.Stop(); } catch { /* ignore */ }
                   }))
            {
                client = await listener.AcceptTcpClientAsync();
            }
        }
        catch
        {
            client?.Dispose();
            throw;
        }

        return client.GetStream();
    }

    public Task? ListenTask => _listenTask;

    public void Dispose()
    {
        Stop();
    }
}
}

