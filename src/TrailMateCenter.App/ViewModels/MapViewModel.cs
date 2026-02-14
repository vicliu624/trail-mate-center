using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Fetcher;
using Mapsui.Tiling.Layers;
using Mapsui.Tiling.Rendering;
using Mapsui.Projections;
using BruTile;
using BruTile.Cache;
using BruTile.FileSystem;
using BruTile.Predefined;
using NetTopologySuite.Geometries;
using Mapsui.Nts;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using TrailMateCenter.Localization;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed class MapViewModel : INotifyPropertyChanged
{
    private readonly object _gate = new();
    private readonly List<MapSample> _samples = new();
    private readonly List<WaypointSample> _waypoints = new();
    private readonly MemoryLayer _pointLayer;
    private readonly MemoryLayer _trackLayer;
    private readonly MemoryLayer _clusterLayer;
    private readonly MemoryLayer _waypointLayer;
    private readonly Action<Mapsui.Logging.LogLevel, string, Exception?> _mapsuiLogSink;
    private readonly Dictionary<ContourKey, TileLayer> _contourLayers = new();
    private readonly ContourTileService _contourService;
    private ITileSource? _osmTileSource;
    private ContourSettings _contourSettings = new();
    private CancellationTokenSource? _contourDebounce;
    private int _currentZoom;
    private string _currentZoomText = string.Empty;
    private string _currentContourText = string.Empty;
    private bool _showLogs;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MapViewModel()
    {
        Map = new Map
        {
            CRS = "EPSG:3857",
            BackColor = Color.FromArgb(255, 11, 15, 20),
        };

        _contourService = new ContourTileService(OnContourTilesUpdated, LogContourMessage);
        _mapsuiLogSink = OnMapsuiLog;
        Mapsui.Logging.Logger.LogDelegate = _mapsuiLogSink;

        var tileLayer = CreateOsmLayer();
        tileLayer.Opacity = 0.8f;
        Map.Layers.Add(tileLayer);

        foreach (var contourLayer in CreateContourLayers())
        {
            Map.Layers.Add(contourLayer);
        }

        _trackLayer = new MemoryLayer { Name = "tracks" };
        _pointLayer = new MemoryLayer { Name = "points" };
        _clusterLayer = new MemoryLayer { Name = "clusters" };
        _waypointLayer = new MemoryLayer { Name = "waypoints" };

        Map.Layers.Add(_trackLayer);
        Map.Layers.Add(_clusterLayer);
        Map.Layers.Add(_pointLayer);
        Map.Layers.Add(_waypointLayer);

        Map.Navigator.ViewportChanged += (_, _) => OnViewportChanged();
        UpdateZoomInfo();
    }

    public Map Map { get; }

    public bool FollowLatest { get; set; } = true;
    public bool EnableCluster { get; set; } = true;
    public int ClusterRadiusPx { get; set; } = 60;
    public int CurrentZoom => _currentZoom;
    public string CurrentZoomText => _currentZoomText;
    public string CurrentContourText => _currentContourText;
    public ObservableCollection<MapLogEntry> MapLogEntries { get; } = new();

    public void RefreshLocalization()
    {
        UpdateZoomInfo();
    }

    public void AddPoint(uint? fromId, double lat, double lon)
    {
        var id = fromId ?? 0;
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var point = new MPoint(x, y);

        lock (_gate)
        {
            _samples.Add(new MapSample(id, point));
            if (_samples.Count > 2000)
                _samples.RemoveAt(0);
        }

        RefreshLayers();

        if (FollowLatest)
        {
            Map.Navigator.CenterOn(point, 0, null);
        }
    }

    public void AddWaypoint(uint? fromId, double lat, double lon, string? label)
    {
        var id = fromId ?? 0;
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var point = new MPoint(x, y);

        lock (_gate)
        {
            _waypoints.Add(new WaypointSample(id, point, label));
            if (_waypoints.Count > 500)
                _waypoints.RemoveAt(0);
        }

        RefreshLayers();

        if (FollowLatest)
        {
            Map.Navigator.CenterOn(point, 0, null);
        }
    }

    public void FocusOn(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        Map.Navigator.CenterOn(new MPoint(x, y), 0, null);
    }

    public void Refresh() => RefreshLayers();

    public void UpdateContourSettings(ContourSettings settings)
    {
        _contourSettings = settings ?? new ContourSettings();
        _contourService.UpdateSettings(_contourSettings);
        var zoom = GetCurrentZoom();
        UpdateContourVisibility(zoom);
        DebounceContourQueue();
        UpdateZoomInfo(zoom);
    }

    public void SetLogVisibility(bool enabled)
    {
        var settings = Mapsui.Logging.Logger.Settings;
        settings.LogMapEvents = enabled;
        settings.LogWidgetEvents = enabled;
        settings.LogFlingEvents = false;
        _showLogs = enabled;
        if (enabled)
        {
            AddMapLog(Mapsui.Logging.LogLevel.Information, "Logging enabled", null, force: true);
        }
        else
        {
            MapLogEntries.Clear();
        }
        Map.Refresh(ChangeType.Discrete);
    }

    private void OnMapsuiLog(Mapsui.Logging.LogLevel level, string message, Exception? exception)
    {
        AddMapLog(level, message, exception);
    }

    private void LogContourMessage(ContourLogLevel level, string message)
    {
        var mapsuiLevel = level switch
        {
            ContourLogLevel.Error => Mapsui.Logging.LogLevel.Error,
            ContourLogLevel.Warning => Mapsui.Logging.LogLevel.Warning,
            _ => Mapsui.Logging.LogLevel.Information,
        };
        AddMapLog(mapsuiLevel, $"Contours: {message}", null);
    }

    private void AddMapLog(Mapsui.Logging.LogLevel level, string message, Exception? exception, bool force = false)
    {
        if (!_showLogs && !force)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddMapLog(level, message, exception, force));
            return;
        }

        var color = level switch
        {
            Mapsui.Logging.LogLevel.Error => "#FF7373",
            Mapsui.Logging.LogLevel.Warning => "#FFCF48",
            _ => "#E2E8EE",
        };
        var text = exception is null ? message : $"{message} ({exception.GetType().Name}: {exception.Message})";

        MapLogEntries.Insert(0, new MapLogEntry(text, color));
        const int maxEntries = 200;
        while (MapLogEntries.Count > maxEntries)
            MapLogEntries.RemoveAt(MapLogEntries.Count - 1);
    }

    internal async Task<EarthdataTestResult> TestEarthdataCredentialsAsync()
    {
        var viewport = Map.Navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return new EarthdataTestResult(EarthdataTestStatus.NoViewport);

        var extent = GetViewportExtent(viewport);
        var bounds = GetLonLatBounds(extent);
        if (!IsFinite(bounds.West) || !IsFinite(bounds.South) || !IsFinite(bounds.East) || !IsFinite(bounds.North))
            return new EarthdataTestResult(EarthdataTestStatus.Error, "Invalid viewport bounds");
        bounds = ClampBounds(bounds);
        return await _contourService.TestCredentialsAsync(bounds, CancellationToken.None);
    }

    private void RefreshLayers()
    {
        List<MapSample> snapshot;
        List<WaypointSample> waypointSnapshot;
        lock (_gate)
        {
            snapshot = _samples.ToList();
            waypointSnapshot = _waypoints.ToList();
        }

        var trackFeatures = BuildTrackFeatures(snapshot);
        _trackLayer.Features = trackFeatures;

        if (EnableCluster)
        {
            _clusterLayer.Features = BuildClusterFeatures(snapshot);
            _pointLayer.Features = Array.Empty<IFeature>();
        }
        else
        {
            _clusterLayer.Features = Array.Empty<IFeature>();
            _pointLayer.Features = BuildPointFeatures(snapshot);
        }

        _waypointLayer.Features = BuildWaypointFeatures(waypointSnapshot);
    }

    private void OnViewportChanged()
    {
        RefreshLayers();
        var zoom = GetCurrentZoom();
        UpdateContourVisibility(zoom);
        UpdateZoomInfo(zoom);
        DebounceContourQueue();
    }

    private void DebounceContourQueue()
    {
        if (!_contourSettings.Enabled)
            return;
        if (_osmTileSource is null)
            return;

        _contourDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _contourDebounce = cts;

        var viewport = Map.Navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;
        var extent = GetViewportExtent(viewport);
        var resolution = viewport.Resolution;
        var allowUltraFine = _contourSettings.EnableUltraFine;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var zoom = GetZoomFromResolution(resolution);
            var spec = GetContourSpec(zoom, allowUltraFine);
            if (spec.Count == 0)
                return;

            var range = GetTileRange(extent, zoom);
            if (range.IsEmpty)
                return;

            _contourService.QueueTiles(zoom, range.MinX, range.MaxX, range.MinY, range.MaxY, spec);
        });
    }

    private void UpdateContourVisibility(int zoom)
    {
        var spec = GetContourSpec(zoom, _contourSettings.EnableUltraFine);
        foreach (var (key, layer) in _contourLayers)
        {
            layer.Enabled = _contourSettings.Enabled && spec.Contains(key);
        }
    }

    private int GetCurrentZoom()
    {
        if (_osmTileSource is null)
            return 0;
        return GetZoomFromResolution(Map.Navigator.Viewport.Resolution);
    }

    private void UpdateZoomInfo()
    {
        UpdateZoomInfo(GetCurrentZoom());
    }

    private void UpdateZoomInfo(int zoom)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateZoomInfo(zoom));
            return;
        }

        if (_currentZoom != zoom)
        {
            _currentZoom = zoom;
            OnPropertyChanged(nameof(CurrentZoom));
        }

        var loc = LocalizationService.Instance;
        _currentZoomText = $"{loc.GetString("Ui.Dashboard.ZoomLabel")}: {zoom}";
        _currentContourText = BuildContourText(loc, zoom);
        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CurrentContourText));
    }

    private string BuildContourText(LocalizationService loc, int zoom)
    {
        var label = loc.GetString("Ui.Dashboard.ContoursLabel");
        if (!_contourSettings.Enabled)
            return $"{label}: {loc.GetString("Ui.Dashboard.ContoursOff")}";

        var spec = GetContourSpec(zoom, _contourSettings.EnableUltraFine);
        if (spec.Count == 0)
            return $"{label}: {loc.GetString("Ui.Dashboard.ContoursNone")}";

        var majorLabel = loc.GetString("Ui.Dashboard.ContourMajor");
        var minorLabel = loc.GetString("Ui.Dashboard.ContourMinor");
        var majors = spec.Where(s => s.Kind == ContourLineKind.Major).Select(s => s.Interval).OrderBy(v => v).ToList();
        var minors = spec.Where(s => s.Kind == ContourLineKind.Minor).Select(s => s.Interval).OrderBy(v => v).ToList();

        var parts = new List<string>();
        if (majors.Count > 0)
            parts.Add($"{majorLabel}{string.Join("/", majors)}m");
        if (minors.Count > 0)
            parts.Add($"{minorLabel}{string.Join("/", minors)}m");
        return $"{label}: {string.Join(" / ", parts)}";
    }

    private static MRect GetViewportExtent(Viewport viewport)
    {
        var halfWidth = viewport.Width * viewport.Resolution / 2.0;
        var halfHeight = viewport.Height * viewport.Resolution / 2.0;
        var minX = viewport.CenterX - halfWidth;
        var maxX = viewport.CenterX + halfWidth;
        var minY = viewport.CenterY - halfHeight;
        var maxY = viewport.CenterY + halfHeight;
        return new MRect(minX, minY, maxX, maxY);
    }

    private int GetZoomFromResolution(double resolution)
    {
        if (_osmTileSource is null)
            return 0;
        return BruTile.Utilities.GetNearestLevel(_osmTileSource.Schema.Resolutions, resolution);
    }

    private static TileRange GetTileRange(MRect extent, int zoom)
    {
        var (west, south, east, north) = GetLonLatBounds(extent);

        var xMin = ContourTileMath.LonToTileX(west, zoom);
        var xMax = ContourTileMath.LonToTileX(east, zoom);
        var yMin = ContourTileMath.LatToTileY(north, zoom);
        var yMax = ContourTileMath.LatToTileY(south, zoom);

        if (xMin > xMax || yMin > yMax)
            return TileRange.Empty;

        xMin = Math.Max(0, xMin - 1);
        yMin = Math.Max(0, yMin - 1);
        xMax = Math.Min((1 << zoom) - 1, xMax + 1);
        yMax = Math.Min((1 << zoom) - 1, yMax + 1);

        var total = (xMax - xMin + 1) * (yMax - yMin + 1);
        if (total > 500)
            return TileRange.Empty;

        return new TileRange(xMin, xMax, yMin, yMax);
    }

    private static (double West, double South, double East, double North) GetLonLatBounds(MRect extent)
    {
        var (minLon, minLat) = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);

        var west = Math.Min(minLon, maxLon);
        var east = Math.Max(minLon, maxLon);
        var south = Math.Min(minLat, maxLat);
        var north = Math.Max(minLat, maxLat);
        return (west, south, east, north);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static (double West, double South, double East, double North) ClampBounds(
        (double West, double South, double East, double North) bounds)
    {
        const double MaxLat = 85.05112878;
        const double MaxLon = 179.999999;

        var west = Math.Clamp(bounds.West, -MaxLon, MaxLon);
        var east = Math.Clamp(bounds.East, -MaxLon, MaxLon);
        var south = Math.Clamp(bounds.South, -MaxLat, MaxLat);
        var north = Math.Clamp(bounds.North, -MaxLat, MaxLat);

        if (west > east)
            (west, east) = (east, west);
        if (south > north)
            (south, north) = (north, south);

        return (west, south, east, north);
    }

    private static HashSet<ContourKey> GetContourSpec(int zoom, bool allowUltraFine)
    {
        var spec = new HashSet<ContourKey>();

        if (zoom <= 7)
            return spec;
        if (zoom == 8)
            return AddMajor(spec, 500);
        if (zoom == 9)
            return AddMajor(spec, 200);
        if (zoom == 10)
            return AddMinor(AddMajor(spec, 500), 100);
        if (zoom == 11)
            return AddMinor(AddMajor(spec, 200), 50);
        if (zoom == 12)
            return AddMinor(AddMajor(spec, 100), 50);
        if (zoom == 13 || zoom == 14)
            return AddMinor(AddMajor(spec, 100), 20);
        if (zoom == 15 || zoom == 16)
            return AddMinor(AddMajor(spec, 50), 10);
        if (zoom >= 17)
        {
            AddMajor(spec, 25);
            if (allowUltraFine)
                AddMinor(spec, 5);
            return spec;
        }

        return spec;
    }

    private IEnumerable<TileLayer> CreateContourLayers()
    {
        var root = GetContourRoot();
        var schema = new GlobalSphericalMercator(0, 19, "EPSG:3857");
        var layers = new List<TileLayer>();

        foreach (var interval in new[] { 500, 200, 100, 50, 25 })
        {
            layers.Add(CreateContourLayer(schema, root, ContourLineKind.Major, interval, 0.85f));
        }

        foreach (var interval in new[] { 100, 50, 20, 10, 5 })
        {
            layers.Add(CreateContourLayer(schema, root, ContourLineKind.Minor, interval, 0.7f));
        }

        return layers;
    }

    private TileLayer CreateContourLayer(ITileSchema schema, string root, ContourLineKind kind, int interval, float opacity)
    {
        var dir = Path.Combine(root, "tiles", $"{kind.ToString().ToLowerInvariant()}-{interval}");
        Directory.CreateDirectory(dir);

        var source = new FileTileSource(schema, dir, "png", null, $"contours-{kind}-{interval}", null);
        var layer = new TileLayer(source)
        {
            Name = $"contours-{kind}-{interval}",
            Opacity = opacity,
            Enabled = false,
        };

        _contourLayers[new ContourKey(kind, interval)] = layer;
        return layer;
    }

    private static string GetContourRoot()
    {
        return ContourPaths.Root;
    }

    private void OnContourTilesUpdated()
    {
        Dispatcher.UIThread.Post(() => Map.Refresh(ChangeType.Discrete));
    }

    private IEnumerable<IFeature> BuildTrackFeatures(List<MapSample> samples)
    {
        var grouped = samples.GroupBy(s => s.DeviceId).ToList();
        var features = new List<IFeature>();
        foreach (var group in grouped)
        {
            var coords = group.Select(s => new Coordinate(s.Point.X, s.Point.Y)).ToArray();
            if (coords.Length < 2)
                continue;
            var line = new LineString(coords);
            var feature = new GeometryFeature { Geometry = line };
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen(Color.FromArgb(255, 56, 189, 248), 2),
            });
            features.Add(feature);
        }
        return features;
    }

    private IEnumerable<IFeature> BuildPointFeatures(List<MapSample> samples)
    {
        return samples.Select(sample =>
        {
            var feature = new PointFeature(sample.Point);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(Color.FromArgb(210, 159, 232, 112)),
                Outline = new Pen(Color.FromArgb(255, 90, 162, 82), 1),
                SymbolScale = 0.7,
            });
            return feature;
        }).ToList();
    }

    private IEnumerable<IFeature> BuildClusterFeatures(List<MapSample> samples)
    {
        var clusters = new List<ClusterBucket>();
        var resolution = Map.Navigator.Viewport.Resolution;
        var radiusWorld = ClusterRadiusPx * resolution;

        foreach (var sample in samples)
        {
            ClusterBucket? bucket = null;
            foreach (var c in clusters)
            {
                if (c.DistanceTo(sample.Point) <= radiusWorld)
                {
                    bucket = c;
                    break;
                }
            }
            if (bucket is null)
            {
                bucket = new ClusterBucket(sample.Point);
                clusters.Add(bucket);
            }
            bucket.Add(sample.Point);
        }

        var features = new List<IFeature>();
        foreach (var cluster in clusters)
        {
            var feature = new PointFeature(cluster.Center);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(Color.FromArgb(200, 245, 158, 11)),
                Outline = new Pen(Color.FromArgb(255, 180, 83, 9), 1),
                SymbolScale = 1.6,
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = cluster.Count.ToString(),
                ForeColor = Color.Black,
                Offset = new Offset(0, 0),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
            });
            features.Add(feature);
        }
        return features;
    }

    private IEnumerable<IFeature> BuildWaypointFeatures(List<WaypointSample> samples)
    {
        var features = new List<IFeature>();
        foreach (var sample in samples)
        {
            var feature = new PointFeature(sample.Point);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Triangle,
                Fill = new Brush(Color.FromArgb(220, 239, 68, 68)),
                Outline = new Pen(Color.FromArgb(255, 185, 28, 28), 1),
                SymbolScale = 1.0,
            });
            if (!string.IsNullOrWhiteSpace(sample.Label))
            {
                feature.Styles.Add(new LabelStyle
                {
                    Text = sample.Label,
                    ForeColor = Color.White,
                    BackColor = new Brush(Color.FromArgb(180, 30, 30, 30)),
                    Offset = new Offset(0, -18),
                    HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                });
            }
            features.Add(feature);
        }
        return features;
    }

    private TileLayer CreateOsmLayer()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter", "tilecache");
        Directory.CreateDirectory(cacheDir);
        var fileCache = new FileCache(cacheDir, "png");
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap, "TrailMateCenter", fileCache, null, 0, 19);
        _osmTileSource = tileSource;

        return new TileLayer(
            tileSource,
            minTiles: 100,
            maxTiles: 400,
            dataFetchStrategy: new MinimalDataFetchStrategy(),
            renderFetchStrategy: new MinimalRenderFetchStrategy(),
            minExtraTiles: 0,
            maxExtraTiles: 0,
            fetchTileAsFeature: null,
            httpClient: null);
    }

    private sealed record MapSample(uint DeviceId, MPoint Point);
    private sealed record WaypointSample(uint DeviceId, MPoint Point, string? Label);

    private sealed class ClusterBucket
    {
        private double _sumX;
        private double _sumY;

        public ClusterBucket(MPoint seed)
        {
            _sumX = seed.X;
            _sumY = seed.Y;
            Count = 1;
        }

        public int Count { get; private set; }

        public MPoint Center => new(_sumX / Count, _sumY / Count);

        public void Add(MPoint point)
        {
            _sumX += point.X;
            _sumY += point.Y;
            Count++;
        }

        public double DistanceTo(MPoint point)
        {
            var dx = point.X - Center.X;
            var dy = point.Y - Center.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private readonly record struct TileRange(int MinX, int MaxX, int MinY, int MaxY)
    {
        public static TileRange Empty => new(0, -1, 0, -1);
        public bool IsEmpty => MaxX < MinX || MaxY < MinY;
    }

    private static HashSet<ContourKey> AddMajor(HashSet<ContourKey> set, int interval)
    {
        set.Add(new ContourKey(ContourLineKind.Major, interval));
        return set;
    }

    private static HashSet<ContourKey> AddMinor(HashSet<ContourKey> set, int interval)
    {
        set.Add(new ContourKey(ContourLineKind.Minor, interval));
        return set;
    }

    private void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed record MapLogEntry(string Message, string Color);

}
