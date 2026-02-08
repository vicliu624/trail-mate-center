using Microsoft.Extensions.Logging;
using TrailMateCenter.Models;

namespace TrailMateCenter.Protocol;

public static class HostLinkSerializer
{
    public static byte[] BuildHelloPayload() => Array.Empty<byte>();

    public static byte[] BuildCmdTxMsgPayload(MessageSendRequest request)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt32(request.ToId);
        writer.WriteByte(request.Channel);
        writer.WriteByte(request.Flags);
        var textBytes = System.Text.Encoding.UTF8.GetBytes(request.Text ?? string.Empty);
        writer.WriteUInt16((ushort)textBytes.Length);
        foreach (var b in textBytes)
            writer.WriteByte(b);
        return writer.ToArray();
    }

    public static byte[] BuildCmdGetConfigPayload() => Array.Empty<byte>();

    public static byte[] BuildCmdSetConfigPayload(DeviceConfig config)
    {
        var writer = new HostLinkBufferWriter();
        foreach (var item in config.Items)
        {
            if (item.Value.Length > 255)
                continue;
            writer.WriteByte((byte)item.Key);
            writer.WriteByte((byte)item.Value.Length);
            foreach (var b in item.Value)
                writer.WriteByte(b);
        }
        return writer.ToArray();
    }

    public static byte[] BuildCmdSetTimePayload(DateTimeOffset time)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt64((ulong)time.ToUnixTimeSeconds());
        return writer.ToArray();
    }

    public static byte[] BuildCmdGetGpsPayload() => Array.Empty<byte>();

    public static byte[] BuildCmdTxAppDataPayload(AppDataSendRequest request, uint offset, byte[] chunk)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt32(request.Portnum);
        writer.WriteUInt32(0);
        writer.WriteUInt32(request.To);
        writer.WriteByte(request.Channel);
        writer.WriteByte((byte)request.Flags);
        var teamId = request.TeamId.Length == 8 ? request.TeamId : new byte[8];
        foreach (var b in teamId)
            writer.WriteByte(b);
        writer.WriteUInt32(request.TeamKeyId);
        writer.WriteUInt32(0);
        writer.WriteUInt32((uint)request.Payload.Length);
        writer.WriteUInt32(offset);
        writer.WriteUInt16((ushort)chunk.Length);
        foreach (var b in chunk)
            writer.WriteByte(b);
        return writer.ToArray();
    }

    public static HostLinkErrorCode ParseAck(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            return HostLinkErrorCode.Internal;
        return (HostLinkErrorCode)payload[0];
    }

    public static DeviceInfo ParseHelloAck(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadUInt16(out var protocolVersion);
        reader.TryReadUInt16(out var maxFrame);
        reader.TryReadUInt32(out var capsValue);
        reader.TryReadString8(out var model);
        reader.TryReadString8(out var firmware);

        var caps = new Capabilities
        {
            MaxFrameLength = maxFrame,
            CapabilitiesMask = (HostLinkCapabilities)capsValue,
        };

        return new DeviceInfo(model, firmware, protocolVersion, caps);
    }

    public static RxMessageEvent ParseRxMessage(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadUInt32(out var msgId);
        reader.TryReadUInt32(out var from);
        reader.TryReadUInt32(out var to);
        reader.TryReadByte(out var channel);
        reader.TryReadUInt32(out var ts);
        reader.TryReadUInt16(out var textLen);

        var text = string.Empty;
        if (textLen > 0 && reader.Remaining.Length >= textLen)
        {
            text = System.Text.Encoding.UTF8.GetString(reader.Remaining.Slice(0, textLen));
        }

        return new RxMessageEvent(
            DateTimeOffset.FromUnixTimeSeconds(ts),
            msgId,
            from,
            to,
            channel,
            text);
    }

    public static TxResultEvent ParseTxResult(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadUInt32(out var msgId);
        reader.TryReadByte(out var success);

        var code = success != 0 ? HostLinkErrorCode.Ok : HostLinkErrorCode.Internal;
        var reason = success != 0 ? "OK" : "FAIL";

        return new TxResultEvent(
            DateTimeOffset.UtcNow,
            msgId,
            success != 0,
            code,
            reason);
    }

    public static StatusEvent ParseStatus(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        var status = new StatusEvent(DateTimeOffset.UtcNow);

        while (reader.Remaining.Length >= 2)
        {
            if (!reader.TryReadByte(out var key))
                break;
            if (!reader.TryReadByte(out var len))
                break;
            if (reader.Remaining.Length < len)
                break;

            var valueSpan = reader.Remaining.Slice(0, len);
            reader = new HostLinkSpanReader(reader.Remaining.Slice(len));

            switch ((HostLinkStatusKey)key)
            {
                case HostLinkStatusKey.Battery:
                    status = status with { BatteryPercent = valueSpan[0] == 0xFF ? (int?)null : valueSpan[0] };
                    break;
                case HostLinkStatusKey.Charging:
                    status = status with { IsCharging = valueSpan[0] != 0 };
                    break;
                case HostLinkStatusKey.LinkState:
                    status = status with { LinkState = (HostLinkLinkState)valueSpan[0] };
                    break;
                case HostLinkStatusKey.MeshProtocol:
                    status = status with { MeshProtocol = valueSpan[0] };
                    break;
                case HostLinkStatusKey.Region:
                    status = status with { Region = valueSpan[0] };
                    break;
                case HostLinkStatusKey.Channel:
                    status = status with { Channel = valueSpan[0] };
                    break;
                case HostLinkStatusKey.DutyCycle:
                    status = status with { DutyCycleEnabled = valueSpan[0] != 0 };
                    break;
                case HostLinkStatusKey.ChannelUtil:
                    status = status with { ChannelUtil = valueSpan[0] };
                    break;
                case HostLinkStatusKey.LastError:
                    if (len >= 4)
                    {
                        var lastError = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(valueSpan);
                        status = status with { LastError = lastError };
                    }
                    break;
                default:
                    break;
            }
        }

        return status;
    }

    public static DeviceConfig ParseConfigFromStatus(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        var config = new DeviceConfig();

        while (reader.Remaining.Length >= 2)
        {
            if (!reader.TryReadByte(out var key))
                break;
            if (!reader.TryReadByte(out var len))
                break;
            if (reader.Remaining.Length < len)
                break;

            var valueSpan = reader.Remaining.Slice(0, len).ToArray();
            reader = new HostLinkSpanReader(reader.Remaining.Slice(len));

            if (Enum.IsDefined(typeof(HostLinkConfigKey), key))
            {
                config.Items[(HostLinkConfigKey)key] = valueSpan;
            }
        }

        return config;
    }

    public static LogEvent ParseLog(ReadOnlySpan<byte> payload)
    {
        return new LogEvent(DateTimeOffset.UtcNow, LogLevel.Information, "EV_LOG", null);
    }

    public static GpsEvent ParseGps(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadByte(out var flags);
        reader.TryReadByte(out var satellites);
        reader.TryReadUInt32(out var ageMs);
        reader.TryReadInt32(out var latE7);
        reader.TryReadInt32(out var lonE7);
        reader.TryReadInt32(out var altCm);
        reader.TryReadUInt16(out var speedCms);
        reader.TryReadUInt16(out var courseCdeg);

        var hasFix = (flags & 0x01) != 0;
        var hasAlt = (flags & 0x02) != 0;
        var hasSpeed = (flags & 0x04) != 0;
        var hasCourse = (flags & 0x08) != 0;

        double? lat = hasFix ? latE7 / 1e7 : null;
        double? lon = hasFix ? lonE7 / 1e7 : null;
        double? alt = hasAlt ? altCm / 100.0 : null;
        double? speed = hasSpeed ? speedCms / 100.0 : null;
        double? course = hasCourse ? courseCdeg / 100.0 : null;

        return new GpsEvent(DateTimeOffset.UtcNow, hasFix, satellites, ageMs, lat, lon, alt, speed, course);
    }

    public static AppDataEvent ParseAppData(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadUInt32(out var portnum);
        reader.TryReadUInt32(out var from);
        reader.TryReadUInt32(out var to);
        reader.TryReadByte(out var channel);
        reader.TryReadByte(out var flags);
        reader.TryReadBytes(8, out var teamId);
        reader.TryReadUInt32(out var teamKeyId);
        reader.TryReadUInt32(out var timestampS);
        reader.TryReadUInt32(out var totalLen);
        reader.TryReadUInt32(out var offset);
        reader.TryReadUInt16(out var chunkLen);

        var chunk = Array.Empty<byte>();
        if (chunkLen > 0 && reader.Remaining.Length >= chunkLen)
        {
            chunk = reader.Remaining.Slice(0, chunkLen).ToArray();
        }

        return new AppDataEvent(
            DateTimeOffset.UtcNow,
            portnum,
            from,
            to,
            channel,
            (HostLinkAppDataFlags)flags,
            teamId,
            teamKeyId,
            timestampS,
            totalLen,
            offset,
            chunkLen,
            chunk);
    }

    public static TeamStateEvent ParseTeamState(ReadOnlySpan<byte> payload)
    {
        var reader = new HostLinkSpanReader(payload);
        reader.TryReadByte(out var version);
        reader.TryReadByte(out var flags);
        reader.TryReadUInt16(out _);
        reader.TryReadUInt32(out var selfId);
        reader.TryReadBytes(8, out var teamId);
        reader.TryReadBytes(8, out var joinTargetId);
        reader.TryReadUInt32(out var keyId);
        reader.TryReadUInt32(out var lastEventSeq);
        reader.TryReadUInt32(out var lastUpdateS);
        reader.TryReadUInt16(out var teamNameLen);

        var teamName = string.Empty;
        if (teamNameLen > 0 && reader.Remaining.Length >= teamNameLen)
        {
            teamName = System.Text.Encoding.UTF8.GetString(reader.Remaining.Slice(0, teamNameLen));
            reader = new HostLinkSpanReader(reader.Remaining.Slice(teamNameLen));
        }

        reader.TryReadByte(out var memberCount);
        var members = new List<TeamMemberInfo>();
        for (var i = 0; i < memberCount; i++)
        {
            if (!reader.TryReadUInt32(out var nodeId))
                break;
            if (!reader.TryReadByte(out var role))
                break;
            if (!reader.TryReadByte(out var online))
                break;
            if (!reader.TryReadUInt32(out var lastSeenS))
                break;
            if (!reader.TryReadUInt16(out var nameLen))
                break;

            var name = string.Empty;
            if (nameLen > 0 && reader.Remaining.Length >= nameLen)
            {
                name = System.Text.Encoding.UTF8.GetString(reader.Remaining.Slice(0, nameLen));
                reader = new HostLinkSpanReader(reader.Remaining.Slice(nameLen));
            }

            members.Add(new TeamMemberInfo(nodeId, role, online != 0, lastSeenS, name));
        }

        return new TeamStateEvent(
            DateTimeOffset.UtcNow,
            version,
            flags,
            selfId,
            teamId,
            joinTargetId,
            keyId,
            lastEventSeq,
            lastUpdateS,
            teamName,
            members);
    }
}
