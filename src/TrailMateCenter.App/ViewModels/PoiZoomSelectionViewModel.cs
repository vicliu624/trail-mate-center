using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TrailMateCenter.Maps;

namespace TrailMateCenter.ViewModels;

public sealed partial class PoiZoomLevelOptionViewModel : ObservableObject
{
    public int Zoom { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isAvailable = true;
    public PoiZoomLevelOptionViewModel(int zoom) => Zoom = zoom;
}

/// <summary>The one editable POI level set shared by both map preparation dialogs.</summary>
public sealed partial class PoiZoomSelectionViewModel : ObservableObject
{
    private bool _updating;
    private int _minimum = 0;
    private int _maximum = 18;
    [ObservableProperty] private int _rangeStart = 16;
    [ObservableProperty] private int _rangeEnd = 18;
    public ObservableCollection<PoiZoomLevelOptionViewModel> Levels { get; } = new();
    public IReadOnlyList<int> AvailableLevels => Enumerable.Range(_minimum, _maximum - _minimum + 1).ToArray();
    public IReadOnlyList<int> EnabledZoomLevels => Levels.Where(l => l.IsAvailable && l.IsSelected).Select(l => l.Zoom).ToArray();
    public string Summary => EnabledZoomLevels.Count == 0 ? "—" : string.Join(", ", EnabledZoomLevels.Select(z => $"z{z}"));
    public event Action? SelectionChanged;

    public PoiZoomSelectionViewModel()
    {
        for (var z = 0; z <= 18; z++)
        {
            var option = new PoiZoomLevelOptionViewModel(z) { IsSelected = z is >= 16 and <= 18 };
            option.PropertyChanged += (_, e) =>
            {
                if (!_updating && e.PropertyName == nameof(PoiZoomLevelOptionViewModel.IsSelected)) NotifySelection();
            };
            Levels.Add(option);
        }
    }

    public void SetBounds(int minimum, int maximum)
    {
        var min = Math.Clamp(Math.Min(minimum, maximum), 0, 18);
        var max = Math.Clamp(Math.Max(minimum, maximum), 0, 18);
        if (_minimum == min && _maximum == max) return;
        _updating = true;
        _minimum = min;
        _maximum = max;
        foreach (var option in Levels)
        {
            option.IsAvailable = option.Zoom >= min && option.Zoom <= max;
            // Bounds filter the effective set without losing the user's choices
            // when they later expand the basemap range again.
        }
        RangeStart = Math.Clamp(RangeStart, min, max);
        RangeEnd = Math.Clamp(RangeEnd, min, max);
        _updating = false;
        OnPropertyChanged(nameof(AvailableLevels));
        NotifySelection();
    }

    public void LoadSelection(IReadOnlyList<int>? explicitLevels, int legacyMin, int legacyMax)
    {
        var options = new PoiIndexOptions { EnabledZoomLevels = explicitLevels, MinZoom = legacyMin, MaxZoom = legacyMax }.Normalize();
        SetSelection(options.EnabledZoomLevels!);
    }

    private void SetSelection(IEnumerable<int> levels)
    {
        var selected = levels.ToHashSet();
        _updating = true;
        foreach (var option in Levels) option.IsSelected = option.IsAvailable && selected.Contains(option.Zoom);
        _updating = false;
        NotifySelection();
    }

    [RelayCommand] private void SelectAll() => SetSelection(AvailableLevels);
    [RelayCommand] private void Clear() => SetSelection(Array.Empty<int>());
    [RelayCommand] private void SelectRange()
    {
        var min = Math.Min(RangeStart, RangeEnd);
        var max = Math.Max(RangeStart, RangeEnd);
        SetSelection(Enumerable.Range(min, max - min + 1));
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(EnabledZoomLevels));
        OnPropertyChanged(nameof(Summary));
        SelectionChanged?.Invoke();
    }
}
