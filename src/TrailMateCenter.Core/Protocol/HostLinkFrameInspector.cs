using System.Buffers.Binary;
using TrailMateCenter.Models;

namespace TrailMateCenter.Protocol;

public sealed record RawFrameSegment(RawFrameSegmentKind Kind, string Label, byte[] Bytes);

public enum RawFrameSegmentKind
{
    Sof,
    Version,
    Type,
    Seq,
    Length,
    Payload,
    Crc,
    Extra,
}

public static class HostLinkFrameInspector
{
    public static IReadOnlyList<RawFrameSegment> ParseSegments(ReadOnlySpan<byte> data)
    {
        var segments = new List<RawFrameSegment>();
        if (data.IsEmpty)
            return segments;

        var offset = 0;
        if (data.Length >= 2)
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Sof, "SOF", data.Slice(0, 2).ToArray()));
            offset = 2;
        }
        else
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Extra, "DATA", data.ToArray()));
            return segments;
        }

        if (data.Length >= offset + 1)
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Version, "VER", data.Slice(offset, 1).ToArray()));
            offset += 1;
        }

        if (data.Length >= offset + 1)
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Type, "TYPE", data.Slice(offset, 1).ToArray()));
            offset += 1;
        }

        if (data.Length >= offset + 2)
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Seq, "SEQ", data.Slice(offset, 2).ToArray()));
            offset += 2;
        }

        ushort payloadLength = 0;
        if (data.Length >= offset + 2)
        {
            payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Length, "LEN", data.Slice(offset, 2).ToArray()));
            offset += 2;
        }

        var remaining = data.Length - offset;
        if (remaining <= 0)
            return segments;

        if (remaining >= HostLinkConstants.CrcSize)
        {
            var payloadAvailable = Math.Max(0, remaining - HostLinkConstants.CrcSize);
            var payloadSize = (int)Math.Min(payloadLength, payloadAvailable);
            if (payloadSize > 0)
            {
                segments.Add(new RawFrameSegment(RawFrameSegmentKind.Payload, "PAYLOAD", data.Slice(offset, payloadSize).ToArray()));
                offset += payloadSize;
                remaining = data.Length - offset;
            }

            if (remaining >= HostLinkConstants.CrcSize)
            {
                segments.Add(new RawFrameSegment(RawFrameSegmentKind.Crc, "CRC", data.Slice(data.Length - HostLinkConstants.CrcSize).ToArray()));
                var extraLen = data.Length - HostLinkConstants.CrcSize - offset;
                if (extraLen > 0)
                {
                    segments.Add(new RawFrameSegment(RawFrameSegmentKind.Extra, "EXTRA", data.Slice(offset, extraLen).ToArray()));
                }
                return segments;
            }
        }

        if (remaining > 0)
        {
            segments.Add(new RawFrameSegment(RawFrameSegmentKind.Extra, "DATA", data.Slice(offset, remaining).ToArray()));
        }

        return segments;
    }
}
