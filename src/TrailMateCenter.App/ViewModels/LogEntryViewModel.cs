using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class LogEntryViewModel : ObservableObject
{
    public LogEntryViewModel(HostLinkLogEntry entry)
    {
        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Message = entry.Message;
        RawCode = entry.RawCode;
    }

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private LogLevel _level;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _rawCode;
}
