namespace TrailMateCenter.Protocol;

public static class HostLinkConstants
{
    public const byte Magic0 = (byte)'H';
    public const byte Magic1 = (byte)'L';
    public const byte Version = 0x01;
    public const int HeaderSize = 8; // Magic(2) + Version + Type + Seq(2) + Length(2)
    public const int CrcSize = 2;
    public const int MinFrameSize = HeaderSize + CrcSize;
    public const int DefaultMaxPayloadLength = 512;
}
