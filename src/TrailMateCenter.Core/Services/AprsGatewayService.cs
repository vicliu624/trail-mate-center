using Google.Protobuf;
using Meshtastic.Protobufs;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TrailMateCenter.Aprs;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public sealed record AprsGatewayStats(
    long Sent,
    long Dropped,
    long DedupeHits,
    long RateLimited,
    long Errors);

public sealed class AprsGatewayService : IAsyncDisposable
{
    private readonly ILogger<AprsGatewayService> _logger;
    private readonly AprsIsClient _client;
    private readonly SessionStore _sessionStore;
    private readonly AppDataReassembler _reassembler = new();
    private readonly AppDataDecoder _decoder = new();
    private readonly ConcurrentDictionary<uint, string> _nodeIdMap = new();
    private readonly ConcurrentDictionary<uint, NodeInfoUpdate> _nodeInfos = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dedupe = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _rateLimit = new();
    private readonly ConcurrentDictionary<uint, TelemetryState> _telemetryState = new();
    private AprsSettings _settings = new();
    private long _sent;
    private long _dropped;
    private long _dedupeHits;
    private long _rateLimited;
    private long _errors;
    private CancellationTokenSource? _cts;
    private Task? _housekeepingTask;

    public AprsGatewayService(
        ILogger<AprsGatewayService> logger,
        AprsIsClient client,
        SessionStore sessionStore)
    {
        _logger = logger;
        _client = client;
        _sessionStore = sessionStore;
    }

    public event EventHandler<AprsGatewayStats>? StatsChanged;

    public void ApplySettings(AprsSettings settings)
    {
        _settings = settings;
        _client.ApplySettings(settings);
        EnsureHousekeeping();
    }

    public void Start()
    {
        _sessionStore.EventAdded += OnEventAdded;
        _sessionStore.NodeInfoUpdated += OnNodeInfoUpdated;
    }

    public async ValueTask DisposeAsync()
    {
        _sessionStore.EventAdded -= OnEventAdded;
        _sessionStore.NodeInfoUpdated -= OnNodeInfoUpdated;
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_housekeepingTask is not null)
                await _housekeepingTask;
        }
    }

    private void EnsureHousekeeping()
    {
        if (_cts is not null)
            return;
        _cts = new CancellationTokenSource();
        _housekeepingTask = Task.Run(() => HousekeepingLoopAsync(_cts.Token));
    }

    private async Task HousekeepingLoopAsync(CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var dedupeWindow = TimeSpan.FromSeconds(_settings.DedupeWindowSec);
            foreach (var kvp in _dedupe)
            {
                if (now - kvp.Value > dedupeWindow)
                    _dedupe.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void OnNodeInfoUpdated(object? sender, NodeInfoUpdate info)
    {
        _nodeInfos[info.NodeId] = info;
        if (TryParseCallsign(info.UserId, out var callsign))
            _nodeIdMap[info.NodeId] = callsign;
    }

    private void OnEventAdded(object? sender, HostLinkEvent ev)
    {
        if (!_settings.Enabled)
            return;

        switch (ev)
        {
            case AppDataEvent app:
                HandleAppData(app);
                break;
            case RxMessageEvent msg:
                if (_settings.EmitMessages)
                    HandleRxMessage(msg);
                break;
            case ConfigEvent cfg:
                HandleConfig(cfg);
                break;
        }
    }

    private void HandleConfig(ConfigEvent cfg)
    {
        if (!cfg.Config.Items.TryGetValue(HostLinkConfigKey.AprsNodeIdMap, out var value))
            return;
        var reader = new HostLinkSpanReader(value);
        while (reader.Remaining.Length >= 5)
        {
            if (!reader.TryReadUInt32(out var nodeId))
                break;
            if (!reader.TryReadByte(out var len))
                break;
            if (reader.Remaining.Length < len)
                break;
            var name = System.Text.Encoding.ASCII.GetString(reader.Remaining.Slice(0, len));
            reader = new HostLinkSpanReader(reader.Remaining.Slice(len));
            if (TryParseCallsign(name, out var cs))
                _nodeIdMap[nodeId] = cs;
        }
    }

    private void HandleRxMessage(RxMessageEvent msg)
    {
        if (!HasRequiredRxMeta(msg.RxMeta))
        {
            _errors++;
            EmitStats();
            return;
        }

        if (IsFromIs(msg.RxMeta))
        {
            _dropped++;
            EmitStats();
            return;
        }

        if (!TryResolveCallsign(msg.From, out var src))
            return;

        var dest = ResolveMessageAddressee(msg.To);
        var messageId = BuildMessageId(msg.MessageId);
        var path = BuildPath(msg.RxMeta);
        var info = AprsPacketFormatter.BuildMessageInfo(dest, msg.Text, messageId);
        EnqueueAprs(src, path, info, AprsPacketKind.Message, msg.RxMeta);
    }

    private void HandleAppData(AppDataEvent app)
    {
        var packets = _reassembler.Accept(app);
        foreach (var packet in packets)
        {
            if (packet.Portnum == (uint)PortNum.NodeinfoApp)
                HandleNodeInfo(packet);

            if (!HasRequiredRxMeta(packet.RxMeta))
            {
                _errors++;
                EmitStats();
                continue;
            }

            if (IsFromIs(packet.RxMeta))
            {
                _dropped++;
                EmitStats();
                continue;
            }

            if (packet.Portnum == (uint)PortNum.TelemetryApp && _settings.EmitTelemetry)
                HandleTelemetry(packet);
            if (packet.Portnum == (uint)PortNum.NodeStatusApp && _settings.EmitStatus)
                HandleStatus(packet);
            if (packet.Portnum == (uint)PortNum.WaypointApp && _settings.EmitWaypoints)
                HandleWaypoint(packet);

            var positionsHandled = false;
            if (packet.Portnum == (uint)PortNum.PositionApp)
            {
                positionsHandled = TryHandlePositionPacket(packet, PositionSource.DeviceGps);
            }
            else if (packet.Portnum == AppDataDecoder.TeamPositionPort)
            {
                positionsHandled = TryHandlePositionPacket(packet, PositionSource.TeamPositionApp);
            }

            var decoded = _decoder.Decode(packet);
            if (!positionsHandled)
            {
                foreach (var pos in decoded.Positions)
                {
                    HandlePosition(pos, packet.RxMeta);
                }
            }
        }
    }

    private void HandleNodeInfo(AppDataPacket packet)
    {
        try
        {
            var user = User.Parser.ParseFrom(packet.Payload);
            if (!string.IsNullOrWhiteSpace(user.Id) && TryParseCallsign(user.Id, out var cs))
                _nodeIdMap[packet.From] = cs;
        }
        catch (InvalidProtocolBufferException)
        {
            // ignore
        }
    }

    private void HandlePosition(PositionUpdate pos, RxMetadata? meta)
    {
        HandlePosition(pos, meta, null, null);
    }

    private bool TryHandlePositionPacket(AppDataPacket packet, PositionSource source)
    {
        Position pos;
        try
        {
            pos = Position.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }

        if (!pos.HasLatitudeI || !pos.HasLongitudeI)
            return false;

        var lat = pos.LatitudeI / 1e7;
        var lon = pos.LongitudeI / 1e7;
        var ts = ExtractPositionTimestamp(pos);
        var altitude = pos.HasAltitude ? (double?)pos.Altitude : null;
        var speedMps = pos.HasGroundSpeed ? (double?)pos.GroundSpeed : null;
        var courseDeg = pos.HasGroundTrack ? pos.GroundTrack / 100.0 : (double?)null;

        var update = new PositionUpdate(
            ts,
            packet.From,
            lat,
            lon,
            altitude,
            null,
            source,
            null);

        HandlePosition(update, packet.RxMeta, speedMps, courseDeg);
        return true;
    }

    private void HandlePosition(PositionUpdate pos, RxMetadata? meta, double? speedMps, double? courseDeg)
    {
        if (!TryResolveCallsign(pos.SourceId, out var src))
            return;

        var path = BuildPath(meta);
        var comment = pos.Label;
        var speedKts = speedMps.HasValue ? speedMps.Value * 1.943844 : (double?)null;

        var info = AprsPacketFormatter.BuildPositionInfo(
            pos.Latitude,
            pos.Longitude,
            _settings.SymbolTable,
            _settings.SymbolCode,
            _settings.UseCompressed,
            pos.Timestamp,
            courseDeg,
            speedKts,
            pos.AltitudeMeters,
            comment);

        EnqueueAprs(src, path, info, AprsPacketKind.Position, meta);
    }

    private void HandleWaypoint(AppDataPacket packet)
    {
        Waypoint wp;
        try
        {
            wp = Waypoint.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (!wp.HasLatitudeI || !wp.HasLongitudeI)
            return;

        if (!TryResolveCallsign(packet.From, out var src))
            return;

        var lat = wp.LatitudeI / 1e7;
        var lon = wp.LongitudeI / 1e7;
        var name = string.IsNullOrWhiteSpace(wp.Name) ? $"WP{wp.Id}" : wp.Name;
        var alive = wp.Expire == 0 || DateTimeOffset.FromUnixTimeSeconds(wp.Expire) > DateTimeOffset.UtcNow;
        var ts = wp.Expire > 0 ? DateTimeOffset.FromUnixTimeSeconds(wp.Expire) : DateTimeOffset.UtcNow;
        var info = AprsPacketFormatter.BuildObjectInfo(name, alive, ts, lat, lon, _settings.SymbolTable, _settings.SymbolCode, wp.Description);
        EnqueueAprs(src, BuildPath(packet.RxMeta), info, AprsPacketKind.Object, packet.RxMeta);
    }

    private void HandleStatus(AppDataPacket packet)
    {
        StatusMessage status;
        try
        {
            status = StatusMessage.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (!TryResolveCallsign(packet.From, out var src))
            return;

        var info = AprsPacketFormatter.BuildStatusInfo(status.Status);
        EnqueueAprs(src, BuildPath(packet.RxMeta), info, AprsPacketKind.Status, packet.RxMeta);
    }

    private void HandleTelemetry(AppDataPacket packet)
    {
        Telemetry telemetry;
        try
        {
            telemetry = Telemetry.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (!TryResolveCallsign(packet.From, out var src))
            return;

        var state = _telemetryState.GetOrAdd(packet.From, _ => new TelemetryState());
        var sample = AprsTelemetryMapper.Map(telemetry, state.NextSequence());
        if (sample is null)
            return;

        var path = BuildPath(packet.RxMeta);
        if (state.ShouldSendDefinition())
        {
            EnqueueAprs(src, path, AprsPacketFormatter.BuildParmLine(sample.Definition.Parm), AprsPacketKind.Telemetry, packet.RxMeta);
            EnqueueAprs(src, path, AprsPacketFormatter.BuildUnitLine(sample.Definition.Unit), AprsPacketKind.Telemetry, packet.RxMeta);
            EnqueueAprs(src, path, AprsPacketFormatter.BuildEqnsLine(sample.Definition.Eqns), AprsPacketKind.Telemetry, packet.RxMeta);
            EnqueueAprs(src, path, AprsPacketFormatter.BuildBitsLine(sample.Definition.Bits, sample.Definition.BitLabels), AprsPacketKind.Telemetry, packet.RxMeta);
            state.MarkDefinitionSent();
        }

        var info = AprsPacketFormatter.BuildTelemetryInfo(sample.Sequence, sample.Analog, sample.Digital);
        EnqueueAprs(src, path, info, AprsPacketKind.Telemetry, packet.RxMeta);

        if (_settings.EmitWeather)
            EmitWeatherIfAvailable(src, telemetry, path, packet.RxMeta);
    }

    private void EmitWeatherIfAvailable(string src, Telemetry telemetry, IReadOnlyList<string> path, RxMetadata? meta)
    {
        if (telemetry.VariantCase != Telemetry.VariantOneofCase.EnvironmentMetrics)
            return;
        var m = telemetry.EnvironmentMetrics;
        if (!m.HasTemperature && !m.HasRelativeHumidity && !m.HasBarometricPressure && !m.HasWindSpeed && !m.HasWindDirection)
            return;
        var info = AprsPacketFormatter.BuildWeatherInfo(
            m.HasWindDirection ? m.WindDirection : null,
            m.HasWindSpeed ? m.WindSpeed : null,
            null,
            m.HasTemperature ? m.Temperature : null,
            m.HasRainfall1H ? m.Rainfall1H : null,
            m.HasRainfall24H ? m.Rainfall24H : null,
            null,
            m.HasRelativeHumidity ? m.RelativeHumidity : null,
            m.HasBarometricPressure ? m.BarometricPressure : null,
            null);
        EnqueueAprs(src, path, info, AprsPacketKind.Weather, meta);
    }

    private bool TryResolveCallsign(uint nodeId, out string callsign)
    {
        if (_nodeIdMap.TryGetValue(nodeId, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            callsign = mapped;
            return true;
        }
        if (_nodeInfos.TryGetValue(nodeId, out var info) && info is not null && TryParseCallsign(info.UserId, out callsign))
            return true;
        callsign = string.Empty;
        return false;
    }

    private string ResolveMessageAddressee(uint toId)
    {
        if (toId == 0)
            return "BLNALL";
        if (TryResolveCallsign(toId, out var cs))
            return cs;
        return "BLNALL";
    }

    private IReadOnlyList<string> BuildPath(RxMetadata? meta)
    {
        var path = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.Path))
        {
            foreach (var token in _settings.Path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.StartsWith("WIDE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (token.StartsWith("TRACE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (token.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                    continue;
                path.Add(token);
            }
        }

        var direct = meta?.Direct == true;
        path.Add(direct ? "qAR" : "qAO");
        var igate = _settings.IgateSsid > 0 ? $"{_settings.IgateCallsign}-{_settings.IgateSsid}" : _settings.IgateCallsign;
        path.Add(igate);
        return path;
    }

    private void EnqueueAprs(string source, IReadOnlyList<string> path, string info, AprsPacketKind kind, RxMetadata? meta)
    {
        if (!ShouldSend(source, kind))
        {
            _rateLimited++;
            EmitStats();
            return;
        }

        var packet = AprsPacketFormatter.BuildPacket(source, _settings.ToCall, path, info);
        var dedupeKey = BuildDedupeKey(kind, source, info, meta);
        if (IsDuplicate(dedupeKey))
        {
            _dedupeHits++;
            EmitStats();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var baseTime = meta?.TimestampUtc ?? now;
        var expireAt = baseTime.AddSeconds(Math.Max(5, _settings.DedupeWindowSec * 2));
        if (expireAt < now)
            expireAt = now.AddSeconds(5);
        _client.Enqueue(packet, expireAt);
        _sent++;
        EmitStats();
    }

    private bool ShouldSend(string source, AprsPacketKind kind)
    {
        var key = $"{source}:{kind}";
        var now = DateTimeOffset.UtcNow;
        var interval = kind == AprsPacketKind.Position ? _settings.PositionIntervalSec : _settings.TxMinIntervalSec;
        if (_rateLimit.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(interval))
            return false;
        _rateLimit[key] = now;
        return true;
    }

    private bool IsDuplicate(string key)
    {
        var now = DateTimeOffset.UtcNow;
        if (_dedupe.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(_settings.DedupeWindowSec))
            return true;
        _dedupe[key] = now;
        return false;
    }

    private bool IsFromIs(RxMetadata? meta)
    {
        if (meta is null)
            return false;
        if (meta.FromIs == true)
            return true;
        return meta.Origin == RxOrigin.External;
    }

    private bool HasRequiredRxMeta(RxMetadata? meta)
    {
        if (meta is null)
            return false;

        var hasTimestamp = meta.TimestampUtc.HasValue || meta.TimestampMs.HasValue;
        if (!hasTimestamp)
            return false;

        if (!meta.Direct.HasValue)
            return false;

        if (meta.Origin == RxOrigin.Unknown)
            return false;

        if (!meta.FromIs.HasValue)
            return false;

        if (!meta.RssiDbm.HasValue || !meta.SnrDb.HasValue)
            return false;

        if (!meta.HopCount.HasValue && !meta.HopLimit.HasValue)
            return false;

        if (!meta.PacketId.HasValue)
            return false;

        return true;
    }

    private string BuildDedupeKey(AprsPacketKind kind, string source, string info, RxMetadata? meta)
    {
        if (meta?.PacketId.HasValue == true && meta.PacketId.Value != 0)
            return $"{kind}|{source}|pid:{meta.PacketId.Value}";
        return $"{kind}|{source}|{info}";
    }

    private static DateTimeOffset ExtractPositionTimestamp(Position pos)
    {
        if (pos.Timestamp != 0)
            return DateTimeOffset.FromUnixTimeSeconds(pos.Timestamp);
        if (pos.Time != 0)
            return DateTimeOffset.FromUnixTimeSeconds(pos.Time);
        return DateTimeOffset.UtcNow;
    }

    private static bool TryParseCallsign(string? value, out string callsign)
    {
        callsign = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var cleaned = value.Trim().ToUpperInvariant();
        if (cleaned.Length < 1 || cleaned.Length > 9)
            return false;
        foreach (var ch in cleaned)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '-'))
                return false;
        }
        callsign = cleaned;
        return true;
    }

    private static string BuildMessageId(uint? messageId)
    {
        if (!messageId.HasValue)
            return string.Empty;
        var hex = messageId.Value.ToString("X");
        return hex.Length <= 5 ? hex : hex[^5..];
    }

    private void EmitStats()
    {
        StatsChanged?.Invoke(this, new AprsGatewayStats(_sent, _dropped, _dedupeHits, _rateLimited, _errors));
    }

    private sealed class TelemetryState
    {
        private int _seq;
        private DateTimeOffset _lastDefinition = DateTimeOffset.MinValue;

        public int NextSequence()
        {
            _seq = (_seq + 1) % 1000;
            return _seq;
        }

        public bool ShouldSendDefinition()
        {
            return DateTimeOffset.UtcNow - _lastDefinition > TimeSpan.FromMinutes(30);
        }

        public void MarkDefinitionSent()
        {
            _lastDefinition = DateTimeOffset.UtcNow;
        }
    }

    private enum AprsPacketKind
    {
        Position,
        Message,
        Telemetry,
        Status,
        Weather,
        Object,
    }
}
