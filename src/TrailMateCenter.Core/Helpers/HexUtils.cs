namespace TrailMateCenter.Helpers;

public static class HexUtils
{
    public static byte[] FromHex(string hex)
    {
        hex = hex.Replace(" ", string.Empty);
        if (hex.Length % 2 != 0)
            throw new FormatException("Hex length must be even");

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    public static string ToHex(ReadOnlySpan<byte> data)
    {
        return Convert.ToHexString(data);
    }
}
