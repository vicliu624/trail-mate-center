using System.Security.Cryptography;
using System.Text;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.Services;

public static class TeamChatEncoder
{
    public static byte[] BuildTextPayload(string? text, uint from = 0)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteByte(1);
        writer.WriteByte((byte)TeamChatType.Text);
        writer.WriteUInt16(0);
        writer.WriteUInt32(GenerateMessageId());
        writer.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteUInt32(from);

        var textBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        foreach (var b in textBytes)
            writer.WriteByte(b);
        return writer.ToArray();
    }

    public static byte[] BuildCommandPayload(TeamCommandRequest request, uint from = 0)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteByte(1);
        writer.WriteByte((byte)TeamChatType.Command);
        writer.WriteUInt16(0);
        writer.WriteUInt32(GenerateMessageId());
        writer.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteUInt32(from);
        writer.WriteByte((byte)request.CommandType);
        writer.WriteInt32((int)Math.Round(request.Latitude * 1e7));
        writer.WriteInt32((int)Math.Round(request.Longitude * 1e7));
        writer.WriteUInt16(request.RadiusMeters);
        writer.WriteByte(request.Priority);
        var noteBytes = Encoding.UTF8.GetBytes(request.Note ?? string.Empty);
        writer.WriteUInt16((ushort)noteBytes.Length);
        foreach (var b in noteBytes)
            writer.WriteByte(b);
        return writer.ToArray();
    }

    public static byte[] BuildLocationPayload(TeamLocationPostRequest request, uint from = 0)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteByte(1);
        writer.WriteByte((byte)TeamChatType.Location);
        writer.WriteUInt16(0);
        writer.WriteUInt32(GenerateMessageId());
        writer.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteUInt32(from);
        writer.WriteInt32((int)Math.Round(request.Latitude * 1e7));
        writer.WriteInt32((int)Math.Round(request.Longitude * 1e7));
        writer.WriteUInt16(unchecked((ushort)(request.AltitudeMeters ?? 0)));
        writer.WriteUInt16(request.AccuracyMeters ?? 0);
        writer.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteByte((byte)request.Source);

        var labelBytes = Encoding.UTF8.GetBytes(request.Label ?? string.Empty);
        var trimmed = labelBytes.Length > ushort.MaxValue
            ? labelBytes.AsSpan(0, ushort.MaxValue).ToArray()
            : labelBytes;
        writer.WriteUInt16((ushort)trimmed.Length);
        foreach (var b in trimmed)
            writer.WriteByte(b);
        return writer.ToArray();
    }

    private static uint GenerateMessageId()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        return BitConverter.ToUInt32(buffer);
    }
}
