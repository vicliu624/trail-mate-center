using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class EventsViewModel : ObservableObject
{
    private readonly SessionStore _sessionStore;
    private readonly List<EventItemViewModel> _allEvents = new();
    private readonly SemaphoreSlim _historyLoadGate = new(1, 1);
    private bool _historyFullyLoaded;

    public EventsViewModel(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
        foreach (var ev in _sessionStore.SnapshotEvents().OrderByDescending(e => e.Timestamp))
        {
            var item = new EventItemViewModel(ev);
            _allEvents.Add(item);
            if (MatchesFilter(item))
                Events.Add(item);
        }
        _sessionStore.EventAdded += OnEventAdded;
    }

    public ObservableCollection<EventItemViewModel> Events { get; } = new();

    [ObservableProperty]
    private bool _filterFromIs;

    private void OnEventAdded(object? sender, HostLinkEvent ev)
    {
        Dispatcher.UIThread.Post(() => AddEventItem(new EventItemViewModel(ev)));
    }

    partial void OnFilterFromIsChanged(bool value)
    {
        ApplyFilters();
    }

    private void AddEventItem(EventItemViewModel item)
    {
        _allEvents.Insert(0, item);
        if (MatchesFilter(item))
            Events.Insert(0, item);
    }

    private void ApplyFilters()
    {
        Events.Clear();
        foreach (var item in _allEvents)
        {
            if (MatchesFilter(item))
                Events.Add(item);
        }
    }

    private bool MatchesFilter(EventItemViewModel item)
    {
        if (FilterFromIs && !item.IsFromIs)
            return false;
        return true;
    }

    public async Task EnsureOlderHistoryLoadedAsync(SqliteStore sqliteStore, CancellationToken cancellationToken)
    {
        if (_historyFullyLoaded || _allEvents.Count == 0)
            return;

        await _historyLoadGate.WaitAsync(cancellationToken);
        try
        {
            if (_historyFullyLoaded || _allEvents.Count == 0)
                return;

            var oldest = _allEvents.Min(item => item.Timestamp);
            var older = await sqliteStore.LoadEventsAsync(cancellationToken, beforeExclusive: oldest);
            if (older.Count == 0)
            {
                _historyFullyLoaded = true;
                return;
            }

            var olderItems = older
                .Select(ev => new EventItemViewModel(ev))
                .OrderByDescending(item => item.Timestamp)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in olderItems)
                {
                    _allEvents.Add(item);
                    if (MatchesFilter(item))
                        Events.Add(item);
                }
            });
        }
        finally
        {
            _historyLoadGate.Release();
        }
    }

    public void RefreshLocalization()
    {
        foreach (var item in _allEvents)
        {
            item.RefreshLocalization();
        }
    }
}
