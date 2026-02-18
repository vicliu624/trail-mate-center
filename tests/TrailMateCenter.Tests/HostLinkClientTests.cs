using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;
using TrailMateCenter.Transport;
using Xunit;

namespace TrailMateCenter.Tests;

public sealed class HostLinkClientTests
{
    [Fact]
    public async Task Handshake_Completes_OnHelloAck()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            var frame = codec.DrainFrames().First();
            if (frame.Type == HostLinkFrameType.Hello)
            {
                var payload = BuildHelloAckPayload();
                var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                var bytes = HostLinkCodec.Encode(ackFrame);
                fake.Inject(bytes);
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        Assert.NotNull(client.DeviceInfo);
        Assert.Equal("TrailMate", client.DeviceInfo!.Model);
    }

    [Fact]
    public async Task Handshake_Sends_SetTime_When_Supported()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapSetTime);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdSetTime)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.Ok });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var sentFrames = DecodeWrittenFrames(fake.Writes);
        Assert.Contains(sentFrames, frame => frame.Type == HostLinkFrameType.CmdSetTime);
    }

    [Fact]
    public async Task Handshake_DoesNotSend_SetTime_When_Unsupported()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload();
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var sentFrames = DecodeWrittenFrames(fake.Writes);
        Assert.DoesNotContain(sentFrames, frame => frame.Type == HostLinkFrameType.CmdSetTime);
    }

    [Fact]
    public async Task SendMessage_Updates_OnAck_And_TxResult()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload();
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxMsg)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.Ok });
                    fake.Inject(HostLinkCodec.Encode(ack));

                    var txPayload = BuildTxResultPayload(1234, true);
                    var evtFrame = new HostLinkFrame(HostLinkFrameType.EvTxResult, 0, txPayload);
                    fake.Inject(HostLinkCodec.Encode(evtFrame));
                }
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var tcs = new TaskCompletionSource<MessageEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageUpdated += (_, msg) =>
        {
            if (msg.Status == MessageDeliveryStatus.Succeeded)
                tcs.TrySetResult(msg);
        };

        var entry = await client.SendMessageAsync(new MessageSendRequest
        {
            ToId = 0x01020304,
            Channel = 1,
            Text = "Hello",
        }, CancellationToken.None);

        var updated = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MessageDeliveryStatus.Succeeded, updated.Status);
    }

    [Fact]
    public async Task SendTeamText_Sends_AppData_And_Updates_Message()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.Ok });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var tcs = new TaskCompletionSource<MessageEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageUpdated += (_, msg) =>
        {
            if (msg.IsTeamChat && msg.Direction == MessageDirection.Outgoing && msg.Status == MessageDeliveryStatus.Succeeded)
            {
                tcs.TrySetResult(msg);
            }
        };

        var result = await client.SendTeamTextAsync("hi team", to: null, channel: 1, teamConversationKey: "1122334455667788:11223344", CancellationToken.None);
        var updated = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(HostLinkErrorCode.Ok, result);
        Assert.Equal("hi team", updated.Text);
        Assert.True(updated.IsTeamChat);
        Assert.Equal((byte)1, updated.ChannelId);

        var sentFrames = DecodeWrittenFrames(fake.Writes);
        Assert.Contains(sentFrames, frame => frame.Type == HostLinkFrameType.CmdTxAppData);
    }

    [Fact]
    public async Task SendTeamText_Uses_TeamState_Metadata_When_Available()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData |
                        HostLinkCapabilities.CapTeamState);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.Ok });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var teamId = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var teamState = BuildTeamStatePayload(
            flags: 0x11,
            selfId: 0x01020304,
            teamId: teamId,
            keyId: 0x11223344);
        fake.Inject(HostLinkCodec.Encode(new HostLinkFrame(HostLinkFrameType.EvTeamState, 0, teamState)));

        var result = await client.SendTeamTextAsync("hi team", to: null, channel: 1, teamConversationKey: null, CancellationToken.None);

        Assert.Equal(HostLinkErrorCode.Ok, result);

        var sentFrames = DecodeWrittenFrames(fake.Writes);
        var appFrame = sentFrames.Last(f => f.Type == HostLinkFrameType.CmdTxAppData);
        var appData = ParseCmdTxAppDataHeader(appFrame.Payload.Span);
        Assert.Equal((uint)0x01020304, appData.From);
        Assert.True(appData.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata));
        Assert.Equal(teamId, appData.TeamId);
        Assert.Equal((uint)0x11223344, appData.TeamKeyId);
    }

    [Fact]
    public async Task SendTeamText_Uses_ConversationKey_Metadata_Fallback()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.Ok });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var result = await client.SendTeamTextAsync(
            "hi team",
            to: null,
            channel: 0,
            teamConversationKey: "1122334455667788:99AABBCC",
            CancellationToken.None);

        Assert.Equal(HostLinkErrorCode.Ok, result);

        var sentFrames = DecodeWrittenFrames(fake.Writes);
        var appFrame = sentFrames.Last(f => f.Type == HostLinkFrameType.CmdTxAppData);
        var appData = ParseCmdTxAppDataHeader(appFrame.Payload.Span);
        Assert.True(appData.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata));
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 }, appData.TeamId);
        Assert.Equal((uint)0x99AABBCC, appData.TeamKeyId);
    }

    [Fact]
    public async Task SendTeamText_WithoutTeamContext_StillAttempts_AppData_Send()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)HostLinkErrorCode.InvalidParam });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var result = await client.SendTeamTextAsync(
            "hi team",
            to: null,
            channel: 0,
            teamConversationKey: null,
            CancellationToken.None);

        Assert.Equal(HostLinkErrorCode.InvalidParam, result);
        var sentFrames = DecodeWrittenFrames(fake.Writes);
        Assert.Contains(sentFrames, frame => frame.Type == HostLinkFrameType.CmdTxAppData);
    }

    [Fact]
    public async Task SendTeamText_Retries_Without_Metadata_On_InvalidParam()
    {
        var fake = new FakeTransport();
        var appDataWriteCount = 0;
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData |
                        HostLinkCapabilities.CapTeamState);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    appDataWriteCount++;
                    var code = appDataWriteCount == 1
                        ? HostLinkErrorCode.InvalidParam
                        : HostLinkErrorCode.Ok;
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)code });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var teamId = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var teamState = BuildTeamStatePayload(
            flags: 0x11,
            selfId: 0,
            teamId: teamId,
            keyId: 0x11223344);
        fake.Inject(HostLinkCodec.Encode(new HostLinkFrame(HostLinkFrameType.EvTeamState, 0, teamState)));

        var result = await client.SendTeamTextAsync(
            "hi team",
            to: 0x01020304,
            channel: 1,
            teamConversationKey: null,
            CancellationToken.None);

        Assert.Equal(HostLinkErrorCode.Ok, result);
        Assert.Equal(2, appDataWriteCount);

        var appFrames = DecodeWrittenFrames(fake.Writes)
            .Where(f => f.Type == HostLinkFrameType.CmdTxAppData)
            .ToArray();
        Assert.Equal(2, appFrames.Length);

        var first = ParseCmdTxAppDataHeader(appFrames[0].Payload.Span);
        Assert.True(first.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata));
        Assert.Equal(teamId, first.TeamId);
        Assert.Equal((uint)0x11223344, first.TeamKeyId);

        var second = ParseCmdTxAppDataHeader(appFrames[1].Payload.Span);
        Assert.False(second.Flags.HasFlag(HostLinkAppDataFlags.HasTeamMetadata));
        Assert.Equal(new byte[8], second.TeamId);
        Assert.Equal(0u, second.TeamKeyId);
    }

    [Fact]
    public async Task SendTeamText_Switches_Wire_Format_After_All_Candidates_InvalidParam()
    {
        var fake = new FakeTransport();
        var appPayloads = new List<byte[]>();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload(
                        HostLinkCapabilities.CapTxMsg |
                        HostLinkCapabilities.CapConfig |
                        HostLinkCapabilities.CapStatus |
                        HostLinkCapabilities.CapTxAppData);
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
                else if (frame.Type == HostLinkFrameType.CmdTxAppData)
                {
                    appPayloads.Add(frame.Payload.ToArray());
                    var code = appPayloads.Count <= 2
                        ? HostLinkErrorCode.InvalidParam
                        : HostLinkErrorCode.Ok;
                    var ack = new HostLinkFrame(HostLinkFrameType.Ack, frame.Seq, new byte[] { (byte)code });
                    fake.Inject(HostLinkCodec.Encode(ack));
                }
            }

            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var result = await client.SendTeamTextAsync(
            "wire fallback",
            to: null,
            channel: 0,
            teamConversationKey: null,
            CancellationToken.None);

        Assert.Equal(HostLinkErrorCode.Ok, result);
        Assert.Equal(3, appPayloads.Count);
        Assert.Equal(appPayloads[0].Length, appPayloads[1].Length);
        Assert.Equal(appPayloads[0].Length + 4, appPayloads[2].Length);
    }

    [Fact]
    public async Task Reconnect_When_Transport_Error()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            foreach (var frame in codec.DrainFrames())
            {
                if (frame.Type == HostLinkFrameType.Hello)
                {
                    var payload = BuildHelloAckPayload();
                    var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                    fake.Inject(HostLinkCodec.Encode(ackFrame));
                }
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = true, ReconnectDelay = TimeSpan.FromMilliseconds(100) }, CancellationToken.None);

        fake.RaiseError(new TransportError(TransportErrorType.Disconnected, "Lost"));
        await Task.Delay(400);

        Assert.True(fake.OpenCount >= 2);
    }

    [Fact]
    public async Task CrcError_Emits_ProtocolError()
    {
        var fake = new FakeTransport();
        fake.OnWriteAsync = data =>
        {
            var codec = new HostLinkCodec();
            codec.Append(data.Span);
            var frame = codec.DrainFrames().First();
            if (frame.Type == HostLinkFrameType.Hello)
            {
                var payload = BuildHelloAckPayload();
                var ackFrame = new HostLinkFrame(HostLinkFrameType.HelloAck, 0, payload);
                fake.Inject(HostLinkCodec.Encode(ackFrame));
            }
            return Task.CompletedTask;
        };

        var client = BuildClient(fake);
        await client.ConnectAsync(new SerialEndpoint("COM1"), new ConnectionOptions { AutoReconnect = false }, CancellationToken.None);

        var tcs = new TaskCompletionSource<HostLinkDecodeError>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ProtocolError += (_, err) => tcs.TrySetResult(err);

        var badFrame = HostLinkCodec.Encode(new HostLinkFrame(HostLinkFrameType.EvStatus, 0, new byte[] { 0x12 }));
        badFrame[^1] ^= 0xFF; // break CRC
        fake.Inject(badFrame);

        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(HostLinkDecodeErrorCode.CrcMismatch, error.Code);
    }

    private readonly record struct CmdTxAppDataHeader(
        uint From,
        HostLinkAppDataFlags Flags,
        byte[] TeamId,
        uint TeamKeyId);

    private static CmdTxAppDataHeader ParseCmdTxAppDataHeader(ReadOnlySpan<byte> payload)
    {
        Assert.True(payload.Length >= 26);
        var from = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        var flags = (HostLinkAppDataFlags)payload[13];
        var teamId = payload.Slice(14, 8).ToArray();
        var teamKeyId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(22, 4));
        return new CmdTxAppDataHeader(from, flags, teamId, teamKeyId);
    }

    private static HostLinkClient BuildClient(FakeTransport fake)
    {
        var logStore = new LogStore();
        var sessionStore = new SessionStore();
        return new HostLinkClient(NullLogger<HostLinkClient>.Instance, logStore, sessionStore, _ => fake);
    }

    private static byte[] BuildHelloAckPayload(
        HostLinkCapabilities capabilities = HostLinkCapabilities.CapTxMsg |
                                           HostLinkCapabilities.CapConfig |
                                           HostLinkCapabilities.CapStatus)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt16(1);
        writer.WriteUInt16(512);
        writer.WriteUInt32((uint)capabilities);
        writer.WriteString8("TrailMate");
        writer.WriteString8("dev");
        return writer.ToArray();
    }

    private static IReadOnlyList<HostLinkFrame> DecodeWrittenFrames(IEnumerable<byte[]> writes)
    {
        var codec = new HostLinkCodec();
        var frames = new List<HostLinkFrame>();
        foreach (var bytes in writes)
        {
            codec.Append(bytes);
            frames.AddRange(codec.DrainFrames());
        }
        return frames;
    }

    private static byte[] BuildTxResultPayload(uint msgId, bool success)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt32(msgId);
        writer.WriteByte(success ? (byte)1 : (byte)0);
        return writer.ToArray();
    }

    private static byte[] BuildTeamStatePayload(byte flags, uint selfId, byte[] teamId, uint keyId)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteByte(1); // version
        writer.WriteByte(flags);
        writer.WriteUInt16(0); // reserved
        writer.WriteUInt32(selfId);

        var normalizedTeamId = teamId.Length == 8 ? teamId : new byte[8];
        foreach (var b in normalizedTeamId)
            writer.WriteByte(b);

        for (var i = 0; i < 8; i++)
            writer.WriteByte(0); // join_target_id

        writer.WriteUInt32(keyId);
        writer.WriteUInt32(0); // last_event_seq
        writer.WriteUInt32(0); // last_update_s
        writer.WriteUInt16(0); // team_name_len
        writer.WriteByte(0); // member_count
        return writer.ToArray();
    }
}
