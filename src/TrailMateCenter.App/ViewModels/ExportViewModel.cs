using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using TrailMateCenter.Localization;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class ExportViewModel : ObservableObject, ILocalizationAware
{
    private readonly ExportService _exportService;
    private readonly SessionStore _sessionStore;
    private string? _statusKey;
    private object[] _statusArgs = Array.Empty<object>();

    public ExportViewModel(ExportService exportService, SessionStore sessionStore)
    {
        _exportService = exportService;
        _sessionStore = sessionStore;
        ExportMessagesCommand = new AsyncRelayCommand(ExportMessagesAsync);
        ExportEventsCommand = new AsyncRelayCommand(ExportEventsAsync);
    }

    [ObservableProperty]
    private string _exportPath = "export.jsonl";

    [ObservableProperty]
    private ExportFormat _format = ExportFormat.Jsonl;

    [ObservableProperty]
    private DateTimeOffset? _from;

    [ObservableProperty]
    private DateTimeOffset? _to;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public IAsyncRelayCommand ExportMessagesCommand { get; }
    public IAsyncRelayCommand ExportEventsCommand { get; }

    public IReadOnlyList<ExportFormat> Formats { get; } = Enum.GetValues<ExportFormat>();

    private async Task ExportMessagesAsync()
    {
        SetStatus("Status.Export.MessagesInProgress");
        try
        {
            await _exportService.ExportMessagesAsync(_sessionStore, ExportPath, Format, From, To, CancellationToken.None);
            SetStatus("Status.Export.MessagesDone");
        }
        catch (Exception ex)
        {
            SetStatus("Status.Export.Failed", ex.Message);
        }
    }

    private async Task ExportEventsAsync()
    {
        SetStatus("Status.Export.EventsInProgress");
        try
        {
            await _exportService.ExportEventsAsync(_sessionStore, ExportPath, Format, From, To, CancellationToken.None);
            SetStatus("Status.Export.EventsDone");
        }
        catch (Exception ex)
        {
            SetStatus("Status.Export.Failed", ex.Message);
        }
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args ?? Array.Empty<object>();
        StatusMessage = _statusArgs.Length == 0
            ? LocalizationService.Instance.GetString(key)
            : LocalizationService.Instance.Format(key, _statusArgs);
    }

    public void RefreshLocalization()
    {
        if (string.IsNullOrWhiteSpace(_statusKey))
        {
            StatusMessage = string.Empty;
            return;
        }

        StatusMessage = _statusArgs.Length == 0
            ? LocalizationService.Instance.GetString(_statusKey)
            : LocalizationService.Instance.Format(_statusKey, _statusArgs);
    }
}
