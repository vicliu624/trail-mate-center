using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class LogsViewModel : ObservableObject, ILocalizationAware
{
    private readonly LogStore _logStore;
    private readonly object _allGate = new();
    private readonly List<HostLinkLogEntry> _all = new();
    private readonly SemaphoreSlim _historyLoadGate = new(1, 1);
    private bool _historyFullyLoaded;
    private string? _statusKey;
    private object[] _statusArgs = Array.Empty<object>();

    public LogsViewModel(LogStore logStore)
    {
        _logStore = logStore;
        lock (_allGate)
        {
            foreach (var entry in _logStore.Snapshot())
                _all.Add(entry);
        }

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
        lock (_allGate)
        {
            _all.Add(entry);
        }

        Dispatcher.UIThread.Post(Rebuild);
    }

    partial void OnShowInfoChanged(bool value) => Rebuild();
    partial void OnShowWarningChanged(bool value) => Rebuild();
    partial void OnShowErrorChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        List<HostLinkLogEntry> snapshot;
        lock (_allGate)
        {
            snapshot = _all.ToList();
        }

        Entries.Clear();
        foreach (var entry in snapshot.OrderByDescending(e => e.Timestamp))
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

    public async Task EnsureOlderHistoryLoadedAsync(SqliteStore sqliteStore, CancellationToken cancellationToken)
    {
        await _historyLoadGate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset oldest;
            lock (_allGate)
            {
                if (_historyFullyLoaded || _all.Count == 0)
                    return;

                oldest = _all.Min(entry => entry.Timestamp);
            }

            var older = await sqliteStore.LoadLogsAsync(cancellationToken, beforeExclusive: oldest);
            if (older.Count == 0)
            {
                _historyFullyLoaded = true;
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_allGate)
                {
                    _all.AddRange(older);
                }

                Rebuild();
            });
        }
        finally
        {
            _historyLoadGate.Release();
        }
    }

    private string BuildLogText()
    {
        List<HostLinkLogEntry> snapshot;
        lock (_allGate)
        {
            snapshot = _all.ToList();
        }

        var sb = new StringBuilder();
        foreach (var entry in snapshot.Where(e => MatchesFilter(e.Level)))
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
            SetStatus("Status.Logs.Saved");
        }
        catch (Exception ex)
        {
            SetStatus("Status.Logs.SaveFailed", ex.Message);
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
            SetStatus("Status.Logs.Copied");
        }
        catch (Exception ex)
        {
            SetStatus("Status.Logs.CopyFailed", ex.Message);
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
