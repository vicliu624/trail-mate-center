namespace TrailMateCenter.Models;

public enum TeamChatType : byte
{
    Text = 1,
    Location = 2,
    Command = 3,
}

public enum TeamLocationSource : byte
{
    None = 0,
    AreaCleared = 1,
    BaseCamp = 2,
    GoodFind = 3,
    Rally = 4,
    Sos = 5,
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

public sealed record TeamLocationPostRequest
{
    public TeamLocationSource Source { get; init; } = TeamLocationSource.None;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public short? AltitudeMeters { get; init; }
    public ushort? AccuracyMeters { get; init; }
    public string? Label { get; init; }
    public uint? To { get; init; }
    public byte Channel { get; init; }
}
