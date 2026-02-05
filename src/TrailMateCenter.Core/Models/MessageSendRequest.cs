namespace TrailMateCenter.Models;

public sealed record MessageSendRequest
{
    public uint ToId { get; init; }
    public byte Channel { get; init; }
    public byte Flags { get; init; }
    public string Text { get; init; } = string.Empty;
}
