using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TrailMateCenter.Models;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly LogStore _logStore;
    private readonly List<HostLinkLogEntry> _all = new();

    public LogsViewModel(LogStore logStore)
    {
        _logStore = logStore;
        foreach (var entry in _logStore.Snapshot())
            _all.Add(entry);

        _logStore.EntryAdded += OnLogAdded;
        SaveLogsCommand = new AsyncRelayCommand(SaveLogsAsync);
        CopyLogsCommand = new AsyncRelayCommand(CopyLogsAsync);
        Rebuild();
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();

    public IAsyncRelayCommand SaveLogsCommand { get; }
    public IAsyncRelayCommand CopyLogsCommand { get; }

    [ObservableProperty]
    private string _exportPath = "logs.txt";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private bool _showWarning = true;

    [ObservableProperty]
    private bool _showError = true;

    private void OnLogAdded(object? sender, HostLinkLogEntry entry)
    {
        _all.Add(entry);
        Dispatcher.UIThread.Post(Rebuild);
    }

    partial void OnShowInfoChanged(bool value) => Rebuild();
    partial void OnShowWarningChanged(bool value) => Rebuild();
    partial void OnShowErrorChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        Entries.Clear();
        foreach (var entry in _all.OrderByDescending(e => e.Timestamp))
        {
            if (!MatchesFilter(entry.Level))
                continue;
            Entries.Add(new LogEntryViewModel(entry));
        }
    }

    private bool MatchesFilter(LogLevel level)
    {
        return level switch
        {
            LogLevel.Information => ShowInfo,
            LogLevel.Warning => ShowWarning,
            LogLevel.Error or LogLevel.Critical => ShowError,
            _ => true,
        };
    }

    private string BuildLogText()
    {
        var sb = new StringBuilder();
        foreach (var entry in _all.Where(e => MatchesFilter(e.Level)))
        {
            sb.AppendLine($"{entry.Timestamp:O} [{entry.Level}] {entry.Message}");
        }
        return sb.ToString();
    }

    private async Task SaveLogsAsync()
    {
        try
        {
            var text = BuildLogText();
            await File.WriteAllTextAsync(ExportPath, text);
            StatusMessage = "日志已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    private async Task CopyLogsAsync()
    {
        try
        {
            var text = BuildLogText();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
            StatusMessage = "日志已复制";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败: {ex.Message}";
        }
    }
}
