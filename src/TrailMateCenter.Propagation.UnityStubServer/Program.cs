using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TrailMateCenter.Propagation.UnityStubServer;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public static async Task Main(string[] args)
    {
        var options = StubOptions.FromEnvironment();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"[UnityStub] transport={options.TransportMode}");
        Console.WriteLine($"[UnityStub] pipe={options.PipeName}");
        Console.WriteLine($"[UnityStub] tcp={options.TcpHost}:{options.TcpPort}");
        Console.WriteLine("[UnityStub] press Ctrl+C to stop");

        try
        {
            if (options.TransportMode == TransportMode.NamedPipe)
            {
                await RunNamedPipeServerAsync(options, cts.Token);
            }
            else
            {
                await RunTcpServerAsync(options, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private static async Task RunNamedPipeServerAsync(StubOptions options, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                options.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Console.WriteLine("[UnityStub] waiting for named pipe client...");
            await server.WaitForConnectionAsync(cancellationToken);
            Console.WriteLine("[UnityStub] client connected.");
            await HandleConnectionAsync(server, cancellationToken);
            Console.WriteLine("[UnityStub] client disconnected.");
        }
    }

    private static async Task RunTcpServerAsync(StubOptions options, CancellationToken cancellationToken)
    {
        var listenerAddress = ResolveIpAddress(options.TcpHost);
        var listener = new TcpListener(listenerAddress, options.TcpPort);
        listener.Start();
        Console.WriteLine($"[UnityStub] tcp listener started at {listenerAddress}:{options.TcpPort}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using (client)
                {
                    Console.WriteLine("[UnityStub] tcp client connected.");
                    await HandleConnectionAsync(client.GetStream(), cancellationToken);
                    Console.WriteLine("[UnityStub] tcp client disconnected.");
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 16 * 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            Console.WriteLine($"[UnityStub] <= {line}");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = ReadString(root, "type");
            var correlationId = ReadString(root, "correlationId");
            var runId = ReadString(root, "runId");

            await SendAsync(
                writer,
                BuildAck(
                    correlationId,
                    action: string.IsNullOrWhiteSpace(type) ? "unknown" : type,
                    runId: runId,
                    detail: "accepted"));

            if (type.Equals("attach_viewport", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(
                    writer,
                    new Dictionary<string, object?>
                    {
                        ["type"] = "bridge_state",
                        ["payload"] = new Dictionary<string, object?>
                        {
                            ["attached"] = true,
                            ["message"] = "viewport ready",
                        },
                    });
                continue;
            }

            if (type.Equals("push_request", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(writer, BuildMapPointSelected(runId));
                continue;
            }

            if (type.Equals("push_result", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(writer, BuildProfileLineChanged(runId));
                continue;
            }

            if (type.Equals("set_active_layer", StringComparison.OrdinalIgnoreCase))
            {
                var payload = ReadPayload(root);
                var layerId = ReadString(payload, "layer_id");

                await SendAsync(writer, BuildLayerStateChanged(runId, layerId, "loading", 0, null));
                await Task.Delay(120, cancellationToken);
                await SendAsync(writer, BuildLayerStateChanged(runId, layerId, "loading", 45, null));
                await Task.Delay(120, cancellationToken);
                await SendAsync(writer, BuildLayerStateChanged(runId, layerId, "ready", 100, 180));
                continue;
            }

            if (type.Equals("set_camera_state", StringComparison.OrdinalIgnoreCase))
            {
                var payload = ReadPayload(root);
                await SendAsync(writer, BuildCameraStateChanged(payload));
                continue;
            }

            if (type.Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(writer, BuildDiagnosticSnapshot());
                continue;
            }
        }
    }

    private static async Task SendAsync(StreamWriter writer, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await writer.WriteLineAsync(json);
        Console.WriteLine($"[UnityStub] => {json}");
    }

    private static object BuildAck(string correlationId, string action, string runId, string detail)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "ack",
            ["correlation_id"] = correlationId,
            ["payload"] = new Dictionary<string, object?>
            {
                ["action"] = action,
                ["run_id"] = runId,
                ["detail"] = detail,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    private static object BuildMapPointSelected(string runId)
    {
        var seed = Math.Abs(HashCode.Combine("map_point_selected", runId));
        var x = 900 + seed % 1600;
        var y = 1200 + (seed / 3) % 1800;
        return new Dictionary<string, object?>
        {
            ["type"] = "map_point_selected",
            ["payload"] = new Dictionary<string, object?>
            {
                ["x"] = x,
                ["y"] = y,
                ["node_id"] = $"relay_{seed % 31:00}",
            },
        };
    }

    private static object BuildProfileLineChanged(string runId)
    {
        var seed = Math.Abs(HashCode.Combine("profile_line_changed", runId));
        var sx = 700 + seed % 900;
        var sy = 1000 + (seed / 5) % 900;
        var ex = sx + 900 + seed % 600;
        var ey = sy + 300 + (seed / 7) % 500;
        return new Dictionary<string, object?>
        {
            ["type"] = "profile_line_changed",
            ["payload"] = new Dictionary<string, object?>
            {
                ["start_x"] = sx,
                ["start_y"] = sy,
                ["end_x"] = ex,
                ["end_y"] = ey,
            },
        };
    }

    private static object BuildLayerStateChanged(
        string runId,
        string layerId,
        string state,
        double? progressPercent,
        double? transitionMs)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "layer_state_changed",
            ["payload"] = new Dictionary<string, object?>
            {
                ["run_id"] = runId,
                ["layer_id"] = string.IsNullOrWhiteSpace(layerId) ? "coverage_mean" : layerId,
                ["state"] = state,
                ["progress"] = progressPercent,
                ["transition_ms"] = transitionMs,
                ["message"] = "layer switched",
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    private static object BuildCameraStateChanged(JsonElement payload)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "camera_state_changed",
            ["payload"] = new Dictionary<string, object?>
            {
                ["x"] = ReadDouble(payload, "x", fallback: 1200),
                ["y"] = ReadDouble(payload, "y", fallback: 1800),
                ["z"] = ReadDouble(payload, "z", fallback: 900),
                ["pitch"] = ReadDouble(payload, "pitch", fallback: 20),
                ["yaw"] = ReadDouble(payload, "yaw", fallback: 45),
                ["roll"] = ReadDouble(payload, "roll", fallback: 0),
                ["fov"] = ReadDouble(payload, "fov", fallback: 55),
                ["message"] = "camera applied",
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    private static object BuildDiagnosticSnapshot()
    {
        var fps = 58 + Random.Shared.NextDouble() * 6;
        var frameTime = 1000 / Math.Max(1, fps) * 1.6;
        return new Dictionary<string, object?>
        {
            ["type"] = "diagnostic_snapshot",
            ["payload"] = new Dictionary<string, object?>
            {
                ["fps"] = fps,
                ["frame_time_p95_ms"] = frameTime,
                ["gpu_memory_mb"] = 650 + Random.Shared.NextDouble() * 80,
                ["layer_load_ms"] = 120 + Random.Shared.NextDouble() * 50,
                ["tile_cache_hit_rate"] = 0.8 + Random.Shared.NextDouble() * 0.15,
                ["message"] = "stub diagnostics",
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    private static JsonElement ReadPayload(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object)
        {
            return payload;
        }

        return root;
    }

    private static double ReadDouble(JsonElement element, string propertyName, double fallback = 0d)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
                return value;
            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), out var parsed))
                return parsed;
        }
        return fallback;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static IPAddress ResolveIpAddress(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.FirstOrDefault(static addr => addr.AddressFamily == AddressFamily.InterNetwork)
                   ?? IPAddress.Loopback;
        }
        catch
        {
            return IPAddress.Loopback;
        }
    }

    private sealed class StubOptions
    {
        public TransportMode TransportMode { get; init; } = TransportMode.NamedPipe;
        public string PipeName { get; init; } = "TrailMateCenter.Propagation.Bridge";
        public string TcpHost { get; init; } = "127.0.0.1";
        public int TcpPort { get; init; } = 51110;

        public static StubOptions FromEnvironment()
        {
            var modeRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE");
            var mode = string.Equals(modeRaw, "tcp", StringComparison.OrdinalIgnoreCase)
                ? TransportMode.Tcp
                : TransportMode.NamedPipe;

            var pipeName = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_PIPE_NAME");
            var host = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_TCP_HOST");
            var portRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_TCP_PORT");

            return new StubOptions
            {
                TransportMode = mode,
                PipeName = string.IsNullOrWhiteSpace(pipeName) ? "TrailMateCenter.Propagation.Bridge" : pipeName.Trim(),
                TcpHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(),
                TcpPort = int.TryParse(portRaw, out var port) ? port : 51110,
            };
        }
    }

    private enum TransportMode
    {
        NamedPipe = 0,
        Tcp = 1,
    }
}
