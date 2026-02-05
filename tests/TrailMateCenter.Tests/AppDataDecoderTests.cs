using Google.Protobuf;
using Meshtastic.Protobufs;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Services;
using Xunit;

namespace TrailMateCenter.Tests;

public sealed class AppDataDecoderTests
{
    [Fact]
    public void Decode_TeamTrack_Produces_Positions()
    {
        var payload = new HostLinkBufferWriter();
        payload.WriteByte(1); // version
        payload.WriteUInt32(1000);
        payload.WriteUInt16(5);
        payload.WriteByte(2);
        payload.WriteUInt32(0b11);
        payload.WriteInt32((int)(22.5 * 1e7));
        payload.WriteInt32((int)(113.1 * 1e7));
        payload.WriteInt32((int)(22.5001 * 1e7));
        payload.WriteInt32((int)(113.1001 * 1e7));

        var packet = new AppDataPacket(
            AppDataDecoder.TeamTrackPort,
            0x01020304,
            0,
            0,
            0,
            new byte[8],
            0,
            0,
            payload.ToArray());

        var decoder = new AppDataDecoder();
        var result = decoder.Decode(packet);

        Assert.Equal(2, result.Positions.Count);
        Assert.Contains(result.TacticalEvents, ev => ev.Kind == TacticalEventKind.TrackUpdate);
    }

    [Fact]
    public void Decode_TeamChatText_Produces_Event()
    {
        var payload = new HostLinkBufferWriter();
        payload.WriteByte(1); // version
        payload.WriteByte(1); // type text
        payload.WriteUInt16(0);
        payload.WriteUInt32(1);
        payload.WriteUInt32(1000);
        payload.WriteUInt32(0x01020304);
        var textBytes = System.Text.Encoding.UTF8.GetBytes("hi");
        foreach (var b in textBytes)
            payload.WriteByte(b);

        var packet = new AppDataPacket(
            AppDataDecoder.TeamChatPort,
            0x01020304,
            0,
            0,
            0,
            new byte[8],
            0,
            0,
            payload.ToArray());

        var decoder = new AppDataDecoder();
        var result = decoder.Decode(packet);

        Assert.Contains(result.TacticalEvents, ev => ev.Kind == TacticalEventKind.ChatText && ev.Detail.Contains("hi"));
    }

    [Fact]
    public void Decode_TeamPosition_Produces_Position()
    {
        var pos = new Position
        {
            LatitudeI = (int)(23.1 * 1e7),
            LongitudeI = (int)(113.3 * 1e7),
            Altitude = 42,
            Timestamp = 1000,
        };

        var packet = new AppDataPacket(
            AppDataDecoder.TeamPositionPort,
            0x01020304,
            0,
            0,
            0,
            new byte[8],
            0,
            0,
            pos.ToByteArray());

        var decoder = new AppDataDecoder();
        var result = decoder.Decode(packet);

        Assert.Single(result.Positions);
        Assert.Contains(result.TacticalEvents, ev => ev.Kind == TacticalEventKind.PositionUpdate);
    }

    [Fact]
    public void Decode_TeamWaypoint_Produces_Waypoint_Event()
    {
        var wp = new Waypoint
        {
            Id = 99,
            LatitudeI = (int)(23.2 * 1e7),
            LongitudeI = (int)(113.4 * 1e7),
            Name = "WP1",
        };

        var packet = new AppDataPacket(
            AppDataDecoder.TeamWaypointPort,
            0x01020304,
            0,
            0,
            0,
            new byte[8],
            0,
            0,
            wp.ToByteArray());

        var decoder = new AppDataDecoder();
        var result = decoder.Decode(packet);

        Assert.Single(result.Positions);
        Assert.Contains(result.TacticalEvents, ev => ev.Kind == TacticalEventKind.Waypoint);
    }

    [Fact]
    public void Reassembler_Rebuilds_Payload()
    {
        var reassembler = new AppDataReassembler();
        var chunk1 = new byte[] { 1, 2, 3 };
        var chunk2 = new byte[] { 4, 5, 6 };

        var ev1 = new AppDataEvent(DateTimeOffset.UtcNow, 303, 1, 2, 0, 0, new byte[8], 0, 10, 6, 0, 3, chunk1);
        var ev2 = new AppDataEvent(DateTimeOffset.UtcNow, 303, 1, 2, 0, 0, new byte[8], 0, 10, 6, 3, 3, chunk2);

        var packets1 = reassembler.Accept(ev1);
        Assert.Empty(packets1);

        var packets2 = reassembler.Accept(ev2);
        var packet = Assert.Single(packets2);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, packet.Payload);
    }
}
