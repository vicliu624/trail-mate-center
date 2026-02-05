using System.Security.Cryptography;
using System.Text;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.Services;

public static class TeamChatEncoder
{
    public static byte[] BuildCommandPayload(TeamCommandRequest request)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteByte(1);
        writer.WriteByte((byte)TeamChatType.Command);
        writer.WriteUInt16(0);
        writer.WriteUInt32(GenerateMessageId());
        writer.WriteUInt32((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        writer.WriteUInt32(0);
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

    private static uint GenerateMessageId()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        return BitConverter.ToUInt32(buffer);
    }
}
