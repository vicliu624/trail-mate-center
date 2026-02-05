namespace TrailMateCenter.Models;

public enum TeamChatType : byte
{
    Text = 1,
    Location = 2,
    Command = 3,
}

public enum TeamCommandType : byte
{
    RallyTo = 1,
    MoveTo = 2,
    Hold = 3,
}

public sealed record TeamCommandRequest
{
    public TeamCommandType CommandType { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public ushort RadiusMeters { get; init; } = 50;
    public byte Priority { get; init; }
    public string? Note { get; init; }
    public uint? To { get; init; }
    public byte Channel { get; init; }
}
