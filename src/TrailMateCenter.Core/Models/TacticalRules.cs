namespace TrailMateCenter.Models;

public static class TacticalRules
{
    public static TacticalSeverity GetDefaultSeverity(TacticalEventKind kind)
    {
        return kind switch
        {
            TacticalEventKind.ChatText => TacticalSeverity.Info,
            TacticalEventKind.ChatLocation => TacticalSeverity.Notice,
            TacticalEventKind.CommandIssued => TacticalSeverity.Notice,
            TacticalEventKind.TrackUpdate => TacticalSeverity.Info,
            TacticalEventKind.TeamMgmt => TacticalSeverity.Notice,
            TacticalEventKind.PositionUpdate => TacticalSeverity.Info,
            TacticalEventKind.Waypoint => TacticalSeverity.Notice,
            TacticalEventKind.StatusChange => TacticalSeverity.Notice,
            TacticalEventKind.System => TacticalSeverity.Warning,
            _ => TacticalSeverity.Info,
        };
    }
}
