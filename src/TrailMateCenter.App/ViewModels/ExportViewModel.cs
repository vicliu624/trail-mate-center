using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class ExportViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly SessionStore _sessionStore;

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
        StatusMessage = "导出消息中...";
        try
        {
            await _exportService.ExportMessagesAsync(_sessionStore, ExportPath, Format, From, To, CancellationToken.None);
            StatusMessage = "消息导出完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
    }

    private async Task ExportEventsAsync()
    {
        StatusMessage = "导出事件中...";
        try
        {
            await _exportService.ExportEventsAsync(_sessionStore, ExportPath, Format, From, To, CancellationToken.None);
            StatusMessage = "事件导出完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
    }
}
