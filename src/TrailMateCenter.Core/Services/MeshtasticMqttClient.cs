using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Meshtastic.Protobufs;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public sealed class MeshtasticMqttClient : IAsyncDisposable
{
    private static readonly byte[] DefaultLongFastPsk =
    [
        0xD4, 0xF1, 0xBB, 0x3A, 0x20, 0x29, 0x07, 0x59,
        0xF0, 0xBC, 0xFF, 0xAB, 0xCF, 0x4E, 0x69, 0x01,
    ];

    private readonly ILogger<MeshtasticMqttClient> _logger;
    private readonly SessionStore _sessionStore;
    private readonly SqliteStore _sqliteStore;
    private readonly AppDataDecoder _decoder = new();
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(3);
    private readonly TimeSpan _connectRetryDelay = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _trafficLogInterval = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _packetDedupWindow = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _packetDedupCleanupInterval = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _reconfigureGate = new(1, 1);
    private readonly object _runtimeGate = new();

    private MeshtasticMqttSettings _settings = MeshtasticMqttSettings.CreateDefault();
    private List<MqttConnectionRuntime> _runtimes = new();
    private volatile bool _started;

    public MeshtasticMqttClient(
        ILogger<MeshtasticMqttClient> logger,
        SessionStore sessionStore,
        SqliteStore sqliteStore)
    {
        _logger = logger;
        _sessionStore = sessionStore;
        _sqliteStore = sqliteStore;
    }

    public event EventHandler<MessageEntry>? MessageReceived;
    public event EventHandler<PositionUpdate>? PositionReceived;
    public event EventHandler<NodeInfoUpdate>? NodeInfoReceived;
    public event EventHandler<TacticalEvent>? TacticalEventReceived;

    private enum PayloadProcessOutcome
    {
        Unhandled = 0,
        Decoded = 1,
        Duplicate = 2,
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _ = ReconfigureAsync();
    }

    public void ApplySettings(MeshtasticMqttSettings? settings)
    {
        _settings = NormalizeSettings(settings);
        if (_started)
        {
            _ = ReconfigureAsync();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return;

        _started = false;

        await _reconfigureGate.WaitAsync(cancellationToken);
        try
        {
            await StopAllRuntimesAsync();
        }
        finally
        {
            _reconfigureGate.Release();
        }
    }

    private async Task ReconfigureAsync()
    {
        await _reconfigureGate.WaitAsync();
        try
        {
            await StopAllRuntimesAsync();

            if (!_started)
                return;

            var enabledSources = _settings.Sources
                .Select(NormalizeSource)
                .Where(IsSourceUsable)
                .ToList();

            if (enabledSources.Count == 0)
            {
                _logger.LogInformation("Meshtastic MQTT not started: no enabled source configured");
                return;
            }

            foreach (var source in enabledSources)
            {
                var runtime = CreateRuntime(source);
                lock (_runtimeGate)
                {
                    _runtimes.Add(runtime);
                }
                _ = ConnectWithRetryAsync(runtime, runtime.Cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reconfigure Meshtastic MQTT sources");
        }
        finally
        {
            _reconfigureGate.Release();
        }
    }

    private async Task StopAllRuntimesAsync()
    {
        List<MqttConnectionRuntime> runtimes;
        lock (_runtimeGate)
        {
            runtimes = _runtimes;
            _runtimes = new List<MqttConnectionRuntime>();
        }

        foreach (var runtime in runtimes)
        {
            await StopRuntimeAsync(runtime);
        }
    }

    private async Task StopRuntimeAsync(MqttConnectionRuntime runtime)
    {
        TryLogTraffic(runtime, force: true);
        runtime.Cts.Cancel();

        try
        {
            if (runtime.Client.IsConnected)
            {
                try
                {
                    await runtime.Client.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to disconnect Meshtastic MQTT source '{Source}' cleanly", runtime.SourceName);
                }
            }
        }
        finally
        {
            runtime.Client.ConnectedAsync -= runtime.ConnectedAsyncHandler;
            runtime.Client.DisconnectedAsync -= runtime.DisconnectedAsyncHandler;
            runtime.Client.ApplicationMessageReceivedAsync -= runtime.MessageAsyncHandler;
            runtime.Client.Dispose();
            runtime.ConnectGate.Dispose();
            runtime.Cts.Dispose();
        }
    }

    private MqttConnectionRuntime CreateRuntime(MeshtasticMqttSourceSettings source)
    {
        var factory = new MqttFactory();
        var client = factory.CreateMqttClient();
        var clientId = BuildClientId(source);

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(source.Host, source.Port)
            .WithClientId(clientId)
            .WithCleanSession(source.CleanSession);

        if (!string.IsNullOrWhiteSpace(source.Username))
        {
            optionsBuilder.WithCredentials(source.Username, source.Password);
        }

        if (source.UseTls)
        {
            optionsBuilder.WithTlsOptions(tls => tls.UseTls(true));
        }

        var runtime = new MqttConnectionRuntime
        {
            SourceId = source.Id,
            SourceName = GetSourceName(source),
            Topic = source.Topic,
            ClientId = clientId,
            CleanSession = source.CleanSession,
            SubscribeQos = Math.Clamp(source.SubscribeQos, 0, 2),
            Client = client,
            Options = optionsBuilder.Build(),
            Cts = new CancellationTokenSource(),
            ConnectGate = new SemaphoreSlim(1, 1),
        };

        runtime.ConnectedAsyncHandler = args => OnConnectedAsync(runtime, args);
        runtime.DisconnectedAsyncHandler = args => OnDisconnectedAsync(runtime, args);
        runtime.MessageAsyncHandler = args => OnApplicationMessageReceivedAsync(runtime, args);

        client.ConnectedAsync += runtime.ConnectedAsyncHandler;
        client.DisconnectedAsync += runtime.DisconnectedAsyncHandler;
        client.ApplicationMessageReceivedAsync += runtime.MessageAsyncHandler;

        return runtime;
    }

    private async Task ConnectWithRetryAsync(MqttConnectionRuntime runtime, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(runtime, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Meshtastic MQTT source '{Source}' connect failed, retrying in {DelaySeconds}s",
                    runtime.SourceName,
                    _connectRetryDelay.TotalSeconds);

                try
                {
                    await Task.Delay(_connectRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task EnsureConnectedAsync(MqttConnectionRuntime runtime, CancellationToken cancellationToken)
    {
        if (runtime.Client.IsConnected)
            return;

        await runtime.ConnectGate.WaitAsync(cancellationToken);
        try
        {
            if (runtime.Client.IsConnected)
                return;
            await runtime.Client.ConnectAsync(runtime.Options, cancellationToken);
        }
        finally
        {
            runtime.ConnectGate.Release();
        }
    }

    private async Task OnConnectedAsync(MqttConnectionRuntime runtime, MqttClientConnectedEventArgs args)
    {
        ResetTrafficStats(runtime);

        var topics = SplitTopics(runtime.Topic).ToList();
        if (topics.Count == 0)
            return;

        var qos = ToMqttQos(runtime.SubscribeQos);
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics)
        {
            subscribeOptions.WithTopicFilter(f =>
            {
                f.WithTopic(topic);
                f.WithQualityOfServiceLevel(qos);
            });
        }

        await runtime.Client.SubscribeAsync(subscribeOptions.Build());
        _logger.LogInformation(
            "Meshtastic MQTT source '{Source}' connected (clientId={ClientId}, cleanSession={CleanSession}) and subscribed to {Topics} with qos={Qos}",
            runtime.SourceName,
            runtime.ClientId,
            runtime.CleanSession,
            string.Join(", ", topics),
            runtime.SubscribeQos);
    }

    private async Task OnDisconnectedAsync(MqttConnectionRuntime runtime, MqttClientDisconnectedEventArgs args)
    {
        if (runtime.Cts.IsCancellationRequested)
            return;

        TryLogTraffic(runtime, force: true);
        _logger.LogWarning(
            "Meshtastic MQTT source '{Source}' disconnected. Reason={Reason}",
            runtime.SourceName,
            args.ReasonString ?? args.Reason.ToString());

        try
        {
            await Task.Delay(_reconnectDelay, runtime.Cts.Token);
            await EnsureConnectedAsync(runtime, runtime.Cts.Token);
        }
        catch (OperationCanceledException) when (runtime.Cts.IsCancellationRequested)
        {
            // Ignore during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meshtastic MQTT source '{Source}' reconnect attempt failed", runtime.SourceName);
            _ = ConnectWithRetryAsync(runtime, runtime.Cts.Token);
        }
    }

    private Task OnApplicationMessageReceivedAsync(MqttConnectionRuntime runtime, MqttApplicationMessageReceivedEventArgs args)
    {
        var payloadSegment = args.ApplicationMessage.PayloadSegment;
        if (payloadSegment.Count == 0)
            return Task.CompletedTask;

        var payload = payloadSegment.AsSpan().ToArray();
        var topic = args.ApplicationMessage.Topic ?? string.Empty;
        runtime.LastTopic = topic;
        var received = Interlocked.Increment(ref runtime.ReceivedCount);
        if (received == 1)
        {
            _logger.LogInformation(
                "Meshtastic MQTT source '{Source}' received first payload on topic '{Topic}'",
                runtime.SourceName,
                topic);
        }

        _ = PersistRawPacketAsync(runtime, args.ApplicationMessage, topic, payload);

        try
        {
            var outcome = ProcessPayload(runtime, topic, payload);
            switch (outcome)
            {
                case PayloadProcessOutcome.Decoded:
                    Interlocked.Increment(ref runtime.DecodedCount);
                    break;
                case PayloadProcessOutcome.Duplicate:
                    Interlocked.Increment(ref runtime.DuplicateCount);
                    break;
                default:
                    Interlocked.Increment(ref runtime.UndecodedCount);
                    if (IsEncryptedTopic(topic))
                    {
                        Interlocked.Increment(ref runtime.EncryptedTopicCount);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref runtime.DecodeErrorCount);
            _logger.LogDebug(ex, "Failed to process Meshtastic MQTT payload on source '{Source}' topic '{Topic}'", runtime.SourceName, topic);
        }
        finally
        {
            TryLogTraffic(runtime);
        }

        return Task.CompletedTask;
    }

    private PayloadProcessOutcome ProcessPayload(MqttConnectionRuntime runtime, string topic, ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            return PayloadProcessOutcome.Unhandled;

        if (TryProcessJsonPayload(runtime, topic, payload))
            return PayloadProcessOutcome.Decoded;

        var serviceEnvelopeOutcome = TryProcessServiceEnvelopePayload(runtime, topic, payload);
        if (serviceEnvelopeOutcome != PayloadProcessOutcome.Unhandled)
            return serviceEnvelopeOutcome;

        var meshPacketOutcome = TryProcessMeshPacketPayload(runtime, topic, payload);
        if (meshPacketOutcome != PayloadProcessOutcome.Unhandled)
            return meshPacketOutcome;

        var proxyOutcome = TryProcessProxyPayload(runtime, topic, payload);
        if (proxyOutcome != PayloadProcessOutcome.Unhandled)
            return proxyOutcome;

        return PayloadProcessOutcome.Unhandled;
    }

    private PayloadProcessOutcome TryProcessServiceEnvelopePayload(MqttConnectionRuntime runtime, string topic, ReadOnlySpan<byte> payload)
    {
        ServiceEnvelope envelope;
        try
        {
            envelope = ServiceEnvelope.Parser.ParseFrom(payload.ToArray());
        }
        catch (InvalidProtocolBufferException)
        {
            return PayloadProcessOutcome.Unhandled;
        }

        if (envelope.Packet is null)
            return PayloadProcessOutcome.Unhandled;

        var channelId = string.IsNullOrWhiteSpace(envelope.ChannelId)
            ? TryExtractTopicChannelId(topic)
            : envelope.ChannelId.Trim();

        return ProcessMeshPacket(runtime, topic, envelope.Packet, channelId);
    }

    private PayloadProcessOutcome TryProcessMeshPacketPayload(MqttConnectionRuntime runtime, string topic, ReadOnlySpan<byte> payload)
    {
        MeshPacket packet;
        try
        {
            packet = MeshPacket.Parser.ParseFrom(payload.ToArray());
        }
        catch (InvalidProtocolBufferException)
        {
            return PayloadProcessOutcome.Unhandled;
        }

        return ProcessMeshPacket(runtime, topic, packet, TryExtractTopicChannelId(topic));
    }

    private PayloadProcessOutcome TryProcessProxyPayload(MqttConnectionRuntime runtime, string topic, ReadOnlySpan<byte> payload)
    {
        MqttClientProxyMessage proxy;
        try
        {
            proxy = MqttClientProxyMessage.Parser.ParseFrom(payload.ToArray());
        }
        catch (InvalidProtocolBufferException)
        {
            return PayloadProcessOutcome.Unhandled;
        }

        switch (proxy.PayloadVariantCase)
        {
            case MqttClientProxyMessage.PayloadVariantOneofCase.Data:
            {
                var data = proxy.Data;
                if (data is null || data.Length == 0)
                    return PayloadProcessOutcome.Decoded;
                return ProcessPayload(runtime, proxy.Topic, data.Span);
            }
            case MqttClientProxyMessage.PayloadVariantOneofCase.Text:
            {
                if (!string.IsNullOrWhiteSpace(proxy.Text))
                {
                    _logger.LogDebug("Meshtastic MQTT proxy text on {Topic}: {Text}", proxy.Topic, proxy.Text);
                }
                return PayloadProcessOutcome.Decoded;
            }
            default:
                return PayloadProcessOutcome.Unhandled;
        }
    }

    private bool TryProcessJsonPayload(MqttConnectionRuntime runtime, string topic, ReadOnlySpan<byte> payload)
    {
        if (!LooksLikeJson(payload))
            return false;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload.ToArray());
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var root = doc.RootElement;
            if (root.TryGetProperty("packet", out var packetElement) && packetElement.ValueKind == JsonValueKind.Object)
            {
                return TryProcessJsonPacket(runtime, topic, packetElement);
            }

            return TryProcessJsonPacket(runtime, topic, root);
        }
    }

    private bool TryProcessJsonPacket(MqttConnectionRuntime runtime, string topic, JsonElement root)
    {
        if (!root.TryGetProperty("decoded", out var decodedElement) || decodedElement.ValueKind != JsonValueKind.Object)
            return false;

        var from = ReadUInt32(root, "from", "source", "fromId");
        var to = ReadUInt32(root, "to", "dest", "toId");
        var channel = (byte)Math.Clamp(ReadInt(root, "channel", "channelId"), 0, 255);

        var port = ReadPortNum(decodedElement, "portnum", "port");
        if (port == PortNum.UnknownApp && decodedElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            port = PortNum.TextMessageApp;
        }

        byte[] payloadBytes = Array.Empty<byte>();
        if (decodedElement.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.String)
        {
            var payloadText = payloadElement.GetString();
            if (!string.IsNullOrWhiteSpace(payloadText))
            {
                try
                {
                    payloadBytes = Convert.FromBase64String(payloadText);
                }
                catch (FormatException)
                {
                    payloadBytes = Encoding.UTF8.GetBytes(payloadText);
                }
            }
        }

        if (decodedElement.TryGetProperty("text", out var decodedTextElement) && decodedTextElement.ValueKind == JsonValueKind.String)
        {
            var text = decodedTextElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                EmitTextMessage(runtime, from, to, channel, text, null);
                return true;
            }
        }

        if (payloadBytes.Length == 0)
            return false;

        ProcessDecodedPayload(runtime, topic, from, to, channel, port, payloadBytes, null);
        return true;
    }

    private PayloadProcessOutcome ProcessMeshPacket(MqttConnectionRuntime runtime, string topic, MeshPacket packet, string? channelId)
    {
        if (IsDuplicateMeshPacket(runtime, packet))
            return PayloadProcessOutcome.Duplicate;

        var channel = (byte)Math.Clamp((int)packet.Channel, 0, 255);

        if (packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Decoded && packet.Decoded is not null)
        {
            var decoded = packet.Decoded;
            var from = packet.From != 0 ? packet.From : decoded.Source;
            var to = packet.To != 0 ? packet.To : decoded.Dest;
            var payloadBytes = decoded.Payload?.ToByteArray() ?? Array.Empty<byte>();
            ProcessDecodedPayload(runtime, topic, from, to, channel, decoded.Portnum, payloadBytes, packet);
            return PayloadProcessOutcome.Decoded;
        }

        if (packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Encrypted &&
            TryDecryptMeshPacketData(packet, channelId, out var decrypted))
        {
            var from = packet.From != 0 ? packet.From : decrypted.Source;
            var to = packet.To != 0 ? packet.To : decrypted.Dest;
            var payloadBytes = decrypted.Payload?.ToByteArray() ?? Array.Empty<byte>();
            ProcessDecodedPayload(runtime, topic, from, to, channel, decrypted.Portnum, payloadBytes, packet);
            return PayloadProcessOutcome.Decoded;
        }

        return PayloadProcessOutcome.Unhandled;
    }

    private bool IsDuplicateMeshPacket(MqttConnectionRuntime runtime, MeshPacket packet)
    {
        if (!TryCreatePacketKey(packet, out var packetKey))
            return false;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dedupWindowMs = (long)_packetDedupWindow.TotalMilliseconds;
        var cleanupIntervalMs = (long)_packetDedupCleanupInterval.TotalMilliseconds;

        lock (runtime.PacketDedupGate)
        {
            if (runtime.PacketDedupCache.TryGetValue(packetKey, out var lastSeenMs) &&
                nowMs - lastSeenMs <= dedupWindowMs)
            {
                runtime.PacketDedupCache[packetKey] = nowMs;
                return true;
            }

            runtime.PacketDedupCache[packetKey] = nowMs;

            if (nowMs - runtime.LastPacketDedupCleanupUnixMs >= cleanupIntervalMs)
            {
                runtime.LastPacketDedupCleanupUnixMs = nowMs;
                var expireBeforeMs = nowMs - dedupWindowMs;
                var staleKeys = new List<ulong>();
                foreach (var item in runtime.PacketDedupCache)
                {
                    if (item.Value < expireBeforeMs)
                    {
                        staleKeys.Add(item.Key);
                    }
                }

                foreach (var staleKey in staleKeys)
                {
                    runtime.PacketDedupCache.Remove(staleKey);
                }
            }
        }

        return false;
    }

    private static bool TryCreatePacketKey(MeshPacket packet, out ulong packetKey)
    {
        packetKey = 0;
        if (packet.Id == 0)
            return false;

        var from = packet.From;
        if (from == 0 &&
            packet.PayloadVariantCase == MeshPacket.PayloadVariantOneofCase.Decoded &&
            packet.Decoded is not null &&
            packet.Decoded.Source != 0)
        {
            from = packet.Decoded.Source;
        }

        if (from == 0)
            return false;

        packetKey = ((ulong)from << 32) | packet.Id;
        return true;
    }

    private bool TryDecryptMeshPacketData(MeshPacket packet, string? channelId, out Data decodedData)
    {
        decodedData = new Data();
        if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Encrypted || packet.Encrypted is null)
            return false;

        var cipher = packet.Encrypted.Span;
        if (cipher.Length == 0)
            return false;

        foreach (var key in ResolveCandidateKeys(channelId))
        {
            if (TryDecryptMeshPacketData(cipher, packet.From, packet.Id, key, incrementBigEndian: true, out decodedData))
                return true;
            if (TryDecryptMeshPacketData(cipher, packet.From, packet.Id, key, incrementBigEndian: false, out decodedData))
                return true;
        }

        return false;
    }

    private static bool TryDecryptMeshPacketData(
        ReadOnlySpan<byte> cipher,
        uint from,
        uint packetId,
        ReadOnlySpan<byte> key,
        bool incrementBigEndian,
        out Data decodedData)
    {
        decodedData = new Data();
        byte[] plaintext;
        try
        {
            plaintext = DecryptAesCtr(cipher, key, from, packetId, incrementBigEndian);
        }
        catch (CryptographicException)
        {
            return false;
        }

        Data candidate;
        try
        {
            candidate = Data.Parser.ParseFrom(plaintext);
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }

        if (!IsLikelyDecodedData(candidate, from))
            return false;

        decodedData = candidate;
        return true;
    }

    private static bool IsLikelyDecodedData(Data data, uint packetFrom)
    {
        var port = (int)data.Portnum;
        if (port < 0 || port > (int)PortNum.Max)
            return false;
        if (data.Source != 0 && packetFrom != 0 && data.Source != packetFrom)
            return false;

        if (data.Payload.Length == 0 &&
            data.Portnum is not PortNum.TextMessageApp and not PortNum.AlertApp &&
            data.Emoji == 0)
        {
            return false;
        }

        return true;
    }

    private static byte[] DecryptAesCtr(
        ReadOnlySpan<byte> cipher,
        ReadOnlySpan<byte> key,
        uint from,
        uint packetId,
        bool incrementBigEndian)
    {
        var nonce = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(0, 8), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(8, 4), from);

        var plaintext = new byte[cipher.Length];
        var counterBlock = new byte[16];
        Buffer.BlockCopy(nonce, 0, counterBlock, 0, nonce.Length);
        var keystream = new byte[16];

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();

        using var encryptor = aes.CreateEncryptor();

        for (var offset = 0; offset < cipher.Length; offset += 16)
        {
            encryptor.TransformBlock(counterBlock, 0, 16, keystream, 0);
            var len = Math.Min(16, cipher.Length - offset);
            for (var i = 0; i < len; i++)
            {
                plaintext[offset + i] = (byte)(cipher[offset + i] ^ keystream[i]);
            }

            IncrementCounter(counterBlock.AsSpan(12, 4), incrementBigEndian);
        }

        return plaintext;
    }

    private static void IncrementCounter(Span<byte> counter, bool bigEndian)
    {
        if (bigEndian)
        {
            for (var i = counter.Length - 1; i >= 0; i--)
            {
                counter[i]++;
                if (counter[i] != 0)
                    return;
            }
            return;
        }

        for (var i = 0; i < counter.Length; i++)
        {
            counter[i]++;
            if (counter[i] != 0)
                return;
        }
    }

    private static IEnumerable<byte[]> ResolveCandidateKeys(string? channelId)
    {
        if (!string.IsNullOrWhiteSpace(channelId) &&
            channelId.Contains("LongFast", StringComparison.OrdinalIgnoreCase))
        {
            yield return DefaultLongFastPsk;
            yield break;
        }

        // Fallback: still try default public key for encrypted public channels.
        yield return DefaultLongFastPsk;
    }

    private static string? TryExtractTopicChannelId(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return null;

        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return null;
        if (!string.Equals(parts[0], "msh", StringComparison.OrdinalIgnoreCase))
            return null;

        // Typical format: msh/{region}/2/e/{channel}/{gateway}
        if (string.Equals(parts[3], "e", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parts[3], "c", StringComparison.OrdinalIgnoreCase))
        {
            return parts[4];
        }

        return null;
    }

    private void ProcessDecodedPayload(
        MqttConnectionRuntime runtime,
        string topic,
        uint from,
        uint to,
        byte channel,
        PortNum portnum,
        byte[] payloadBytes,
        MeshPacket? packet)
    {
        if (payloadBytes.Length == 0 && portnum != PortNum.TextMessageApp && portnum != PortNum.AlertApp)
            return;

        if (portnum is PortNum.TextMessageApp or PortNum.AlertApp)
        {
            var text = DecodeUtf8(payloadBytes);
            if (!string.IsNullOrWhiteSpace(text))
            {
                EmitTextMessage(runtime, from, to, channel, text, packet);
            }
            return;
        }

        var appPacket = new AppDataPacket(
            (uint)portnum,
            from,
            to,
            channel,
            (HostLinkAppDataFlags)0,
            Array.Empty<byte>(),
            0,
            0,
            payloadBytes);

        var decoded = _decoder.Decode(appPacket);
        var nodeInfoCount = 0;
        foreach (var info in decoded.NodeInfos)
        {
            _sessionStore.AddOrUpdateNodeInfo(info);
            NodeInfoReceived?.Invoke(this, info);
            nodeInfoCount++;
        }
        if (nodeInfoCount > 0)
            Interlocked.Add(ref runtime.NodeInfoCount, nodeInfoCount);

        var positionCount = 0;
        foreach (var pos in decoded.Positions)
        {
            _sessionStore.AddPositionUpdate(pos);
            PositionReceived?.Invoke(this, pos);
            positionCount++;
        }
        if (positionCount > 0)
            Interlocked.Add(ref runtime.PositionCount, positionCount);

        var tacticalCount = 0;
        foreach (var ev in decoded.TacticalEvents)
        {
            _sessionStore.AddTacticalEvent(ev);
            TacticalEventReceived?.Invoke(this, ev);
            tacticalCount++;
        }
        if (tacticalCount > 0)
            Interlocked.Add(ref runtime.TacticalEventCount, tacticalCount);

        _logger.LogDebug(
            "Meshtastic MQTT decoded packet on {Topic}: port={Port} from=0x{From:X8} to=0x{To:X8} payload={Length}",
            topic,
            (int)portnum,
            from,
            to,
            payloadBytes.Length);
    }

    private void EmitTextMessage(MqttConnectionRuntime runtime, uint from, uint to, byte channel, string text, MeshPacket? packet)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var isBroadcast = to == 0 || to == uint.MaxValue;
        var hopCount = packet is not null && packet.HopStart >= packet.HopLimit
            ? (int?)(packet.HopStart - packet.HopLimit)
            : null;
        var direct = hopCount is 0;

        var timestamp = packet is not null && packet.RxTime != 0
            ? DateTimeOffset.FromUnixTimeSeconds(packet.RxTime)
            : DateTimeOffset.UtcNow;

        var message = new MessageEntry
        {
            Direction = MessageDirection.Incoming,
            FromId = from == 0 ? null : from,
            ToId = isBroadcast ? null : to,
            From = from == 0 ? "MQTT" : $"0x{from:X8}",
            To = isBroadcast ? "broadcast" : $"0x{to:X8}",
            ChannelId = channel,
            Channel = channel.ToString(),
            Text = text,
            Status = MessageDeliveryStatus.Succeeded,
            Timestamp = DateTimeOffset.UtcNow,
            DeviceTimestamp = timestamp,
            Rssi = packet?.RxRssi == 0 ? null : packet?.RxRssi,
            Snr = packet is null ? null : (int?)Math.Round(packet.RxSnr),
            Hop = hopCount,
            Direct = direct,
            Origin = RxOrigin.External,
            FromIs = true,
        };

        _sessionStore.AddMessage(message);
        MessageReceived?.Invoke(this, message);
        Interlocked.Increment(ref runtime.TextMessageCount);
    }

    private void ResetTrafficStats(MqttConnectionRuntime runtime)
    {
        Interlocked.Exchange(ref runtime.ReceivedCount, 0);
        Interlocked.Exchange(ref runtime.DecodedCount, 0);
        Interlocked.Exchange(ref runtime.DuplicateCount, 0);
        Interlocked.Exchange(ref runtime.UndecodedCount, 0);
        Interlocked.Exchange(ref runtime.EncryptedTopicCount, 0);
        Interlocked.Exchange(ref runtime.PositionCount, 0);
        Interlocked.Exchange(ref runtime.NodeInfoCount, 0);
        Interlocked.Exchange(ref runtime.TextMessageCount, 0);
        Interlocked.Exchange(ref runtime.TacticalEventCount, 0);
        Interlocked.Exchange(ref runtime.DecodeErrorCount, 0);

        Interlocked.Exchange(ref runtime.LastReportedReceivedCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedDecodedCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedDuplicateCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedUndecodedCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedEncryptedTopicCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedPositionCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedNodeInfoCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedTextMessageCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedTacticalEventCount, 0);
        Interlocked.Exchange(ref runtime.LastReportedDecodeErrorCount, 0);

        Interlocked.Exchange(ref runtime.LastTrafficLogUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref runtime.LastPacketDedupCleanupUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        lock (runtime.PacketDedupGate)
        {
            runtime.PacketDedupCache.Clear();
        }
        runtime.LastTopic = string.Empty;
    }

    private void TryLogTraffic(MqttConnectionRuntime runtime, bool force = false)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!force)
        {
            var intervalMs = (long)_trafficLogInterval.TotalMilliseconds;
            while (true)
            {
                var last = Interlocked.Read(ref runtime.LastTrafficLogUnixMs);
                if (nowMs - last < intervalMs)
                    return;
                if (Interlocked.CompareExchange(ref runtime.LastTrafficLogUnixMs, nowMs, last) == last)
                    break;
            }
        }
        else
        {
            Interlocked.Exchange(ref runtime.LastTrafficLogUnixMs, nowMs);
        }

        var received = Interlocked.Read(ref runtime.ReceivedCount);
        var decoded = Interlocked.Read(ref runtime.DecodedCount);
        var duplicate = Interlocked.Read(ref runtime.DuplicateCount);
        var undecoded = Interlocked.Read(ref runtime.UndecodedCount);
        var encryptedTopic = Interlocked.Read(ref runtime.EncryptedTopicCount);
        var positions = Interlocked.Read(ref runtime.PositionCount);
        var nodeInfos = Interlocked.Read(ref runtime.NodeInfoCount);
        var textMessages = Interlocked.Read(ref runtime.TextMessageCount);
        var tactical = Interlocked.Read(ref runtime.TacticalEventCount);
        var errors = Interlocked.Read(ref runtime.DecodeErrorCount);

        if (received == 0 && decoded == 0 && duplicate == 0 && undecoded == 0 && positions == 0 && nodeInfos == 0 && textMessages == 0 && tactical == 0 && errors == 0)
            return;

        var lastReceived = Interlocked.Exchange(ref runtime.LastReportedReceivedCount, received);
        var lastDecoded = Interlocked.Exchange(ref runtime.LastReportedDecodedCount, decoded);
        var lastDuplicate = Interlocked.Exchange(ref runtime.LastReportedDuplicateCount, duplicate);
        var lastUndecoded = Interlocked.Exchange(ref runtime.LastReportedUndecodedCount, undecoded);
        var lastEncryptedTopic = Interlocked.Exchange(ref runtime.LastReportedEncryptedTopicCount, encryptedTopic);
        var lastPositions = Interlocked.Exchange(ref runtime.LastReportedPositionCount, positions);
        var lastNodeInfos = Interlocked.Exchange(ref runtime.LastReportedNodeInfoCount, nodeInfos);
        var lastTextMessages = Interlocked.Exchange(ref runtime.LastReportedTextMessageCount, textMessages);
        var lastTactical = Interlocked.Exchange(ref runtime.LastReportedTacticalEventCount, tactical);
        var lastErrors = Interlocked.Exchange(ref runtime.LastReportedDecodeErrorCount, errors);

        var deltaReceived = received - lastReceived;
        var deltaDecoded = decoded - lastDecoded;
        var deltaDuplicate = duplicate - lastDuplicate;
        var deltaUndecoded = undecoded - lastUndecoded;
        var deltaEncryptedTopic = encryptedTopic - lastEncryptedTopic;
        var deltaPositions = positions - lastPositions;
        var deltaNodeInfos = nodeInfos - lastNodeInfos;
        var deltaTextMessages = textMessages - lastTextMessages;
        var deltaTactical = tactical - lastTactical;
        var deltaErrors = errors - lastErrors;

        if (!force && deltaReceived <= 0 && deltaErrors <= 0)
            return;

        var topic = string.IsNullOrWhiteSpace(runtime.LastTopic) ? "(none)" : runtime.LastTopic;
        _logger.LogInformation(
            "Meshtastic MQTT source '{Source}' traffic: recv+{DeltaReceived} (total {Received}), decoded+{DeltaDecoded}, duplicate+{DeltaDuplicate} (total {Duplicate}), undecoded+{DeltaUndecoded}, encryptedTopic+{DeltaEncryptedTopic}, positions+{DeltaPositions}, nodeInfos+{DeltaNodeInfos}, text+{DeltaTextMessages}, tactical+{DeltaTactical}, errors+{DeltaErrors}, lastTopic={Topic}",
            runtime.SourceName,
            deltaReceived,
            received,
            deltaDecoded,
            deltaDuplicate,
            duplicate,
            deltaUndecoded,
            deltaEncryptedTopic,
            deltaPositions,
            deltaNodeInfos,
            deltaTextMessages,
            deltaTactical,
            deltaErrors,
            topic);
    }

    private static bool IsEncryptedTopic(string topic)
    {
        return topic.Contains("/e/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PersistRawPacketAsync(
        MqttConnectionRuntime runtime,
        MqttApplicationMessage message,
        string topic,
        byte[] payload)
    {
        try
        {
            var sourceId = string.IsNullOrWhiteSpace(runtime.SourceId) ? runtime.SourceName : runtime.SourceId;
            var packet = new MqttPacketRecord(
                DateTimeOffset.UtcNow,
                sourceId,
                runtime.SourceName,
                topic,
                (int)message.QualityOfServiceLevel,
                message.Retain,
                payload,
                Convert.ToHexString(SHA256.HashData(payload)));

            await _sqliteStore.AddMqttPacketAsync(packet, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to persist Meshtastic MQTT packet from source '{Source}' topic '{Topic}'",
                runtime.SourceName,
                topic);
        }
    }

    private static MqttQualityOfServiceLevel ToMqttQos(int qos)
    {
        return Math.Clamp(qos, 0, 2) switch
        {
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce,
        };
    }

    private static string BuildClientId(MeshtasticMqttSourceSettings source)
    {
        if (!string.IsNullOrWhiteSpace(source.ClientId))
            return source.ClientId.Trim();

        var seed = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id.Trim();
        var compact = new string(seed.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length == 0)
            compact = Guid.NewGuid().ToString("N");
        var suffix = compact.Length > 18 ? compact[..18] : compact;
        return $"tmc-{suffix}";
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> payload)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            var ch = payload[i];
            if (ch is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                continue;
            return ch == (byte)'{';
        }
        return false;
    }

    private static string DecodeUtf8(byte[] payloadBytes)
    {
        if (payloadBytes.Length == 0)
            return string.Empty;
        return Encoding.UTF8.GetString(payloadBytes).Trim('\0', '\r', '\n');
    }

    private static uint ReadUInt32(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;
            if (TryReadUInt32(value, out var result))
                return result;
        }
        return 0;
    }

    private static int ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;
        }
        return 0;
    }

    private static PortNum ReadPortNum(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return (PortNum)number;

            if (value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (int.TryParse(text, out number))
                return (PortNum)number;

            var compact = text.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
            if (compact.Contains("TEXTMESSAGE", StringComparison.OrdinalIgnoreCase))
                return PortNum.TextMessageApp;
            if (compact.Contains("POSITION", StringComparison.OrdinalIgnoreCase))
                return PortNum.PositionApp;
            if (compact.Contains("NODEINFO", StringComparison.OrdinalIgnoreCase))
                return PortNum.NodeinfoApp;
            if (compact.Contains("TELEMETRY", StringComparison.OrdinalIgnoreCase))
                return PortNum.TelemetryApp;
            if (compact.Contains("WAYPOINT", StringComparison.OrdinalIgnoreCase))
                return PortNum.WaypointApp;
            if (compact.Contains("MAPREPORT", StringComparison.OrdinalIgnoreCase))
                return PortNum.MapReportApp;
        }

        return PortNum.UnknownApp;
    }

    private static bool TryReadUInt32(JsonElement value, out uint result)
    {
        result = 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out result))
            return true;

        if (value.ValueKind != JsonValueKind.String)
            return false;

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out result);
        }

        return uint.TryParse(text, out result);
    }

    private static IEnumerable<string> SplitTopics(string topicText)
    {
        return topicText
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));
    }

    private static bool IsSourceUsable(MeshtasticMqttSourceSettings source)
    {
        if (!source.Enabled)
            return false;
        if (string.IsNullOrWhiteSpace(source.Host))
            return false;
        if (string.IsNullOrWhiteSpace(source.Topic))
            return false;
        return source.Port is > 0 and <= 65535;
    }

    private static MeshtasticMqttSettings NormalizeSettings(MeshtasticMqttSettings? settings)
    {
        if (settings is null)
            return MeshtasticMqttSettings.CreateDefault();

        var sources = (settings.Sources ?? new List<MeshtasticMqttSourceSettings>())
            .Select(NormalizeSource)
            .ToList();

        if (sources.Count == 0)
            sources.Add(MeshtasticMqttSourceSettings.CreateDefault());

        return settings with
        {
            Sources = sources,
        };
    }

    private static MeshtasticMqttSourceSettings NormalizeSource(MeshtasticMqttSourceSettings source)
    {
        var normalizedId = string.IsNullOrWhiteSpace(source.Id)
            ? Guid.NewGuid().ToString("N")
            : source.Id.Trim();

        var defaultPort = source.UseTls ? 8883 : 1883;
        return source with
        {
            Id = normalizedId,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Meshtastic MQTT" : source.Name.Trim(),
            Host = source.Host?.Trim() ?? string.Empty,
            Port = source.Port is > 0 and <= 65535 ? source.Port : defaultPort,
            Username = source.Username?.Trim() ?? string.Empty,
            Password = source.Password ?? string.Empty,
            Topic = source.Topic?.Trim() ?? string.Empty,
            ClientId = source.ClientId?.Trim() ?? string.Empty,
            CleanSession = source.CleanSession,
            SubscribeQos = Math.Clamp(source.SubscribeQos, 0, 2),
        };
    }

    private static string GetSourceName(MeshtasticMqttSourceSettings source)
    {
        if (!string.IsNullOrWhiteSpace(source.Name))
            return source.Name.Trim();
        return $"{source.Host}:{source.Port}";
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _reconfigureGate.Dispose();
    }

    private sealed class MqttConnectionRuntime
    {
        public required string SourceId { get; init; }
        public required string SourceName { get; init; }
        public required string Topic { get; init; }
        public required string ClientId { get; init; }
        public required bool CleanSession { get; init; }
        public required int SubscribeQos { get; init; }
        public required IMqttClient Client { get; init; }
        public required MqttClientOptions Options { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required SemaphoreSlim ConnectGate { get; init; }
        public string LastTopic { get; set; } = string.Empty;
        public long ReceivedCount;
        public long DecodedCount;
        public long DuplicateCount;
        public long UndecodedCount;
        public long EncryptedTopicCount;
        public long PositionCount;
        public long NodeInfoCount;
        public long TextMessageCount;
        public long TacticalEventCount;
        public long DecodeErrorCount;
        public long LastTrafficLogUnixMs;
        public long LastPacketDedupCleanupUnixMs;
        public long LastReportedReceivedCount;
        public long LastReportedDecodedCount;
        public long LastReportedDuplicateCount;
        public long LastReportedUndecodedCount;
        public long LastReportedEncryptedTopicCount;
        public long LastReportedPositionCount;
        public long LastReportedNodeInfoCount;
        public long LastReportedTextMessageCount;
        public long LastReportedTacticalEventCount;
        public long LastReportedDecodeErrorCount;
        public object PacketDedupGate { get; } = new();
        public Dictionary<ulong, long> PacketDedupCache { get; } = new();
        public Func<MqttClientConnectedEventArgs, Task> ConnectedAsyncHandler { get; set; } = _ => Task.CompletedTask;
        public Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsyncHandler { get; set; } = _ => Task.CompletedTask;
        public Func<MqttApplicationMessageReceivedEventArgs, Task> MessageAsyncHandler { get; set; } = _ => Task.CompletedTask;
    }
}
