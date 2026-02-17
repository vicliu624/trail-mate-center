namespace TrailMateCenter.Models;

public enum TacticalSeverity
{
    Info = 0,
    Notice = 1,
    Warning = 2,
    Critical = 3,
}

public enum TacticalEventKind
{
    Unknown = 0,
    ChatText = 1,
    ChatLocation = 2,
    CommandIssued = 3,
    TrackUpdate = 4,
    TeamMgmt = 5,
    PositionUpdate = 6,
    Waypoint = 7,
    StatusChange = 8,
    System = 9,
    Telemetry = 10,
}

public sealed record TacticalEvent(
    DateTimeOffset Timestamp,
    TacticalSeverity Severity,
    TacticalEventKind Kind,
    string Title,
    string Detail,
    uint? SubjectId,
    string? SubjectLabel,
    double? Latitude,
    double? Longitude);

public enum PositionSource
{
    DeviceGps,
    TeamTrack,
    TeamChatLocation,
    TeamPositionApp,
    TeamWaypointApp,
}

public sealed record PositionUpdate(
    DateTimeOffset Timestamp,
    uint SourceId,
    double Latitude,
    double Longitude,
    double? AltitudeMeters,
    double? AccuracyMeters,
    PositionSource Source,
    string? Label,
    byte? TeamLocationMarkerRaw = null,
    TeamLocationSource? TeamLocationMarker = null);
