using Google.Protobuf;
using SkiaSharp;

internal sealed record VectorFeature(ulong Id, int Type, Dictionary<string, string> Tags, uint[] Geometry);
internal sealed record VectorLayer(string Name, int Extent, List<VectorFeature> Features);

// Mapbox Vector Tile v2 protobuf; coordinates retain the supplied tile buffer.
internal static class VectorTile
{
    public static List<VectorLayer> Read(byte[] bytes)
    {
        var layers = new List<VectorLayer>(); using var tile = new CodedInputStream(bytes); uint tag;
        while ((tag = tile.ReadTag()) != 0)
            if (tag == 26) layers.Add(ReadLayer(tile.ReadBytes().ToByteArray())); else tile.SkipLastField();
        return layers;
    }
    private static VectorLayer ReadLayer(byte[] bytes)
    {
        using var input = new CodedInputStream(bytes); var name = ""; var extent = 4096; uint tag;
        var keys = new List<string>(); var values = new List<string>(); var features = new List<byte[]>();
        while ((tag = input.ReadTag()) != 0)
            switch (tag)
            {
                case 10: name = input.ReadString(); break;
                case 18: features.Add(input.ReadBytes().ToByteArray()); break;
                case 26: keys.Add(input.ReadString()); break;
                case 34: values.Add(Value(input.ReadBytes().ToByteArray())); break;
                case 40: extent = checked((int)input.ReadUInt32()); break;
                case 120: if (input.ReadUInt32() is not (1 or 2)) throw new InvalidDataException("Unknown MVT version."); break;
                default: input.SkipLastField(); break;
            }
        if (extent is < 1 or > 65536 || name.Length == 0) throw new InvalidDataException("Invalid MVT layer.");
        return new(name, extent, features.Select(b => Feature(b, keys, values)).ToList());
    }
    private static string Value(byte[] bytes)
    {
        using var p = new CodedInputStream(bytes); uint tag; var result = "";
        while ((tag = p.ReadTag()) != 0)
            result = tag switch
            {
                10 => p.ReadString(),
                21 => p.ReadFloat().ToString(System.Globalization.CultureInfo.InvariantCulture),
                25 => p.ReadDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                32 => p.ReadInt64().ToString(),
                40 => p.ReadUInt64().ToString(),
                48 => p.ReadSInt64().ToString(),
                56 => p.ReadBool() ? "true" : "false",
                _ => Skip(p, result)
            };
        return result;
    }
    private static string Skip(CodedInputStream p, string result) { p.SkipLastField(); return result; }
    private static uint[] Packed(ByteString bytes)
    {
        using var p = bytes.CreateCodedInput(); var result = new List<uint>();
        while (!p.IsAtEnd) result.Add(p.ReadUInt32()); return result.ToArray();
    }
    private static VectorFeature Feature(byte[] bytes, List<string> keys, List<string> values)
    {
        using var p = new CodedInputStream(bytes); ulong id = 0; int type = 0; uint tag; uint[] tags = [], geometry = [];
        while ((tag = p.ReadTag()) != 0)
            switch (tag)
            {
                case 8: id = p.ReadUInt64(); break;
                case 18: tags = Packed(p.ReadBytes()); break;
                case 24: type = (int)p.ReadUInt32(); break;
                case 34: geometry = Packed(p.ReadBytes()); break;
                default: p.SkipLastField(); break;
            }
        if (tags.Length % 2 != 0) throw new InvalidDataException("Invalid MVT tags.");
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < tags.Length; i += 2)
        {
            if (tags[i] >= keys.Count || tags[i + 1] >= values.Count) throw new InvalidDataException("MVT tag index outside dictionary.");
            properties[keys[(int)tags[i]]] = values[(int)tags[i + 1]];
        }
        return new(id, type, properties, geometry);
    }
    internal static List<List<SKPoint>> Parts(uint[] commands)
    {
        var parts = new List<List<SKPoint>>(); List<SKPoint>? current = null; int x = 0, y = 0;
        for (var at = 0; at < commands.Length;)
        {
            var command = commands[at++]; var op = command & 7; var count = command >> 3;
            if (count == 0 || op is not (1 or 2 or 7)) throw new InvalidDataException("Invalid MVT geometry command.");
            if (op == 7) { if (count != 1 || current is null) throw new InvalidDataException("Invalid close command."); current.Add(current[0]); continue; }
            if ((ulong)at + count * 2UL > (ulong)commands.Length) throw new InvalidDataException("Truncated MVT geometry.");
            for (uint i = 0; i < count; i++)
            {
                var dx = commands[at++]; var dy = commands[at++];
                var signedX = (int)(dx >> 1) ^ -(int)(dx & 1); var signedY = (int)(dy >> 1) ^ -(int)(dy & 1);
                x = checked(x + signedX); y = checked(y + signedY);
                if (op == 1) { current = new(); parts.Add(current); }
                if (current is null) throw new InvalidDataException("Line without move."); current.Add(new(x, y));
            }
        }
        return parts;
    }
}
