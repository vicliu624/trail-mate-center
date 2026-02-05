using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace TrailMateCenter.Protocol;

internal ref struct HostLinkSpanReader
{
    private ReadOnlySpan<byte> _span;

    public HostLinkSpanReader(ReadOnlySpan<byte> span)
    {
        _span = span;
    }

    public bool TryReadByte(out byte value)
    {
        if (_span.Length < 1)
        {
            value = 0;
            return false;
        }
        value = _span[0];
        _span = _span[1..];
        return true;
    }

    public bool TryReadSByte(out sbyte value)
    {
        if (!TryReadByte(out var b))
        {
            value = 0;
            return false;
        }
        value = unchecked((sbyte)b);
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (_span.Length < 2)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt16LittleEndian(_span.Slice(0, 2));
        _span = _span[2..];
        return true;
    }

    public bool TryReadInt16(out short value)
    {
        if (_span.Length < 2)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt16LittleEndian(_span.Slice(0, 2));
        _span = _span[2..];
        return true;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (_span.Length < 4)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(0, 4));
        _span = _span[4..];
        return true;
    }

    public bool TryReadInt32(out int value)
    {
        if (_span.Length < 4)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(_span.Slice(0, 4));
        _span = _span[4..];
        return true;
    }

    public bool TryReadUInt64(out ulong value)
    {
        if (_span.Length < 8)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(0, 8));
        _span = _span[8..];
        return true;
    }

    public bool TryReadInt64(out long value)
    {
        if (_span.Length < 8)
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt64LittleEndian(_span.Slice(0, 8));
        _span = _span[8..];
        return true;
    }

    public bool TryReadString(out string value)
    {
        value = string.Empty;
        if (!TryReadUInt16(out var length))
            return false;
        if (_span.Length < length)
            return false;
        value = Encoding.UTF8.GetString(_span.Slice(0, length));
        _span = _span[length..];
        return true;
    }

    public bool TryReadString8(out string value)
    {
        value = string.Empty;
        if (!TryReadByte(out var length))
            return false;
        if (_span.Length < length)
            return false;
        value = Encoding.ASCII.GetString(_span.Slice(0, length));
        _span = _span[length..];
        return true;
    }

    public bool TryReadBytes(int length, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (length < 0 || _span.Length < length)
            return false;
        value = _span.Slice(0, length).ToArray();
        _span = _span[length..];
        return true;
    }

    public ReadOnlySpan<byte> Remaining => _span;
}

public sealed class HostLinkBufferWriter
{
    private readonly ArrayBufferWriter<byte> _writer = new();

    public void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }

    public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    public void WriteUInt16(ushort value)
    {
        var span = _writer.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        _writer.Advance(2);
    }

    public void WriteUInt32(uint value)
    {
        var span = _writer.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _writer.Advance(4);
    }

    public void WriteInt32(int value)
    {
        var span = _writer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        _writer.Advance(4);
    }

    public void WriteUInt64(ulong value)
    {
        var span = _writer.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _writer.Advance(8);
    }

    public void WriteInt64(long value)
    {
        var span = _writer.GetSpan(8);
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        _writer.Advance(8);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt16((ushort)bytes.Length);
        bytes.CopyTo(_writer.GetSpan(bytes.Length));
        _writer.Advance(bytes.Length);
    }

    public void WriteString8(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        if (bytes.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(value), "String too long");
        WriteByte((byte)bytes.Length);
        bytes.CopyTo(_writer.GetSpan(bytes.Length));
        _writer.Advance(bytes.Length);
    }

    public byte[] ToArray() => _writer.WrittenSpan.ToArray();
}
