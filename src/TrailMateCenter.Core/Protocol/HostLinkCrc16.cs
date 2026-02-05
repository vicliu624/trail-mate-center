namespace TrailMateCenter.Protocol;

public static class HostLinkCrc16
{
    // CRC-16/CCITT-FALSE: poly 0x1021, init 0xFFFF, xorout 0x0000
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        const ushort poly = 0x1021;
        ushort crc = 0xFFFF;

        for (var i = 0; i < data.Length; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ poly);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }

        return crc;
    }
}
