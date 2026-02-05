namespace TrailMateCenter.Models;

public sealed record NodeInfoUpdate(
    uint NodeId,
    string? ShortName,
    string? LongName,
    string? UserId,
    byte? Channel,
    DateTimeOffset LastHeard,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters);
