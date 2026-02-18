using System.Text;
using System.Globalization;
using Google.Protobuf;
using Meshtastic.Protobufs;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.Services;

public sealed class AppDataDecoder
{
    public const uint TeamMgmtPort = 300;
    public const uint TeamPositionPort = 301;
    public const uint TeamWaypointPort = 302;
    public const uint TeamChatPort = 303;
    public const uint TeamTrackPort = 304;
    public const uint NodeInfoPort = (uint)PortNum.NodeinfoApp;
    public const uint PositionPort = (uint)PortNum.PositionApp;
    public const uint WaypointPort = (uint)PortNum.WaypointApp;
    public const uint TelemetryPort = (uint)PortNum.TelemetryApp;
    public const uint MapReportPort = (uint)PortNum.MapReportApp;

    public AppDataDecodeResult Decode(AppDataPacket packet)
    {
        var events = new List<TacticalEvent>();
        var positions = new List<PositionUpdate>();
        var nodeInfos = new List<NodeInfoUpdate>();
        var messages = new List<MessageEntry>();

        switch (packet.Portnum)
        {
            case TeamTrackPort:
                DecodeTeamTrack(packet, events, positions);
                break;
            case TeamChatPort:
                DecodeTeamChat(packet, events, positions, messages);
                break;
            case TeamMgmtPort:
                DecodeTeamMgmt(packet, events);
                break;
            case TeamPositionPort:
                DecodeTeamPosition(packet, events, positions);
                break;
            case TeamWaypointPort:
                DecodeTeamWaypoint(packet, events, positions);
                break;
            case NodeInfoPort:
                DecodeNodeInfo(packet, nodeInfos, positions);
                break;
            case PositionPort:
                DecodePosition(packet, positions);
                break;
            case WaypointPort:
                DecodeTeamWaypoint(packet, events, positions);
                break;
            case TelemetryPort:
                DecodeTelemetry(packet, events);
                break;
            case MapReportPort:
                DecodeMapReport(packet, events, nodeInfos, positions);
                break;
            default:
                var label = ResolvePortLabel(packet.Portnum);
                var title = label is null ? $"APP {packet.Portnum}" : $"{label} (APP {packet.Portnum})";
                events.Add(BuildOpaqueEvent(packet, TacticalEventKind.Unknown, title, $"payload {packet.Payload.Length} bytes"));
                break;
        }

        return new AppDataDecodeResult(packet, events, positions, nodeInfos, messages);
    }

    private static void DecodeTeamTrack(AppDataPacket packet, List<TacticalEvent> events, List<PositionUpdate> positions)
    {
        var reader = new HostLinkSpanReader(packet.Payload);
        if (!reader.TryReadByte(out var version) || version != 1)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.TrackUpdate, "Team Track", "版本不支持"));
            return;
        }
        if (!reader.TryReadUInt32(out var startTs) ||
            !reader.TryReadUInt16(out var intervalS) ||
            !reader.TryReadByte(out var count) ||
            !reader.TryReadUInt32(out var validMask))
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.TrackUpdate, "Team Track", "解析失败"));
            return;
        }

        var baseTs = DateTimeOffset.FromUnixTimeSeconds(startTs);
        var validPoints = 0;
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadInt32(out var latE7) || !reader.TryReadInt32(out var lonE7))
                break;
            if ((validMask & (1u << i)) == 0)
                continue;
            var ts = baseTs.AddSeconds(intervalS * i);
            var lat = latE7 / 1e7;
            var lon = lonE7 / 1e7;
            positions.Add(new PositionUpdate(ts, packet.From, lat, lon, null, null, PositionSource.TeamTrack, null));
            validPoints++;
        }

        events.Add(new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.TrackUpdate),
            TacticalEventKind.TrackUpdate,
            "轨迹更新",
            $"来自 0x{packet.From:X8} · {validPoints} 点",
            packet.From,
            $"0x{packet.From:X8}",
            null,
            null));
    }

    private static void DecodeTeamChat(
        AppDataPacket packet,
        List<TacticalEvent> events,
        List<PositionUpdate> positions,
        List<MessageEntry> messages)
    {
        var reader = new HostLinkSpanReader(packet.Payload);
        if (!reader.TryReadByte(out var version) || version != 1)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.ChatText, "Team Chat", "版本不支持"));
            return;
        }
        if (!reader.TryReadByte(out var type) ||
            !reader.TryReadUInt16(out var flags) ||
            !reader.TryReadUInt32(out var msgId) ||
            !reader.TryReadUInt32(out var ts) ||
            !reader.TryReadUInt32(out var from))
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.ChatText, "Team Chat", "解析失败"));
            return;
        }

        var when = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts) : DateTimeOffset.UtcNow;
        var sender = from == 0 ? packet.From : from;

        switch (type)
        {
            case 1:
                var text = Encoding.UTF8.GetString(reader.Remaining);
                AddTeamChatMessage(packet, messages, sender, msgId, when, text);
                events.Add(new TacticalEvent(
                    when,
                    TacticalRules.GetDefaultSeverity(TacticalEventKind.ChatText),
                    TacticalEventKind.ChatText,
                    $"消息 · 0x{sender:X8}",
                    text,
                    sender,
                    $"0x{sender:X8}",
                    null,
                    null));
                break;
            case 2:
                DecodeChatLocation(packet, sender, msgId, when, reader, events, positions, messages);
                break;
            case 3:
                DecodeChatCommand(packet, sender, msgId, when, reader, events, messages);
                break;
            default:
                events.Add(BuildOpaqueEvent(packet, TacticalEventKind.ChatText, "Team Chat", $"未知类型 {type}"));
                break;
        }

        _ = flags; // reserved for future use
        _ = msgId;
    }

    private static void DecodeChatLocation(
        AppDataPacket packet,
        uint from,
        uint msgId,
        DateTimeOffset when,
        HostLinkSpanReader reader,
        List<TacticalEvent> events,
        List<PositionUpdate> positions,
        List<MessageEntry> messages)
    {
        if (!reader.TryReadInt32(out var latE7) ||
            !reader.TryReadInt32(out var lonE7) ||
            !reader.TryReadInt16(out var altM) ||
            !reader.TryReadUInt16(out var accM) ||
            !reader.TryReadUInt32(out var fixTs) ||
            !reader.TryReadByte(out var source) ||
            !reader.TryReadUInt16(out var labelLen))
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.ChatLocation, "位置", "解析失败"));
            return;
        }

        var label = string.Empty;
        if (labelLen > 0 && reader.Remaining.Length >= labelLen)
        {
            label = Encoding.UTF8.GetString(reader.Remaining.Slice(0, labelLen));
        }

        var ts = fixTs > 0 ? DateTimeOffset.FromUnixTimeSeconds(fixTs) : when;
        var lat = latE7 / 1e7;
        var lon = lonE7 / 1e7;
        TeamLocationSource? marker = TryResolveTeamLocationSource(source, out var knownSource)
            ? knownSource
            : null;
        positions.Add(new PositionUpdate(
            ts,
            from,
            lat,
            lon,
            altM,
            accM,
            PositionSource.TeamChatLocation,
            label,
            source,
            marker));

        var sourceLabel = ResolveTeamLocationSourceLabel(source, marker);
        var detail = string.IsNullOrWhiteSpace(label)
            ? $"精度 {accM}m · 来源 {sourceLabel} (raw={source})"
            : $"{label} · 精度 {accM}m · 来源 {sourceLabel} (raw={source})";
        AddTeamChatMessage(packet, messages, from, msgId, ts, $"[位置] {detail}", lat, lon, altM);

        events.Add(new TacticalEvent(
            ts,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.ChatLocation),
            TacticalEventKind.ChatLocation,
            $"位置 · 0x{from:X8}",
            detail,
            from,
            $"0x{from:X8}",
            lat,
            lon));
    }

    private static void DecodeChatCommand(
        AppDataPacket packet,
        uint from,
        uint msgId,
        DateTimeOffset when,
        HostLinkSpanReader reader,
        List<TacticalEvent> events,
        List<MessageEntry> messages)
    {
        if (!reader.TryReadByte(out var cmdType) ||
            !reader.TryReadInt32(out var latE7) ||
            !reader.TryReadInt32(out var lonE7) ||
            !reader.TryReadUInt16(out var radiusM) ||
            !reader.TryReadByte(out var priority) ||
            !reader.TryReadUInt16(out var noteLen))
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.CommandIssued, "指令", "解析失败"));
            return;
        }

        var note = string.Empty;
        if (noteLen > 0 && reader.Remaining.Length >= noteLen)
        {
            note = Encoding.UTF8.GetString(reader.Remaining.Slice(0, noteLen));
        }

        var lat = latE7 / 1e7;
        var lon = lonE7 / 1e7;
        var cmdName = cmdType switch
        {
            1 => "RallyTo",
            2 => "MoveTo",
            3 => "Hold",
            _ => $"Cmd{cmdType}",
        };
        var detail = $"半径 {radiusM}m · 优先级 {priority}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : $" · {note}");
        AddTeamChatMessage(packet, messages, from, msgId, when, $"[指令] {cmdName} · {detail}", lat, lon);

        events.Add(new TacticalEvent(
            when,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.CommandIssued),
            TacticalEventKind.CommandIssued,
            $"指令 {cmdName} · 0x{from:X8}",
            detail,
            from,
            $"0x{from:X8}",
            lat,
            lon));
    }

    private static void AddTeamChatMessage(
        AppDataPacket packet,
        List<MessageEntry> messages,
        uint sender,
        uint msgId,
        DateTimeOffset when,
        string text,
        double? latitude = null,
        double? longitude = null,
        double? altitude = null)
    {
        var isBroadcast = packet.To == 0 || packet.To == uint.MaxValue;
        var teamConversationKey = BuildTeamConversationKey(packet);
        messages.Add(new MessageEntry
        {
            Direction = MessageDirection.Incoming,
            MessageId = msgId == 0 ? null : msgId,
            FromId = sender == 0 ? null : sender,
            ToId = isBroadcast ? null : packet.To,
            From = sender == 0 ? "APPDATA" : $"0x{sender:X8}",
            To = isBroadcast ? "broadcast" : $"0x{packet.To:X8}",
            ChannelId = packet.Channel,
            Channel = packet.Channel.ToString(),
            Text = text,
            Status = MessageDeliveryStatus.Succeeded,
            Timestamp = DateTimeOffset.UtcNow,
            DeviceTimestamp = when,
            Rssi = packet.RxMeta?.RssiDbm,
            Snr = packet.RxMeta?.SnrDb,
            Hop = packet.RxMeta?.HopCount,
            Direct = packet.RxMeta?.Direct,
            Origin = packet.RxMeta?.Origin,
            FromIs = packet.RxMeta?.FromIs,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            IsTeamChat = true,
            TeamConversationKey = teamConversationKey,
        });
    }

    private static string BuildTeamConversationKey(AppDataPacket packet)
    {
        if (packet.TeamId is { Length: 8 } teamId && teamId.Any(b => b != 0))
        {
            return $"{Convert.ToHexString(teamId)}:{packet.TeamKeyId:X8}";
        }

        if (packet.TeamKeyId != 0)
        {
            return $"KEY:{packet.TeamKeyId:X8}";
        }

        return "DEFAULT";
    }

    private static void DecodeTeamMgmt(AppDataPacket packet, List<TacticalEvent> events)
    {
        var reader = new HostLinkSpanReader(packet.Payload);
        if (!reader.TryReadByte(out var version) || version != 1)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.TeamMgmt, "Team Mgmt", "版本不支持"));
            return;
        }
        if (!reader.TryReadByte(out var type) ||
            !reader.TryReadUInt16(out var reserved) ||
            !reader.TryReadUInt16(out var payloadLen))
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.TeamMgmt, "Team Mgmt", "解析失败"));
            return;
        }

        var typeName = type switch
        {
            1 => "Advertise",
            2 => "JoinRequest",
            3 => "JoinAccept",
            4 => "JoinConfirm",
            5 => "Status",
            9 => "JoinDecision",
            10 => "Kick",
            11 => "TransferLeader",
            12 => "KeyDist",
            _ => $"Type{type}",
        };

        var detail = $"payload {payloadLen} bytes";
        if (payloadLen > 0 && reader.Remaining.Length >= payloadLen)
        {
            detail = $"{typeName} · {payloadLen} bytes";
        }

        _ = reserved;
        events.Add(new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.TeamMgmt),
            TacticalEventKind.TeamMgmt,
            $"TeamMgmt {typeName}",
            detail,
            packet.From,
            $"0x{packet.From:X8}",
            null,
            null));
    }

    private static void DecodeTeamPosition(AppDataPacket packet, List<TacticalEvent> events, List<PositionUpdate> positions)
    {
        Position pos;
        try
        {
            pos = Position.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.PositionUpdate, "Team Position", "解析失败"));
            return;
        }

        if (!pos.HasLatitudeI || !pos.HasLongitudeI)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.PositionUpdate, "Team Position", "缺少坐标"));
            return;
        }

        var lat = pos.LatitudeI / 1e7;
        var lon = pos.LongitudeI / 1e7;
        var ts = ExtractPositionTimestamp(pos);
        positions.Add(new PositionUpdate(ts, packet.From, lat, lon, pos.Altitude, null, PositionSource.TeamPositionApp, null));

        events.Add(new TacticalEvent(
            ts,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.PositionUpdate),
            TacticalEventKind.PositionUpdate,
            $"位置更新 · 0x{packet.From:X8}",
            $"来源 TeamPosition · alt {pos.Altitude}m",
            packet.From,
            $"0x{packet.From:X8}",
            lat,
            lon));
    }

    private static void DecodeTeamWaypoint(AppDataPacket packet, List<TacticalEvent> events, List<PositionUpdate> positions)
    {
        Waypoint wp;
        try
        {
            wp = Waypoint.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.Waypoint, "航点", "解析失败"));
            return;
        }

        if (!wp.HasLatitudeI || !wp.HasLongitudeI)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.Waypoint, "航点", "缺少坐标"));
            return;
        }

        var lat = wp.LatitudeI / 1e7;
        var lon = wp.LongitudeI / 1e7;
        var ts = wp.Expire > 0 ? DateTimeOffset.FromUnixTimeSeconds(wp.Expire) : DateTimeOffset.UtcNow;
        positions.Add(new PositionUpdate(ts, packet.From, lat, lon, null, null, PositionSource.TeamWaypointApp, wp.Name));

        var title = string.IsNullOrWhiteSpace(wp.Name) ? "航点" : $"航点 · {wp.Name}";
        var detail = string.IsNullOrWhiteSpace(wp.Description) ? $"id {wp.Id}" : wp.Description;
        events.Add(new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.Waypoint),
            TacticalEventKind.Waypoint,
            title,
            detail,
            packet.From,
            $"0x{packet.From:X8}",
            lat,
            lon));
    }

    private static void DecodeNodeInfo(AppDataPacket packet, List<NodeInfoUpdate> nodeInfos, List<PositionUpdate> positions)
    {
        try
        {
            var user = User.Parser.ParseFrom(packet.Payload);
            if (user is not null &&
                (!string.IsNullOrWhiteSpace(user.ShortName) ||
                 !string.IsNullOrWhiteSpace(user.LongName) ||
                 !string.IsNullOrWhiteSpace(user.Id)))
            {
                nodeInfos.Add(new NodeInfoUpdate(
                    packet.From,
                    string.IsNullOrWhiteSpace(user.ShortName) ? null : user.ShortName,
                    string.IsNullOrWhiteSpace(user.LongName) ? null : user.LongName,
                    string.IsNullOrWhiteSpace(user.Id) ? null : user.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null));
                return;
            }
        }
        catch (InvalidProtocolBufferException)
        {
            // ignore and fallback
        }

        NodeInfo? info = null;
        try
        {
            info = NodeInfo.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (info is null)
            return;

        var userInfo = info.User;
        var nodeId = info.Num != 0 ? info.Num : packet.From;
        var lastHeard = info.LastHeard > 0
            ? DateTimeOffset.FromUnixTimeSeconds(info.LastHeard)
            : DateTimeOffset.UtcNow;

        double? lat = null;
        double? lon = null;
        double? alt = null;
        if (info.Position is { } pos && pos.HasLatitudeI && pos.HasLongitudeI)
        {
            lat = pos.LatitudeI / 1e7;
            lon = pos.LongitudeI / 1e7;
            if (pos.Altitude != 0)
                alt = pos.Altitude;
            var ts = ExtractPositionTimestamp(pos);
            positions.Add(new PositionUpdate(ts, nodeId, lat.Value, lon.Value, alt, null, PositionSource.DeviceGps, null));
        }

        nodeInfos.Add(new NodeInfoUpdate(
            nodeId,
            string.IsNullOrWhiteSpace(userInfo?.ShortName) ? null : userInfo.ShortName,
            string.IsNullOrWhiteSpace(userInfo?.LongName) ? null : userInfo.LongName,
            string.IsNullOrWhiteSpace(userInfo?.Id) ? null : userInfo.Id,
            info.Channel == 0 ? null : (byte?)Math.Clamp((int)info.Channel, 0, 255),
            lastHeard,
            lat,
            lon,
            alt));
    }

    private static void DecodePosition(AppDataPacket packet, List<PositionUpdate> positions)
    {
        Position pos;
        try
        {
            pos = Position.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (!pos.HasLatitudeI || !pos.HasLongitudeI)
            return;

        var lat = pos.LatitudeI / 1e7;
        var lon = pos.LongitudeI / 1e7;
        var ts = ExtractPositionTimestamp(pos);
        positions.Add(new PositionUpdate(ts, packet.From, lat, lon, pos.Altitude, null, PositionSource.DeviceGps, null));
    }

    private static void DecodeTelemetry(AppDataPacket packet, List<TacticalEvent> events)
    {
        Telemetry telemetry;
        try
        {
            telemetry = Telemetry.Parser.ParseFrom(packet.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.Telemetry, "Telemetry", "解析失败"));
            return;
        }

        var when = telemetry.Time > 0
            ? DateTimeOffset.FromUnixTimeSeconds(telemetry.Time)
            : DateTimeOffset.UtcNow;

        var detail = BuildTelemetryDetail(telemetry);
        if (string.IsNullOrWhiteSpace(detail))
            detail = $"payload {packet.Payload.Length} bytes";

        events.Add(new TacticalEvent(
            when,
            TacticalRules.GetDefaultSeverity(TacticalEventKind.Telemetry),
            TacticalEventKind.Telemetry,
            $"Telemetry · 0x{packet.From:X8}",
            detail,
            packet.From,
            $"0x{packet.From:X8}",
            null,
            null));
    }

    private static void DecodeMapReport(
        AppDataPacket packet,
        List<TacticalEvent> events,
        List<NodeInfoUpdate> nodeInfos,
        List<PositionUpdate> positions)
    {
        try
        {
            var report = MapReport.Parser.ParseFrom(packet.Payload);
            if (report is not null && packet.From != 0)
            {
                double? lat = null;
                double? lon = null;
                double? alt = null;

                // Map report coordinates use e7 integers (0 means unknown for this feed).
                if (report.LatitudeI != 0 || report.LongitudeI != 0)
                {
                    lat = report.LatitudeI / 1e7;
                    lon = report.LongitudeI / 1e7;
                    if (report.Altitude != 0)
                        alt = report.Altitude;
                    if (lat.HasValue && lon.HasValue)
                    {
                        positions.Add(new PositionUpdate(
                            DateTimeOffset.UtcNow,
                            packet.From,
                            lat.Value,
                            lon.Value,
                            alt,
                            null,
                            PositionSource.DeviceGps,
                            null));
                    }
                }

                var userId = $"!{packet.From:x8}";
                nodeInfos.Add(new NodeInfoUpdate(
                    packet.From,
                    string.IsNullOrWhiteSpace(report.ShortName) ? null : report.ShortName,
                    string.IsNullOrWhiteSpace(report.LongName) ? null : report.LongName,
                    userId,
                    null,
                    DateTimeOffset.UtcNow,
                    lat,
                    lon,
                    alt));
                return;
            }
        }
        catch (InvalidProtocolBufferException)
        {
            // ignore and fallback
        }

        var beforeInfos = nodeInfos.Count;
        var beforePositions = positions.Count;

        // Meshtastic map report payloads are node-centric; in current firmware this maps well to NodeInfo-like payloads.
        DecodeNodeInfo(packet, nodeInfos, positions);
        if (nodeInfos.Count > beforeInfos || positions.Count > beforePositions)
            return;

        // Fallback for map feeds that only publish position payloads.
        DecodePosition(packet, positions);
        if (positions.Count <= beforePositions)
        {
            events.Add(BuildOpaqueEvent(packet, TacticalEventKind.Unknown, "Map Report", $"payload {packet.Payload.Length} bytes"));
        }
    }

    private static string BuildTelemetryDetail(Telemetry telemetry)
    {
        var parts = new List<string>();

        switch (telemetry.VariantCase)
        {
            case Telemetry.VariantOneofCase.DeviceMetrics:
            {
                var m = telemetry.DeviceMetrics;
                AddIf(parts, m.HasBatteryLevel, $"电量 {m.BatteryLevel}%");
                AddIf(parts, m.HasVoltage, $"电压 {Fmt(m.Voltage)}V");
                AddIf(parts, m.HasChannelUtilization, $"信道占用 {Fmt(m.ChannelUtilization)}%");
                AddIf(parts, m.HasAirUtilTx, $"发射占空 {Fmt(m.AirUtilTx)}%");
                AddIf(parts, m.HasUptimeSeconds, $"运行 {FormatUptime(m.UptimeSeconds)}");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.EnvironmentMetrics:
            {
                var m = telemetry.EnvironmentMetrics;
                AddIf(parts, m.HasTemperature, $"温度 {Fmt(m.Temperature)}°C");
                AddIf(parts, m.HasRelativeHumidity, $"湿度 {Fmt(m.RelativeHumidity)}%");
                AddIf(parts, m.HasBarometricPressure, $"气压 {Fmt(m.BarometricPressure)}hPa");
                AddIf(parts, m.HasGasResistance, $"气阻 {Fmt(m.GasResistance)}MΩ");
                AddIf(parts, m.HasVoltage, $"电压 {Fmt(m.Voltage)}V");
                AddIf(parts, m.HasCurrent, $"电流 {Fmt(m.Current)}A");
                AddIf(parts, m.HasIaq, $"IAQ {m.Iaq}");
                AddIf(parts, m.HasLux, $"照度 {Fmt(m.Lux)}lx");
                AddIf(parts, m.HasUvLux, $"UV {Fmt(m.UvLux)}lx");
                AddIf(parts, m.HasWindSpeed, $"风速 {Fmt(m.WindSpeed)}m/s");
                AddIf(parts, m.HasWindDirection, $"风向 {m.WindDirection}°");
                AddIf(parts, m.HasRainfall1H, $"雨量1h {Fmt(m.Rainfall1H)}mm");
                AddIf(parts, m.HasRainfall24H, $"雨量24h {Fmt(m.Rainfall24H)}mm");
                AddIf(parts, m.HasSoilMoisture, $"土壤湿度 {m.SoilMoisture}%");
                AddIf(parts, m.HasSoilTemperature, $"土壤温度 {Fmt(m.SoilTemperature)}°C");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.AirQualityMetrics:
            {
                var m = telemetry.AirQualityMetrics;
                AddIf(parts, m.HasPm25Standard, $"PM2.5 {m.Pm25Standard}μg/m³");
                AddIf(parts, m.HasPm10Standard, $"PM10 {m.Pm10Standard}μg/m³");
                AddIf(parts, m.HasPm100Standard, $"PM100 {m.Pm100Standard}μg/m³");
                AddIf(parts, m.HasCo2, $"CO2 {m.Co2}ppm");
                AddIf(parts, m.HasPmTemperature, $"温度 {Fmt(m.PmTemperature)}°C");
                AddIf(parts, m.HasPmHumidity, $"湿度 {Fmt(m.PmHumidity)}%");
                AddIf(parts, m.HasPmVocIdx, $"VOC {Fmt(m.PmVocIdx)}");
                AddIf(parts, m.HasPmNoxIdx, $"NOx {Fmt(m.PmNoxIdx)}");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.PowerMetrics:
            {
                var m = telemetry.PowerMetrics;
                AddIf(parts, m.HasCh1Voltage, $"CH1 {Fmt(m.Ch1Voltage)}V");
                AddIf(parts, m.HasCh1Current, $"CH1 {Fmt(m.Ch1Current)}A");
                AddIf(parts, m.HasCh2Voltage, $"CH2 {Fmt(m.Ch2Voltage)}V");
                AddIf(parts, m.HasCh2Current, $"CH2 {Fmt(m.Ch2Current)}A");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.LocalStats:
            {
                var m = telemetry.LocalStats;
                if (m.UptimeSeconds > 0) parts.Add($"运行 {FormatUptime(m.UptimeSeconds)}");
                if (m.ChannelUtilization > 0) parts.Add($"信道占用 {Fmt(m.ChannelUtilization)}%");
                if (m.AirUtilTx > 0) parts.Add($"发射占空 {Fmt(m.AirUtilTx)}%");
                if (m.NumPacketsTx > 0 || m.NumPacketsRx > 0) parts.Add($"TX {m.NumPacketsTx} / RX {m.NumPacketsRx}");
                if (m.NumPacketsRxBad > 0) parts.Add($"RX坏包 {m.NumPacketsRxBad}");
                if (m.NoiseFloor != 0) parts.Add($"噪声 {m.NoiseFloor}dBm");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.HealthMetrics:
            {
                var m = telemetry.HealthMetrics;
                AddIf(parts, m.HasHeartBpm, $"心率 {m.HeartBpm}bpm");
                AddIf(parts, m.HasSpO2, $"血氧 {m.SpO2}%");
                AddIf(parts, m.HasTemperature, $"体温 {Fmt(m.Temperature)}°C");
                return string.Join(" · ", parts);
            }
            case Telemetry.VariantOneofCase.HostMetrics:
            {
                var m = telemetry.HostMetrics;
                if (m.UptimeSeconds > 0) parts.Add($"运行 {FormatUptime(m.UptimeSeconds)}");
                if (m.FreememBytes > 0) parts.Add($"空闲内存 {FormatBytes(m.FreememBytes)}");
                if (m.Diskfree1Bytes > 0) parts.Add($"/ 可用 {FormatBytes(m.Diskfree1Bytes)}");
                if (m.Load1 > 0) parts.Add($"负载1 {m.Load1 / 100f:F2}");
                if (!string.IsNullOrWhiteSpace(m.UserString)) parts.Add(m.UserString);
                return string.Join(" · ", parts);
            }
            default:
                return string.Empty;
        }
    }

    private static void AddIf(List<string> parts, bool condition, string text)
    {
        if (condition)
            parts.Add(text);
    }

    private static string Fmt(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatUptime(uint seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatBytes(ulong bytes)
    {
        const double k = 1024.0;
        if (bytes < k) return $"{bytes}B";
        var kb = bytes / k;
        if (kb < k) return $"{kb:F1}KB";
        var mb = kb / k;
        if (mb < k) return $"{mb:F1}MB";
        var gb = mb / k;
        return $"{gb:F1}GB";
    }

    private static bool TryResolveTeamLocationSource(byte raw, out TeamLocationSource source)
    {
        switch (raw)
        {
            case 0:
                source = TeamLocationSource.None;
                return true;
            case 1:
                source = TeamLocationSource.AreaCleared;
                return true;
            case 2:
                source = TeamLocationSource.BaseCamp;
                return true;
            case 3:
                source = TeamLocationSource.GoodFind;
                return true;
            case 4:
                source = TeamLocationSource.Rally;
                return true;
            case 5:
                source = TeamLocationSource.Sos;
                return true;
            default:
                source = default;
                return false;
        }
    }

    private static string ResolveTeamLocationSourceLabel(byte raw, TeamLocationSource? source)
    {
        if (!source.HasValue)
            return $"Unknown({raw})";

        return source.Value switch
        {
            TeamLocationSource.None => "None",
            TeamLocationSource.AreaCleared => "AreaCleared",
            TeamLocationSource.BaseCamp => "BaseCamp",
            TeamLocationSource.GoodFind => "GoodFind",
            TeamLocationSource.Rally => "Rally",
            TeamLocationSource.Sos => "Sos",
            _ => $"Unknown({raw})",
        };
    }

    private static DateTimeOffset ExtractPositionTimestamp(Position pos)
    {
        if (pos.Timestamp != 0)
            return DateTimeOffset.FromUnixTimeSeconds(pos.Timestamp);
        if (pos.Time != 0)
            return DateTimeOffset.FromUnixTimeSeconds(pos.Time);
        return DateTimeOffset.UtcNow;
    }

    private static TacticalEvent BuildOpaqueEvent(AppDataPacket packet, TacticalEventKind kind, string title, string detail)
    {
        return new TacticalEvent(
            DateTimeOffset.UtcNow,
            TacticalRules.GetDefaultSeverity(kind),
            kind,
            title,
            detail,
            packet.From,
            $"0x{packet.From:X8}",
            null,
            null);
    }

    private static string? ResolvePortLabel(uint port)
    {
        if (!Enum.IsDefined(typeof(PortNum), (int)port))
            return null;

        var name = Enum.GetName(typeof(PortNum), (int)port);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[^1].Equals("APP", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count == 0)
            return name;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].ToLowerInvariant();
            parts[i] = part.Length switch
            {
                0 => part,
                1 => part.ToUpperInvariant(),
                _ => char.ToUpperInvariant(part[0]) + part[1..],
            };
        }

        return string.Join(' ', parts);
    }
}

public sealed record AppDataDecodeResult(
    AppDataPacket Packet,
    IReadOnlyList<TacticalEvent> TacticalEvents,
    IReadOnlyList<PositionUpdate> Positions,
    IReadOnlyList<NodeInfoUpdate> NodeInfos,
    IReadOnlyList<MessageEntry> Messages);
