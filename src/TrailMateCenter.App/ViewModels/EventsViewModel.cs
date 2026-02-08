using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class EventsViewModel : ObservableObject
{
    private readonly SessionStore _sessionStore;
    private readonly List<EventItemViewModel> _allEvents = new();

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
}
