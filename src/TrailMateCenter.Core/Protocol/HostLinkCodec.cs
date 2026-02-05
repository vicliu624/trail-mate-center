using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TrailMateCenter.Models;

namespace TrailMateCenter.Protocol;

public sealed class HostLinkCodec
{
    private readonly List<byte> _buffer = new();
    private readonly int _maxPayloadLength;

    public HostLinkCodec(int maxPayloadLength = HostLinkConstants.DefaultMaxPayloadLength)
    {
        _maxPayloadLength = maxPayloadLength;
    }

    public event EventHandler<HostLinkDecodeError>? DecodeError;
    public event EventHandler<HostLinkRawFrameData>? RawFrameDecoded;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        _buffer.AddRange(data.ToArray());
    }

    public IEnumerable<HostLinkFrame> DrainFrames()
    {
        while (_buffer.Count >= HostLinkConstants.MinFrameSize)
        {
            if (_buffer[0] != HostLinkConstants.Magic0)
            {
                _buffer.RemoveAt(0);
                DecodeError?.Invoke(this, new HostLinkDecodeError(
                    HostLinkDecodeErrorCode.InvalidSof,
                    "SOF mismatch while resyncing"));
                continue;
            }
            if (_buffer.Count < 2)
                yield break;
            if (_buffer[1] != HostLinkConstants.Magic1)
            {
                _buffer.RemoveAt(0);
                DecodeError?.Invoke(this, new HostLinkDecodeError(
                    HostLinkDecodeErrorCode.InvalidSof,
                    "SOF mismatch while resyncing"));
                continue;
            }

            if (_buffer.Count < HostLinkConstants.HeaderSize)
                yield break;

            var version = _buffer[2];
            var type = (HostLinkFrameType)_buffer[3];
            var seq = BinaryPrimitives.ReadUInt16LittleEndian(CollectionsMarshal.AsSpan(_buffer).Slice(4, 2));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(CollectionsMarshal.AsSpan(_buffer).Slice(6, 2));

            if (version != HostLinkConstants.Version)
            {
                _buffer.RemoveAt(0);
                DecodeError?.Invoke(this, new HostLinkDecodeError(
                    HostLinkDecodeErrorCode.InvalidSof,
                    $"Protocol version mismatch {version}"));
                continue;
            }

            if (length > _maxPayloadLength)
            {
                _buffer.RemoveAt(0);
                DecodeError?.Invoke(this, new HostLinkDecodeError(
                    HostLinkDecodeErrorCode.LengthTooLarge,
                    $"Payload length {length} exceeds max {_maxPayloadLength}"));
                continue;
            }

            var totalLength = HostLinkConstants.HeaderSize + length + HostLinkConstants.CrcSize;
            if (_buffer.Count < totalLength)
                yield break;

            var frameBytes = _buffer.GetRange(0, totalLength).ToArray();
            _buffer.RemoveRange(0, totalLength);

            var crcExpected = BinaryPrimitives.ReadUInt16LittleEndian(frameBytes.AsSpan(totalLength - 2, 2));
            var crcActual = HostLinkCrc16.Compute(frameBytes.AsSpan(0, totalLength - HostLinkConstants.CrcSize));
            if (crcExpected != crcActual)
            {
                RawFrameDecoded?.Invoke(this, new HostLinkRawFrameData(
                    frameBytes,
                    RawFrameStatus.CrcMismatch,
                    $"expected={crcExpected:X4} actual={crcActual:X4}"));
                DecodeError?.Invoke(this, new HostLinkDecodeError(
                    HostLinkDecodeErrorCode.CrcMismatch,
                    $"CRC mismatch expected={crcExpected:X4} actual={crcActual:X4}",
                    frameBytes));
                continue;
            }

            RawFrameDecoded?.Invoke(this, new HostLinkRawFrameData(
                frameBytes,
                RawFrameStatus.Ok,
                null));

            var payload = length == 0 ? ReadOnlyMemory<byte>.Empty : frameBytes.AsMemory(HostLinkConstants.HeaderSize, length);
            yield return new HostLinkFrame(type, seq, payload, version);
        }
    }

    public static byte[] Encode(HostLinkFrame frame)
    {
        var payloadLength = frame.Payload.Length;
        var totalLength = HostLinkConstants.HeaderSize + payloadLength + HostLinkConstants.CrcSize;
        var buffer = new byte[totalLength];

        buffer[0] = HostLinkConstants.Magic0;
        buffer[1] = HostLinkConstants.Magic1;
        buffer[2] = frame.Version;
        buffer[3] = (byte)frame.Type;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), frame.Seq);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), (ushort)payloadLength);

        if (payloadLength > 0)
            frame.Payload.Span.CopyTo(buffer.AsSpan(HostLinkConstants.HeaderSize, payloadLength));

        var crc = HostLinkCrc16.Compute(buffer.AsSpan(0, totalLength - HostLinkConstants.CrcSize));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(totalLength - 2, 2), crc);

        return buffer;
    }
}
