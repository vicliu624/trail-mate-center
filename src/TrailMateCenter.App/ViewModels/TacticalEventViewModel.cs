using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class TacticalEventViewModel : ObservableObject
{
    public TacticalEventViewModel(TacticalEvent ev)
    {
        Timestamp = ev.Timestamp;
        Kind = ev.Kind;
        Title = ev.Title;
        Detail = ev.Detail;
        SubjectLabel = ev.SubjectLabel ?? string.Empty;
        SubjectId = ev.SubjectId;
        DefaultSeverity = ev.Severity;
        Severity = ev.Severity;
        Latitude = ev.Latitude;
        Longitude = ev.Longitude;
    }

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private TacticalEventKind _kind;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _subjectLabel = string.Empty;

    [ObservableProperty]
    private uint? _subjectId;

    [ObservableProperty]
    private TacticalSeverity _severity;

    public TacticalSeverity DefaultSeverity { get; }

    [ObservableProperty]
    private double? _latitude;

    [ObservableProperty]
    private double? _longitude;

    public string SeverityColor => Severity switch
    {
        TacticalSeverity.Info => "#3B82F6",
        TacticalSeverity.Notice => "#10B981",
        TacticalSeverity.Warning => "#F59E0B",
        TacticalSeverity.Critical => "#EF4444",
        _ => "#6B7280",
    };

    public void ApplySeverity(TacticalSeverity severity)
    {
        Severity = severity;
        OnPropertyChanged(nameof(SeverityColor));
    }
}
