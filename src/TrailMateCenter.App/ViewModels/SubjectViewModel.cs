using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public enum LinkStatus
{
    Online,
    Weak,
    Offline,
}

public sealed partial class SubjectViewModel : ObservableObject, ILocalizationAware
{
    public SubjectViewModel(uint id)
    {
        Id = id;
        DisplayId = $"0x{id:X8}";
    }

    public uint Id { get; }
    public string DisplayId { get; }

    [ObservableProperty]
    private DateTimeOffset _lastSeen;

    [ObservableProperty]
    private LinkStatus _status;

    [ObservableProperty]
    private double? _latitude;

    [ObservableProperty]
    private double? _longitude;

    [ObservableProperty]
    private string _shortName = string.Empty;

    [ObservableProperty]
    private string _longName = string.Empty;

    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private string _teamName = string.Empty;

    [ObservableProperty]
    private string _telemetryText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _telemetryTimestamp;

    public bool HasTelemetry => !string.IsNullOrWhiteSpace(TelemetryText);
    public bool HasTeam => !string.IsNullOrWhiteSpace(TeamName);

    public string TelemetryTimeText => TelemetryTimestamp.HasValue
        ? TelemetryTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : LocalizationService.Instance.GetString("Common.Unknown");

    public string StatusText => Status switch
    {
        LinkStatus.Online => LocalizationService.Instance.GetString("Status.Link.Online"),
        LinkStatus.Weak => LocalizationService.Instance.GetString("Status.Link.Weak"),
        LinkStatus.Offline => LocalizationService.Instance.GetString("Status.Link.Offline"),
        _ => LocalizationService.Instance.GetString("Status.Link.Unknown"),
    };

    public string StatusColor => Status switch
    {
        LinkStatus.Online => "#22C55E",
        LinkStatus.Weak => "#F59E0B",
        LinkStatus.Offline => "#EF4444",
        _ => "#6B7280",
    };

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ShortName))
                return ShortName;
            if (!string.IsNullOrWhiteSpace(LongName))
                return LongName;
            return DisplayId;
        }
    }

    public string DisplayDetail
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LongName) && !string.Equals(LongName, ShortName, StringComparison.Ordinal))
                return LongName;
            return DisplayId;
        }
    }

    public string LastSeenText => LastSeen == default
        ? LocalizationService.Instance.GetString("Common.Unknown")
        : LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string LocationText
    {
        get
        {
            if (Latitude.HasValue && Longitude.HasValue)
                return LocalizationService.Instance.Format("Status.Location.Format", Latitude.Value, Longitude.Value);
            return LocalizationService.Instance.GetString("Status.Location.Unknown");
        }
    }

    public void UpdateStatus(LinkStatus status)
    {
        Status = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    public void ApplyNodeInfo(NodeInfoUpdate info)
    {
        if (!string.IsNullOrWhiteSpace(info.ShortName))
            ShortName = info.ShortName;
        if (!string.IsNullOrWhiteSpace(info.LongName))
            LongName = info.LongName;
        if (!string.IsNullOrWhiteSpace(info.UserId))
            UserId = info.UserId;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayDetail));
    }

    public void ApplyTelemetry(string summary, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;
        TelemetryText = summary;
        TelemetryTimestamp = timestamp;
        OnPropertyChanged(nameof(HasTelemetry));
        OnPropertyChanged(nameof(TelemetryTimeText));
    }

    partial void OnShortNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayDetail));
    }

    partial void OnLongNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayDetail));
    }

    partial void OnLastSeenChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(LastSeenText));
    }

    partial void OnLatitudeChanged(double? value)
    {
        OnPropertyChanged(nameof(LocationText));
    }

    partial void OnLongitudeChanged(double? value)
    {
        OnPropertyChanged(nameof(LocationText));
    }

    partial void OnTeamNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasTeam));
    }

    partial void OnTelemetryTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasTelemetry));
    }

    partial void OnTelemetryTimestampChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(TelemetryTimeText));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastSeenText));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(TelemetryTimeText));
    }
}
