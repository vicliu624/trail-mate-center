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

    private static HostLinkClient BuildClient(FakeTransport fake)
    {
        var logStore = new LogStore();
        var sessionStore = new SessionStore();
        return new HostLinkClient(NullLogger<HostLinkClient>.Instance, logStore, sessionStore, _ => fake);
    }

    private static byte[] BuildHelloAckPayload()
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt16(1);
        writer.WriteUInt16(512);
        writer.WriteUInt32((uint)(HostLinkCapabilities.CapTxMsg | HostLinkCapabilities.CapConfig | HostLinkCapabilities.CapStatus));
        writer.WriteString8("TrailMate");
        writer.WriteString8("dev");
        return writer.ToArray();
    }

    private static byte[] BuildTxResultPayload(uint msgId, bool success)
    {
        var writer = new HostLinkBufferWriter();
        writer.WriteUInt32(msgId);
        writer.WriteByte(success ? (byte)1 : (byte)0);
        return writer.ToArray();
    }
}
