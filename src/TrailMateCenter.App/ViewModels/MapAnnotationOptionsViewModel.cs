using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Maps;

namespace TrailMateCenter.ViewModels;

/// <summary>One editable annotation policy shared by map preparation dialogs.</summary>
public sealed partial class MapAnnotationOptionsViewModel : ObservableObject
{
    private bool _loading;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private MapAnnotationPreset _preset = MapAnnotationPreset.Outdoor;
    [ObservableProperty] private bool _placesEnabled = true;
    [ObservableProperty] private bool _roadsEnabled = true;
    [ObservableProperty] private bool _poisEnabled = true;
    [ObservableProperty] private int _placeMinimumZoom = 1;
    [ObservableProperty] private int _placeMaximumZoom = 18;
    [ObservableProperty] private int _roadMinimumZoom = 7;
    [ObservableProperty] private int _roadMaximumZoom = 18;
    [ObservableProperty] private int _poiMinimumZoom = 10;
    [ObservableProperty] private int _poiMaximumZoom = 18;
    public IReadOnlyList<int> ZoomLevels { get; } = Enumerable.Range(1, 18).ToArray();
    public event Action? Changed;

    public MapAnnotationOptionsViewModel()
    {
        PropertyChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
    }

    public MapAnnotationOptions? ToOptions()
    {
        if (!Enabled) return null;
        static MapAnnotationZoomRange Range(int a, int b) => new(Math.Clamp(Math.Min(a, b), 1, 18), Math.Clamp(Math.Max(a, b), 1, 18));
        return new()
        {
            Preset = Preset,
            PlacesEnabled = PlacesEnabled,
            RoadsEnabled = RoadsEnabled,
            PoisEnabled = PoisEnabled,
            PlaceZooms = Range(PlaceMinimumZoom, PlaceMaximumZoom),
            RoadZooms = Range(RoadMinimumZoom, RoadMaximumZoom),
            PoiZooms = Range(PoiMinimumZoom, PoiMaximumZoom),
        };
    }

    public void Load(MapAnnotationOptions? options)
    {
        _loading = true;
        Enabled = options is not null;
        var policy = options ?? new();
        Preset = policy.Preset;
        PlacesEnabled = policy.PlacesEnabled; RoadsEnabled = policy.RoadsEnabled; PoisEnabled = policy.PoisEnabled;
        PlaceMinimumZoom = policy.PlaceZooms.Minimum; PlaceMaximumZoom = policy.PlaceZooms.Maximum;
        RoadMinimumZoom = policy.RoadZooms.Minimum; RoadMaximumZoom = policy.RoadZooms.Maximum;
        PoiMinimumZoom = policy.PoiZooms.Minimum; PoiMaximumZoom = policy.PoiZooms.Maximum;
        _loading = false;
        Changed?.Invoke();
    }
}
