using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Fetcher;
using Mapsui.Tiling.Layers;
using Mapsui.Tiling.Rendering;
using Mapsui.Rendering;
using Mapsui.Manipulations;
using Mapsui.Projections;
using BruTile;
using BruTile.Cache;
using BruTile.FileSystem;
using BruTile.Predefined;
using BruTile.Web;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using Mapsui.Nts;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Xml.Linq;
using TrailMateCenter.Localization;
using TrailMateCenter.Services;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public enum MapSampleSource
{
    Local = 0,
    Mqtt = 1,
}

public enum MapBaseLayerKind
{
    Osm = 0,
    Terrain = 1,
    Satellite = 2,
}

public sealed record OfflineCacheBuildOptions
{
    public bool IncludeOsm { get; init; } = true;
    public bool IncludeTerrain { get; init; } = true;
    public bool IncludeSatellite { get; init; } = true;
    public bool IncludeContours { get; init; } = true;
    public bool IncludeUltraFineContours { get; init; }
}

public sealed class MapViewModel : INotifyPropertyChanged
{
    private const string NodeIdField = "tmc_node_id";
    private const string OfflineRouteIdField = "tmc_offline_route_id";
    private readonly object _gate = new();
    private readonly List<MapSample> _samples = new();
    private readonly List<WaypointSample> _waypoints = new();
    private readonly List<OfflineRoute> _offlineRoutes = new();
    private readonly MemoryLayer _pointLayer;
    private readonly MemoryLayer _trackLayer;
    private readonly MemoryLayer _clusterLayer;
    private readonly MemoryLayer _waypointLayer;
    private readonly MemoryLayer _offlineSelectionLayer;
    private readonly MemoryLayer _offlineRouteLayer;
    private readonly Dictionary<MapBaseLayerKind, TileLayer> _baseLayers = new();
    private readonly TileLayer _gibsLayer;
    private readonly Action<Mapsui.Logging.LogLevel, string, Exception?> _mapsuiLogSink;
    private readonly Dictionary<ContourKey, TileLayer> _contourLayers = new();
    private readonly ContourTileService _contourService;
    private int _contourRefreshScheduled;
    private ITileSource? _osmTileSource;
    private ContourSettings _contourSettings = new();
    private CancellationTokenSource? _contourDebounce;
    private int _currentZoom;
    private string _currentZoomText = string.Empty;
    private string _currentContourText = string.Empty;
    private bool _showLogs;
    private bool _showMqtt = true;
    private bool _showGibs;
    private MapBaseLayerKind _baseLayer = MapBaseLayerKind.Osm;
    private string _lastContourQueueDiagnostic = string.Empty;
    private readonly object _offlineCacheGate = new();
    private readonly HttpClient _offlineTileHttpClient = new();
    private CancellationTokenSource? _offlineCacheRunCts;
    private (double West, double South, double East, double North)? _offlineCacheSelectionBounds;
    private Geometry? _offlineCacheSelectionGeometry;
    private string? _offlineCacheSelectionName;
    private bool _isOfflineCacheSelectionMode;
    private bool _isOfflineCacheRunning;
    private string _offlineCacheStatusText = string.Empty;
    private string? _selectedOfflineRouteId;
    private int _bulkUpdateDepth;
    private bool _layersDirty;

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
        _offlineTileHttpClient.Timeout = TimeSpan.FromSeconds(20);
        _offlineTileHttpClient.DefaultRequestHeaders.UserAgent.Clear();
        _offlineTileHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrailMateCenter", "0.1"));

        var osmLayer = CreateOsmLayer();
        osmLayer.Opacity = 0.8f;
        _baseLayers[MapBaseLayerKind.Osm] = osmLayer;
        Map.Layers.Add(osmLayer);

        var terrainLayer = CreateTerrainLayer();
        _baseLayers[MapBaseLayerKind.Terrain] = terrainLayer;
        Map.Layers.Add(terrainLayer);

        var satelliteLayer = CreateSatelliteLayer();
        _baseLayers[MapBaseLayerKind.Satellite] = satelliteLayer;
        Map.Layers.Add(satelliteLayer);

        _gibsLayer = CreateGibsLayer();
        Map.Layers.Add(_gibsLayer);

        foreach (var contourLayer in CreateContourLayers())
        {
            Map.Layers.Add(contourLayer);
        }

        _trackLayer = new MemoryLayer { Name = "tracks" };
        _pointLayer = new MemoryLayer { Name = "points" };
        _clusterLayer = new MemoryLayer { Name = "clusters" };
        _waypointLayer = new MemoryLayer { Name = "waypoints" };
        _offlineSelectionLayer = new MemoryLayer { Name = "offline-selection-overlay" };
        _offlineRouteLayer = new MemoryLayer { Name = "offline-routes" };

        Map.Layers.Add(_offlineSelectionLayer);
        Map.Layers.Add(_offlineRouteLayer);
        Map.Layers.Add(_trackLayer);
        Map.Layers.Add(_clusterLayer);
        Map.Layers.Add(_pointLayer);
        Map.Layers.Add(_waypointLayer);

        Map.Navigator.ViewportChanged += (_, _) => OnViewportChanged();
        UpdateZoomInfo();
    }

    public Map Map { get; }

    public bool FollowLatest { get; set; }
    public bool EnableCluster { get; set; } = true;
    public int ClusterRadiusPx { get; set; } = 60;
    public int CurrentZoom => _currentZoom;
    public string CurrentZoomText => _currentZoomText;
    public string CurrentContourText => _currentContourText;
    public bool IsOfflineCacheSelectionMode
    {
        get => _isOfflineCacheSelectionMode;
        set
        {
            if (_isOfflineCacheSelectionMode == value)
                return;

            _isOfflineCacheSelectionMode = value;
            OnPropertyChanged(nameof(IsOfflineCacheSelectionMode));
        }
    }
    public bool HasOfflineCacheSelection => _offlineCacheSelectionBounds.HasValue;
    public bool IsOfflineCacheRunning => _isOfflineCacheRunning;
    public bool CanRunOfflineCache => !_isOfflineCacheRunning && _offlineCacheSelectionBounds.HasValue;
    public string OfflineCacheStatusText => _offlineCacheStatusText;
    public ObservableCollection<MapLogEntry> MapLogEntries { get; } = new();

    public void RefreshLocalization()
    {
        UpdateZoomInfo();
    }

    public void AddPoint(uint? fromId, double lat, double lon, MapSampleSource source = MapSampleSource.Local)
    {
        var id = fromId ?? 0;
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var point = new MPoint(x, y);

        lock (_gate)
        {
            _samples.Add(new MapSample(id, point, source));
            if (_samples.Count > 2000)
                _samples.RemoveAt(0);
        }

        RequestLayerRefresh();

        if (FollowLatest && (source != MapSampleSource.Mqtt || _showMqtt))
        {
            Map.Navigator.CenterOn(point, 0, null);
        }
    }

    public void AddWaypoint(uint? fromId, double lat, double lon, string? label, MapSampleSource source = MapSampleSource.Local)
    {
        var id = fromId ?? 0;
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var point = new MPoint(x, y);

        lock (_gate)
        {
            _waypoints.Add(new WaypointSample(id, point, label, source));
            if (_waypoints.Count > 500)
                _waypoints.RemoveAt(0);
        }

        RequestLayerRefresh();

        if (FollowLatest && (source != MapSampleSource.Mqtt || _showMqtt))
        {
            Map.Navigator.CenterOn(point, 0, null);
        }
    }

    public void FocusOn(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        Map.Navigator.CenterOn(new MPoint(x, y), 0, null);
    }

    public void FocusOnBounds((double West, double South, double East, double North) bounds)
    {
        var normalized = ClampBounds(bounds);
        var (minX, minY) = SphericalMercator.FromLonLat(normalized.West, normalized.South);
        var (maxX, maxY) = SphericalMercator.FromLonLat(normalized.East, normalized.North);
        var rect = new MRect(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minX, maxX),
            Math.Max(minY, maxY));

        if (rect.Width < 1 || rect.Height < 1)
        {
            Map.Navigator.CenterOn(new MPoint((rect.MinX + rect.MaxX) * 0.5, (rect.MinY + rect.MaxY) * 0.5), 0, null);
            return;
        }

        Map.Navigator.ZoomToBox(rect, MBoxFit.Fit, 0, null);
    }

    public void Refresh() => RefreshLayers();

    public IDisposable BeginBulkUpdate()
    {
        lock (_gate)
        {
            _bulkUpdateDepth++;
        }

        return new BulkUpdateScope(this);
    }

    public void UpdateContourSettings(ContourSettings settings)
    {
        _contourSettings = settings ?? new ContourSettings();
        _contourService.UpdateSettings(_contourSettings);
        _lastContourQueueDiagnostic = string.Empty;
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

    public void SetMqttVisibility(bool enabled)
    {
        if (_showMqtt == enabled)
            return;

        _showMqtt = enabled;
        RefreshLayers();
        Map.Refresh(ChangeType.Discrete);
    }

    public void SetBaseLayer(MapBaseLayerKind layer)
    {
        if (_baseLayer == layer)
            return;

        _baseLayer = layer;
        foreach (var (kind, baseLayer) in _baseLayers)
        {
            var enabled = kind == _baseLayer;
            if (baseLayer.Enabled == enabled)
                continue;

            baseLayer.Enabled = enabled;
            baseLayer.ClearCache();
        }

        Map.Refresh(ChangeType.Discrete);
    }

    public void SetGibsVisibility(bool enabled)
    {
        if (_showGibs == enabled)
            return;

        _showGibs = enabled;
        _gibsLayer.Enabled = enabled;
        _gibsLayer.ClearCache();
        Map.Refresh(ChangeType.Discrete);
    }

    public void SetOfflineCacheSelectionBounds((double West, double South, double East, double North) bounds, string? selectionName = null)
    {
        var normalized = ClampBounds(bounds);
        var geometry = CreateRectPolygon(normalized);
        SetOfflineCacheSelectionGeometryCore(
            geometry,
            normalized,
            selectionName,
            statusText: $"Selection: W {normalized.West:F5}, S {normalized.South:F5}, E {normalized.East:F5}, N {normalized.North:F5}");
    }

    public bool SetOfflineCacheSelectionPolygonFromWorldPoints(
        IReadOnlyList<MPoint> worldPoints,
        string? selectionName = null,
        bool updateStatus = true)
    {
        if (worldPoints is null || worldPoints.Count < 3)
            return false;

        var ring = new Coordinate[worldPoints.Count + 1];
        for (var i = 0; i < worldPoints.Count; i++)
        {
            ring[i] = new Coordinate(worldPoints[i].X, worldPoints[i].Y);
        }
        ring[worldPoints.Count] = ring[0];

        Geometry geometry;
        try
        {
            geometry = new Polygon(new LinearRing(ring));
            if (!geometry.IsValid)
            {
                geometry = geometry.Buffer(0);
            }
        }
        catch
        {
            return false;
        }

        if (geometry.IsEmpty || geometry.EnvelopeInternal.IsNull)
            return false;

        var bounds = GetLonLatBounds(geometry.EnvelopeInternal);
        var normalized = ClampBounds(bounds);
        var statusText = updateStatus
            ? $"Polygon selection: {worldPoints.Count} points, W {normalized.West:F5}, S {normalized.South:F5}, E {normalized.East:F5}, N {normalized.North:F5}"
            : null;
        SetOfflineCacheSelectionGeometryCore(geometry, normalized, selectionName, statusText);
        return true;
    }

    public bool TryGetOfflineCacheSelectionBounds(out (double West, double South, double East, double North) bounds)
    {
        if (_offlineCacheSelectionBounds.HasValue)
        {
            bounds = _offlineCacheSelectionBounds.Value;
            return true;
        }

        bounds = default;
        return false;
    }

    public void ClearOfflineCacheSelection()
    {
        _offlineCacheSelectionBounds = null;
        _offlineCacheSelectionGeometry = null;
        _offlineCacheSelectionName = null;
        RefreshOfflineSelectionOverlay();
        OnPropertyChanged(nameof(HasOfflineCacheSelection));
        OnPropertyChanged(nameof(CanRunOfflineCache));
        SetOfflineCacheStatus("Selection cleared.");
    }

    public void CancelOfflineCache()
    {
        lock (_offlineCacheGate)
        {
            _offlineCacheRunCts?.Cancel();
        }
    }

    public Task RunOfflineCacheForSelectionAsync()
    {
        return RunOfflineCacheForSelectionAsync(new OfflineCacheBuildOptions());
    }

    public async Task RunOfflineCacheForSelectionAsync(OfflineCacheBuildOptions? options)
    {
        if (_isOfflineCacheRunning)
        {
            SetOfflineCacheStatus("Offline cache task is already running.");
            return;
        }

        if (!_offlineCacheSelectionBounds.HasValue)
        {
            SetOfflineCacheStatus("Select an area first.");
            return;
        }

        var buildOptions = options ?? new OfflineCacheBuildOptions();
        if (!buildOptions.IncludeOsm &&
            !buildOptions.IncludeTerrain &&
            !buildOptions.IncludeSatellite &&
            !buildOptions.IncludeContours)
        {
            SetOfflineCacheStatus("Nothing selected for caching.");
            return;
        }

        var bounds = ClampBounds(_offlineCacheSelectionBounds.Value);
        Geometry? selectionGeometry;
        lock (_gate)
        {
            selectionGeometry = _offlineCacheSelectionGeometry?.Copy();
        }
        var preparedSelection = selectionGeometry is null
            ? null
            : PreparedGeometryFactory.Prepare(selectionGeometry);
        var cts = new CancellationTokenSource();
        lock (_offlineCacheGate)
        {
            if (_isOfflineCacheRunning)
            {
                cts.Dispose();
                SetOfflineCacheStatus("Offline cache task is already running.");
                return;
            }

            _offlineCacheRunCts = cts;
            _isOfflineCacheRunning = true;
        }

        OnPropertyChanged(nameof(IsOfflineCacheRunning));
        OnPropertyChanged(nameof(CanRunOfflineCache));
        SetOfflineCacheStatus("Offline cache task started...");
        AddMapLog(
            Mapsui.Logging.LogLevel.Information,
            $"Offline cache task started (osm={buildOptions.IncludeOsm}, terrain={buildOptions.IncludeTerrain}, satellite={buildOptions.IncludeSatellite}, contours={buildOptions.IncludeContours}, ultraFine={buildOptions.IncludeUltraFineContours})",
            null,
            force: true);

        try
        {
            var token = cts.Token;
            var totalDownloaded = 0;
            var totalSkipped = 0;
            var totalFailed = 0;
            var totalPlanned = 0L;

            foreach (var source in BuildOfflineTileSources(buildOptions))
            {
                for (var zoom = 0; zoom <= 18; zoom++)
                {
                    if (zoom < source.MinZoom || zoom > source.MaxZoom)
                        continue;

                    var range = GetOfflineTileRange(bounds, zoom);
                    if (range.IsEmpty)
                        continue;

                    var tiles = GetIntersectingTiles(range, zoom, preparedSelection);
                    var tileCount = tiles.Count;
                    if (tileCount <= 0)
                        continue;

                    totalPlanned += tileCount;
                    SetOfflineCacheStatus($"Caching {source.DisplayName} Z{zoom} ({tileCount} tiles)...");
                    AddMapLog(
                        Mapsui.Logging.LogLevel.Information,
                        $"Offline cache: {source.DisplayName} Z{zoom} X {range.MinX}-{range.MaxX}, Y {range.MinY}-{range.MaxY}, tiles={tileCount}",
                        null,
                        force: true);

                    var (downloaded, skipped, failed) = await CacheBaseTilesAsync(source, zoom, tiles, token);
                    totalDownloaded += downloaded;
                    totalSkipped += skipped;
                    totalFailed += failed;
                    SetOfflineCacheStatus(
                        $"Caching {source.DisplayName} Z{zoom} done: +{downloaded}, skip {skipped}, fail {failed}");
                }
            }

            if (!buildOptions.IncludeContours)
            {
                AddMapLog(
                    Mapsui.Logging.LogLevel.Information,
                    "Offline cache: contour generation skipped (disabled by options)",
                    null,
                    force: true);
            }
            else if (_contourSettings.Earthdata is null || string.IsNullOrWhiteSpace(_contourSettings.Earthdata.Token))
            {
                AddMapLog(
                    Mapsui.Logging.LogLevel.Warning,
                    "Offline cache: contour generation skipped (Earthdata token missing)",
                    null,
                    force: true);
            }
            else
            {
                var contourSettings = _contourSettings with
                {
                    Enabled = true,
                    EnableUltraFine = buildOptions.IncludeUltraFineContours,
                };
                _contourService.UpdateSettings(contourSettings);

                var queuedContourLineTiles = 0L;
                for (var zoom = 0; zoom <= 18; zoom++)
                {
                    var spec = GetContourSpec(zoom, contourSettings.EnableUltraFine);
                    if (spec.Count == 0)
                        continue;

                    var range = GetOfflineTileRange(bounds, zoom);
                    if (range.IsEmpty)
                        continue;

                    var tiles = GetIntersectingTiles(range, zoom, preparedSelection);
                    var mapTileCount = tiles.Count;
                    if (mapTileCount <= 0)
                        continue;

                    queuedContourLineTiles += (long)mapTileCount * spec.Count;
                    SetOfflineCacheStatus($"Queueing contours Z{zoom} ({mapTileCount} map tiles)...");
                    foreach (var tile in tiles)
                    {
                        _contourService.QueueTiles(zoom, tile.X, tile.X, tile.Y, tile.Y, spec);
                    }
                }

                SetOfflineCacheStatus($"Waiting contour generation queue ({queuedContourLineTiles} line tiles planned)...");
                await _contourService.WaitForIdleAsync(token);
                _contourService.UpdateSettings(_contourSettings);
            }

            SetOfflineCacheStatus(
                $"Offline cache complete: planned {totalPlanned}, downloaded {totalDownloaded}, skipped {totalSkipped}, failed {totalFailed}");
            AddMapLog(
                Mapsui.Logging.LogLevel.Information,
                $"Offline cache complete: planned={totalPlanned}, downloaded={totalDownloaded}, skipped={totalSkipped}, failed={totalFailed}",
                null,
                force: true);
        }
        catch (OperationCanceledException)
        {
            SetOfflineCacheStatus("Offline cache task canceled.");
            AddMapLog(Mapsui.Logging.LogLevel.Warning, "Offline cache task canceled", null, force: true);
        }
        catch (Exception ex)
        {
            SetOfflineCacheStatus($"Offline cache failed: {ex.Message}");
            AddMapLog(Mapsui.Logging.LogLevel.Error, $"Offline cache failed: {ex.Message}", ex, force: true);
        }
        finally
        {
            lock (_offlineCacheGate)
            {
                _offlineCacheRunCts?.Dispose();
                _offlineCacheRunCts = null;
                _isOfflineCacheRunning = false;
            }

            OnPropertyChanged(nameof(IsOfflineCacheRunning));
            OnPropertyChanged(nameof(CanRunOfflineCache));
        }
    }

    public uint? ResolveNodeIdFromMapInfo(Func<IEnumerable<ILayer>, MapInfo> getMapInfo)
    {
        if (getMapInfo is null)
            return null;

        MapInfo? mapInfo;
        try
        {
            mapInfo = getMapInfo(GetNodeHitTestLayers());
        }
        catch
        {
            return null;
        }

        return ResolveNodeIdFromMapInfo(mapInfo);
    }

    public uint? ResolveNodeIdAtScreenPosition(
        ScreenPosition screenPosition,
        Func<ScreenPosition, IEnumerable<ILayer>, MapInfo> getMapInfo)
    {
        if (getMapInfo is null)
            return null;

        MapInfo? mapInfo;
        try
        {
            mapInfo = getMapInfo(screenPosition, GetNodeHitTestLayers());
        }
        catch
        {
            return null;
        }

        return ResolveNodeIdFromMapInfo(mapInfo);
    }

    public OfflineRouteImportResult ImportOfflineRouteFromKml(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new OfflineRouteImportResult(false, null, 0, "KML file not found.");
            }

            var points = ParseKmlTrackPoints(filePath);
            if (points.Count < 2)
            {
                return new OfflineRouteImportResult(false, null, points.Count, "No valid track points in KML.");
            }

            var routeName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(routeName))
                routeName = $"Track {DateTime.Now:HHmmss}";

            var route = new OfflineRoute(
                Guid.NewGuid().ToString("N"),
                routeName.Trim(),
                points);

            lock (_gate)
            {
                _offlineRoutes.Add(route);
                if (_offlineRoutes.Count > 32)
                {
                    _offlineRoutes.RemoveAt(0);
                }

                _selectedOfflineRouteId = route.Id;
            }

            RefreshImportedRoutesLayer();
            FocusOnBounds(route.Bounds);
            SetOfflineCacheStatus($"Track imported: {route.Name} ({route.Points.Count} points)");
            return new OfflineRouteImportResult(true, route.Name, route.Points.Count, null);
        }
        catch (Exception ex)
        {
            SetOfflineCacheStatus($"Track import failed: {ex.Message}");
            return new OfflineRouteImportResult(false, null, 0, ex.Message);
        }
    }

    public bool SelectOfflineRouteFromMapInfo(Func<IEnumerable<ILayer>, MapInfo> getMapInfo)
    {
        if (getMapInfo is null)
            return false;

        MapInfo? mapInfo;
        try
        {
            mapInfo = getMapInfo(GetOfflineRouteHitTestLayers());
        }
        catch
        {
            return false;
        }

        var routeId = ResolveOfflineRouteIdFromMapInfo(mapInfo);
        return SelectOfflineRoute(routeId);
    }

    public bool SelectOfflineRouteAtScreenPosition(
        ScreenPosition screenPosition,
        Func<ScreenPosition, IEnumerable<ILayer>, MapInfo> getMapInfo)
    {
        if (getMapInfo is null)
            return false;

        MapInfo? mapInfo;
        try
        {
            mapInfo = getMapInfo(screenPosition, GetOfflineRouteHitTestLayers());
        }
        catch
        {
            return false;
        }

        var routeId = ResolveOfflineRouteIdFromMapInfo(mapInfo);
        return SelectOfflineRoute(routeId);
    }

    public bool BuildOfflineCacheSelectionFromSelectedRoute(double radiusKm)
    {
        if (radiusKm <= 0)
            return false;

        OfflineRoute? route;
        lock (_gate)
        {
            route = _offlineRoutes.FirstOrDefault(r => string.Equals(r.Id, _selectedOfflineRouteId, StringComparison.Ordinal));
        }

        if (route is null)
            return false;

        var geometry = route.Points.Count <= 1
            ? (Geometry)new Point(route.Points[0].X, route.Points[0].Y)
            : new LineString(route.Points.Select(p => new Coordinate(p.X, p.Y)).ToArray());
        var buffered = geometry.Buffer(radiusKm * 1000.0);
        var envelope = buffered.EnvelopeInternal;
        if (envelope.IsNull)
            return false;

        var normalized = ClampBounds(GetLonLatBounds(envelope));
        SetOfflineCacheSelectionGeometryCore(
            buffered,
            normalized,
            route.Name,
            statusText: $"Route cache area ready: {route.Name}, radius {radiusKm:F1}km");
        return true;
    }

    public bool FocusSelectedOfflineRoute()
    {
        OfflineRoute? route;
        lock (_gate)
        {
            route = _offlineRoutes.FirstOrDefault(r => string.Equals(r.Id, _selectedOfflineRouteId, StringComparison.Ordinal));
        }

        if (route is null)
            return false;

        FocusOnBounds(route.Bounds);
        return true;
    }

    private uint? ResolveNodeIdFromMapInfo(MapInfo? mapInfo)
    {
        if (mapInfo is null)
            return null;

        var nodeId = TryGetNodeId(mapInfo.Feature);
        if (nodeId.HasValue)
            return nodeId;

        foreach (var record in mapInfo.MapInfoRecords ?? Enumerable.Empty<MapInfoRecord>())
        {
            nodeId = TryGetNodeId(record.Feature);
            if (nodeId.HasValue)
                return nodeId;
        }

        return null;
    }

    private string? ResolveOfflineRouteIdFromMapInfo(MapInfo? mapInfo)
    {
        if (mapInfo is null)
            return null;

        var routeId = TryGetOfflineRouteId(mapInfo.Feature);
        if (!string.IsNullOrWhiteSpace(routeId))
            return routeId;

        foreach (var record in mapInfo.MapInfoRecords ?? Enumerable.Empty<MapInfoRecord>())
        {
            routeId = TryGetOfflineRouteId(record.Feature);
            if (!string.IsNullOrWhiteSpace(routeId))
                return routeId;
        }

        return null;
    }

    private bool SelectOfflineRoute(string? routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return false;

        bool changed;
        lock (_gate)
        {
            var exists = _offlineRoutes.Any(r => string.Equals(r.Id, routeId, StringComparison.Ordinal));
            if (!exists)
                return false;

            changed = !string.Equals(_selectedOfflineRouteId, routeId, StringComparison.Ordinal);
            _selectedOfflineRouteId = routeId;
        }

        if (changed)
        {
            RefreshImportedRoutesLayer();
            var route = GetSelectedOfflineRoute();
            if (route is not null)
            {
                SetOfflineCacheStatus($"Track selected: {route.Name}");
            }
        }

        return true;
    }

    private OfflineRoute? GetSelectedOfflineRoute()
    {
        lock (_gate)
        {
            return _offlineRoutes.FirstOrDefault(r => string.Equals(r.Id, _selectedOfflineRouteId, StringComparison.Ordinal));
        }
    }

    private IEnumerable<ILayer> GetOfflineRouteHitTestLayers()
    {
        yield return _offlineRouteLayer;
    }

    private IEnumerable<ILayer> GetNodeHitTestLayers()
    {
        yield return _clusterLayer;
        yield return _pointLayer;
        yield return _waypointLayer;
    }

    private static string? TryGetOfflineRouteId(IFeature? feature)
    {
        if (feature is null)
            return null;

        var value = feature[OfflineRouteIdField];
        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            _ => null,
        };
    }

    private static uint? TryGetNodeId(IFeature? feature)
    {
        if (feature is null)
            return null;

        var value = feature[NodeIdField];
        return value switch
        {
            uint id when id != 0 => id,
            int id when id > 0 => (uint)id,
            long id when id > 0 && id <= uint.MaxValue => (uint)id,
            string text when uint.TryParse(text, out var id) && id != 0 => id,
            _ => null,
        };
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

    private void LogContourQueueDiagnostic(ContourLogLevel level, string message)
    {
        if (string.Equals(_lastContourQueueDiagnostic, message, StringComparison.Ordinal))
            return;
        _lastContourQueueDiagnostic = message;
        LogContourMessage(level, message);
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
        List<OfflineRoute> offlineRouteSnapshot;
        string? selectedOfflineRouteId;
        lock (_gate)
        {
            snapshot = _samples.ToList();
            waypointSnapshot = _waypoints.ToList();
            offlineRouteSnapshot = _offlineRoutes.ToList();
            selectedOfflineRouteId = _selectedOfflineRouteId;
        }

        if (!_showMqtt)
        {
            snapshot = snapshot.Where(s => s.Source != MapSampleSource.Mqtt).ToList();
            waypointSnapshot = waypointSnapshot.Where(s => s.Source != MapSampleSource.Mqtt).ToList();
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
        _offlineRouteLayer.Features = BuildOfflineRouteFeatures(offlineRouteSnapshot, selectedOfflineRouteId);
    }

    private void RequestLayerRefresh()
    {
        lock (_gate)
        {
            if (_bulkUpdateDepth > 0)
            {
                _layersDirty = true;
                return;
            }
        }

        RefreshLayers();
    }

    private void EndBulkUpdate()
    {
        var shouldRefresh = false;
        lock (_gate)
        {
            if (_bulkUpdateDepth > 0)
            {
                _bulkUpdateDepth--;
            }

            if (_bulkUpdateDepth == 0 && _layersDirty)
            {
                _layersDirty = false;
                shouldRefresh = true;
            }
        }

        if (shouldRefresh)
            RefreshLayers();
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
            {
                LogContourQueueDiagnostic(ContourLogLevel.Info, $"Queue skipped at Z{zoom}: no contour profile for this zoom.");
                return;
            }

            var range = GetTileRange(extent, zoom, out var tileCount, out var skipReason);
            if (range.IsEmpty)
            {
                LogContourQueueDiagnostic(ContourLogLevel.Warning, $"Queue skipped at Z{zoom}: {skipReason ?? "empty tile range"}");
                return;
            }

            var specText = string.Join(", ", spec.OrderBy(s => s.Kind).ThenBy(s => s.Interval)
                .Select(s => $"{s.Kind.ToString().ToLowerInvariant()}-{s.Interval}m"));
            LogContourQueueDiagnostic(
                ContourLogLevel.Info,
                $"Queue at Z{zoom}: X {range.MinX}-{range.MaxX}, Y {range.MinY}-{range.MaxY}, tiles={tileCount}, spec=[{specText}]");
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

    private static TileRange GetTileRange(MRect extent, int zoom, out int tileCount, out string? skipReason)
    {
        tileCount = 0;
        skipReason = null;
        var (west, south, east, north) = GetLonLatBounds(extent);

        var xMin = ContourTileMath.LonToTileX(west, zoom);
        var xMax = ContourTileMath.LonToTileX(east, zoom);
        var yMin = ContourTileMath.LatToTileY(north, zoom);
        var yMax = ContourTileMath.LatToTileY(south, zoom);

        if (xMin > xMax || yMin > yMax)
        {
            skipReason = "invalid viewport bounds";
            return TileRange.Empty;
        }

        xMin = Math.Max(0, xMin - 1);
        yMin = Math.Max(0, yMin - 1);
        xMax = Math.Min((1 << zoom) - 1, xMax + 1);
        yMax = Math.Min((1 << zoom) - 1, yMax + 1);

        tileCount = (xMax - xMin + 1) * (yMax - yMin + 1);
        if (tileCount > 500)
        {
            skipReason = $"tile range too large ({tileCount} > 500)";
            return TileRange.Empty;
        }

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

    private static (double West, double South, double East, double North) GetLonLatBounds(Envelope envelope)
    {
        var (minLon, minLat) = SphericalMercator.ToLonLat(envelope.MinX, envelope.MinY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(envelope.MaxX, envelope.MaxY);

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

    private static TileRange GetOfflineTileRange((double West, double South, double East, double North) bounds, int zoom)
    {
        var xMin = ContourTileMath.LonToTileX(bounds.West, zoom);
        var xMax = ContourTileMath.LonToTileX(bounds.East, zoom);
        var yMin = ContourTileMath.LatToTileY(bounds.North, zoom);
        var yMax = ContourTileMath.LatToTileY(bounds.South, zoom);

        if (xMin > xMax)
            (xMin, xMax) = (xMax, xMin);
        if (yMin > yMax)
            (yMin, yMax) = (yMax, yMin);

        return new TileRange(xMin, xMax, yMin, yMax);
    }

    private static List<(int X, int Y)> GetIntersectingTiles(TileRange range, int zoom, IPreparedGeometry? preparedSelection)
    {
        if (range.IsEmpty)
            return new List<(int X, int Y)>();

        var tiles = new List<(int X, int Y)>((range.MaxX - range.MinX + 1) * (range.MaxY - range.MinY + 1));
        for (var x = range.MinX; x <= range.MaxX; x++)
        {
            for (var y = range.MinY; y <= range.MaxY; y++)
            {
                if (preparedSelection is not null && !IntersectsTile(preparedSelection, zoom, x, y))
                    continue;

                tiles.Add((x, y));
            }
        }

        return tiles;
    }

    private static bool IntersectsTile(IPreparedGeometry preparedSelection, int zoom, int x, int y)
    {
        var bounds = ContourTileMath.TileToBounds(x, y, zoom);
        var (westX, southY) = SphericalMercator.FromLonLat(bounds.West, bounds.South);
        var (eastX, northY) = SphericalMercator.FromLonLat(bounds.East, bounds.North);
        var minX = Math.Min(westX, eastX);
        var maxX = Math.Max(westX, eastX);
        var minY = Math.Min(southY, northY);
        var maxY = Math.Max(southY, northY);

        var tilePolygon = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        }));
        return preparedSelection.Intersects(tilePolygon);
    }

    private IReadOnlyList<OfflineTileSource> BuildOfflineTileSources(OfflineCacheBuildOptions options)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter");
        var sources = new List<OfflineTileSource>();

        if (options.IncludeOsm)
        {
            sources.Add(new OfflineTileSource(
                "OSM",
                Path.Combine(root, "tilecache"),
                "png",
                0,
                18,
                static (z, x, y) => $"https://tile.openstreetmap.org/{z}/{x}/{y}.png"));
        }

        if (options.IncludeTerrain)
        {
            sources.Add(new OfflineTileSource(
                "Terrain",
                Path.Combine(root, "terrain-cache"),
                "png",
                0,
                17,
                static (z, x, y) => $"https://tile.opentopomap.org/{z}/{x}/{y}.png"));
        }

        if (options.IncludeSatellite)
        {
            sources.Add(new OfflineTileSource(
                "Satellite",
                Path.Combine(root, "satellite-cache"),
                "jpg",
                0,
                18,
                static (z, x, y) => $"https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"));
        }

        return sources;
    }

    private async Task<(int Downloaded, int Skipped, int Failed)> CacheBaseTilesAsync(
        OfflineTileSource source,
        int zoom,
        IReadOnlyList<(int X, int Y)> tiles,
        CancellationToken cancellationToken)
    {
        if (tiles.Count == 0)
            return (0, 0, 0);

        var downloaded = 0;
        var skipped = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            tiles,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (coordinate, token) =>
            {
                var result = await EnsureBaseTileCachedAsync(source, zoom, coordinate.X, coordinate.Y, token);
                switch (result)
                {
                    case TileCacheWriteResult.Downloaded:
                        Interlocked.Increment(ref downloaded);
                        break;
                    case TileCacheWriteResult.Skipped:
                        Interlocked.Increment(ref skipped);
                        break;
                    default:
                        Interlocked.Increment(ref failed);
                        break;
                }
            });

        return (downloaded, skipped, failed);
    }

    private async Task<TileCacheWriteResult> EnsureBaseTileCachedAsync(
        OfflineTileSource source,
        int zoom,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        var targetDir = Path.Combine(
            source.CacheRoot,
            zoom.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(targetDir);

        var outputPath = Path.Combine(targetDir, $"{y}.{source.Extension}");
        if (File.Exists(outputPath))
            return TileCacheWriteResult.Skipped;

        var tmpPath = outputPath + ".tmp";
        try
        {
            var url = source.BuildUrl(zoom, x, y);
            using var response = await _offlineTileHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return TileCacheWriteResult.Failed;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var file = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }

            File.Move(tmpPath, outputPath, overwrite: true);
            return TileCacheWriteResult.Downloaded;
        }
        catch
        {
            return TileCacheWriteResult.Failed;
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private void SetOfflineCacheStatus(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetOfflineCacheStatus(text));
            return;
        }

        if (string.Equals(_offlineCacheStatusText, text, StringComparison.Ordinal))
            return;

        _offlineCacheStatusText = text;
        OnPropertyChanged(nameof(OfflineCacheStatusText));
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
        if (Interlocked.Exchange(ref _contourRefreshScheduled, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120);
            }
            catch
            {
                // Ignore delay cancellation/failures and try to refresh anyway.
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    foreach (var layer in _contourLayers.Values)
                    {
                        layer.ClearCache();
                    }
                    Map.Refresh(ChangeType.Discrete);
                }
                finally
                {
                    Interlocked.Exchange(ref _contourRefreshScheduled, 0);
                }
            });
        });
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
            if (sample.DeviceId != 0)
                feature[NodeIdField] = sample.DeviceId;
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
                bucket = new ClusterBucket();
                clusters.Add(bucket);
            }
            bucket.Add(sample.DeviceId, sample.Point);
        }

        var features = new List<IFeature>();
        foreach (var cluster in clusters)
        {
            var feature = new PointFeature(cluster.Center);
            if (cluster.TryGetSingleNodeId(out var nodeId))
                feature[NodeIdField] = nodeId;
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(Color.FromArgb(200, 245, 158, 11)),
                Outline = new Pen(Color.FromArgb(255, 180, 83, 9), 1),
                SymbolScale = 1.6,
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = cluster.UniqueNodeCount.ToString(),
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
            if (sample.DeviceId != 0)
                feature[NodeIdField] = sample.DeviceId;
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

    private IEnumerable<IFeature> BuildOfflineRouteFeatures(List<OfflineRoute> routes, string? selectedRouteId)
    {
        var features = new List<IFeature>(routes.Count);
        foreach (var route in routes)
        {
            if (route.Points.Count < 2)
                continue;

            var coords = route.Points
                .Select(p => new Coordinate(p.X, p.Y))
                .ToArray();
            if (coords.Length < 2)
                continue;

            var isSelected = string.Equals(route.Id, selectedRouteId, StringComparison.Ordinal);
            var feature = new GeometryFeature
            {
                Geometry = new LineString(coords),
            };
            feature[OfflineRouteIdField] = route.Id;
            feature.Styles.Add(new VectorStyle
            {
                Line = isSelected
                    ? new Pen(Color.FromArgb(255, 255, 209, 102), 4)
                    : new Pen(Color.FromArgb(220, 244, 114, 182), 3),
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = route.Name,
                ForeColor = Color.FromArgb(255, 245, 250, 255),
                BackColor = new Brush(isSelected
                    ? Color.FromArgb(215, 20, 38, 56)
                    : Color.FromArgb(180, 23, 32, 44)),
                Offset = new Offset(10, -8),
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Top,
                Font = new Font { Size = isSelected ? 11 : 10, Bold = isSelected },
            });
            features.Add(feature);
        }

        return features;
    }

    private void RefreshImportedRoutesLayer()
    {
        RequestLayerRefresh();
        Map.Refresh(ChangeType.Discrete);
    }

    private static List<MPoint> ParseKmlTrackPoints(string filePath)
    {
        var document = XDocument.Load(filePath, LoadOptions.None);
        var points = new List<MPoint>();

        foreach (var lineString in document.Descendants().Where(e => e.Name.LocalName == "LineString"))
        {
            foreach (var coordinatesElement in lineString.Elements().Where(e => e.Name.LocalName == "coordinates"))
            {
                ParseCoordinateLines(coordinatesElement.Value, points);
            }
        }

        foreach (var gxTrack in document.Descendants().Where(e => e.Name.LocalName == "Track"))
        {
            foreach (var gxCoordElement in gxTrack.Elements().Where(e => e.Name.LocalName == "coord"))
            {
                ParseGxCoordLine(gxCoordElement.Value, points);
            }
        }

        // Fallback: if no LineString/Track was found, try generic coordinates nodes.
        if (points.Count == 0)
        {
            foreach (var coordinatesElement in document.Descendants().Where(e => e.Name.LocalName == "coordinates"))
            {
                ParseCoordinateLines(coordinatesElement.Value, points);
            }
            foreach (var gxCoordElement in document.Descendants().Where(e => e.Name.LocalName == "coord"))
            {
                ParseGxCoordLine(gxCoordElement.Value, points);
            }
        }

        // Remove consecutive duplicates to reduce route noise.
        for (var i = points.Count - 1; i >= 1; i--)
        {
            if (Math.Abs(points[i].X - points[i - 1].X) < 0.01 &&
                Math.Abs(points[i].Y - points[i - 1].Y) < 0.01)
            {
                points.RemoveAt(i);
            }
        }

        return points;
    }

    private static void ParseCoordinateLines(string raw, List<MPoint> output)
    {
        var entries = raw
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                continue;
            if (!IsValidLonLat(lon, lat))
                continue;

            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            output.Add(new MPoint(x, y));
        }
    }

    private static void ParseGxCoordLine(string raw, List<MPoint> output)
    {
        var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            return;
        if (!IsValidLonLat(lon, lat))
            return;

        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        output.Add(new MPoint(x, y));
    }

    private static bool IsValidLonLat(double lon, double lat)
    {
        return IsFinite(lon) &&
               IsFinite(lat) &&
               lon >= -180 &&
               lon <= 180 &&
               lat >= -90 &&
               lat <= 90;
    }

    private void RefreshOfflineSelectionOverlay()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshOfflineSelectionOverlay);
            return;
        }

        var selectionGeometry = _offlineCacheSelectionGeometry;
        if (selectionGeometry is null)
        {
            _offlineSelectionLayer.Features = Array.Empty<IFeature>();
            Map.Refresh(ChangeType.Discrete);
            return;
        }

        _offlineSelectionLayer.Features = BuildOfflineSelectionFeatures(selectionGeometry, _offlineCacheSelectionName);
        Map.Refresh(ChangeType.Discrete);
    }

    private static IEnumerable<IFeature> BuildOfflineSelectionFeatures(
        Geometry geometry,
        string? selectionName)
    {
        var boundaryGeometry = NormalizeSelectionBoundaryLineGeometry(geometry);

        var feature = new GeometryFeature
        {
            Geometry = boundaryGeometry,
        };
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromArgb(255, 83, 199, 255), 2.2f),
        });

        if (string.IsNullOrWhiteSpace(selectionName))
            return new[] { feature };

        var envelope = geometry.EnvelopeInternal;
        var labelFeature = new PointFeature(envelope.MinX, envelope.MaxY);
        labelFeature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(Color.FromArgb(0, 0, 0, 0)),
            Outline = new Pen(Color.FromArgb(0, 0, 0, 0), 0),
            SymbolScale = 0.0,
        });
        labelFeature.Styles.Add(new LabelStyle
        {
            Text = selectionName.Trim(),
            ForeColor = Color.FromArgb(255, 232, 244, 255),
            BackColor = new Brush(Color.FromArgb(200, 15, 28, 43)),
            Offset = new Offset(8, -8),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Left,
            VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Top,
            Font = new Font { Size = 11, Bold = true },
        });

        return new IFeature[] { feature, labelFeature };
    }

    private static Geometry NormalizeSelectionBoundaryLineGeometry(Geometry geometry)
    {
        var lineParts = new List<LineString>();
        CollectLineStringBoundaries(geometry, lineParts);
        if (lineParts.Count == 0)
        {
            CollectLineStringBoundaries(geometry.Boundary, lineParts);
        }

        if (lineParts.Count == 1)
            return lineParts[0];
        if (lineParts.Count > 1)
            return new MultiLineString(lineParts.ToArray());

        var boundary = geometry.Boundary;
        if (boundary is not null && !boundary.IsEmpty)
            return boundary;
        return geometry;
    }

    private static void CollectLineStringBoundaries(Geometry? source, List<LineString> output)
    {
        if (source is null || source.IsEmpty)
            return;

        switch (source)
        {
            case Polygon polygon:
            {
                var shell = polygon.ExteriorRing;
                if (shell is not null && !shell.IsEmpty)
                {
                    output.Add(new LineString(shell.Coordinates));
                }
                return;
            }
            case MultiPolygon multiPolygon:
            {
                foreach (var child in multiPolygon.Geometries.OfType<Geometry>())
                {
                    CollectLineStringBoundaries(child, output);
                }
                return;
            }
            case LinearRing ring:
            {
                output.Add(new LineString(ring.Coordinates));
                return;
            }
            case LineString line:
            {
                output.Add(line);
                return;
            }
            case MultiLineString multiLine:
            {
                foreach (var child in multiLine.Geometries.OfType<LineString>())
                {
                    output.Add(child);
                }
                return;
            }
            case GeometryCollection collection:
            {
                foreach (var child in collection.Geometries.OfType<Geometry>())
                {
                    CollectLineStringBoundaries(child, output);
                }
                return;
            }
        }
    }

    private void SetOfflineCacheSelectionGeometryCore(
        Geometry geometry,
        (double West, double South, double East, double North) bounds,
        string? selectionName,
        string? statusText)
    {
        _offlineCacheSelectionGeometry = geometry.Copy();
        _offlineCacheSelectionBounds = bounds;
        _offlineCacheSelectionName = string.IsNullOrWhiteSpace(selectionName)
            ? null
            : selectionName.Trim();
        RefreshOfflineSelectionOverlay();
        OnPropertyChanged(nameof(HasOfflineCacheSelection));
        OnPropertyChanged(nameof(CanRunOfflineCache));
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetOfflineCacheStatus(statusText);
        }
    }

    private static Polygon CreateRectPolygon((double West, double South, double East, double North) bounds)
    {
        var (westX, southY) = SphericalMercator.FromLonLat(bounds.West, bounds.South);
        var (eastX, northY) = SphericalMercator.FromLonLat(bounds.East, bounds.North);
        var minX = Math.Min(westX, eastX);
        var maxX = Math.Max(westX, eastX);
        var minY = Math.Min(southY, northY);
        var maxY = Math.Max(southY, northY);
        return new Polygon(new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        }));
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
            httpClient: null)
        {
            Name = "basemap-osm",
            Enabled = true,
        };
    }

    private TileLayer CreateTerrainLayer()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter", "terrain-cache");
        Directory.CreateDirectory(cacheDir);

        var schema = new GlobalSphericalMercator(0, 17, "EPSG:3857");
        var fileCache = new FileCache(cacheDir, "png");
        var url = "https://tile.opentopomap.org/{z}/{x}/{y}.png";
        var tileSource = new HttpTileSource(schema, url, name: "terrain-opentopomap", persistentCache: fileCache);

        return new TileLayer(
            tileSource,
            minTiles: 100,
            maxTiles: 400,
            dataFetchStrategy: new MinimalDataFetchStrategy(),
            renderFetchStrategy: new MinimalRenderFetchStrategy(),
            minExtraTiles: 0,
            maxExtraTiles: 0,
            fetchTileAsFeature: null,
            httpClient: null)
        {
            Name = "basemap-terrain",
            Opacity = 0.95f,
            Enabled = false,
        };
    }

    private TileLayer CreateSatelliteLayer()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter", "satellite-cache");
        Directory.CreateDirectory(cacheDir);

        var schema = new GlobalSphericalMercator(0, 19, "EPSG:3857");
        var fileCache = new FileCache(cacheDir, "jpg");
        var url = "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
        var tileSource = new HttpTileSource(schema, url, name: "satellite-esri-world-imagery", persistentCache: fileCache);

        return new TileLayer(
            tileSource,
            minTiles: 100,
            maxTiles: 400,
            dataFetchStrategy: new MinimalDataFetchStrategy(),
            renderFetchStrategy: new MinimalRenderFetchStrategy(),
            minExtraTiles: 0,
            maxExtraTiles: 0,
            fetchTileAsFeature: null,
            httpClient: null)
        {
            Name = "basemap-satellite",
            Enabled = false,
        };
    }

    private TileLayer CreateGibsLayer()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrailMateCenter", "gibs-cache");
        Directory.CreateDirectory(cacheDir);

        var schema = new GlobalSphericalMercator(0, 9, "EPSG:3857");
        var fileCache = new FileCache(cacheDir, "jpg");
        var date = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/VIIRS_SNPP_CorrectedReflectance_TrueColor/default/{date}/GoogleMapsCompatible_Level9/{{z}}/{{y}}/{{x}}.jpg";
        var tileSource = new HttpTileSource(schema, url, name: "nasa-gibs", persistentCache: fileCache);

        return new TileLayer(
            tileSource,
            minTiles: 80,
            maxTiles: 320,
            dataFetchStrategy: new MinimalDataFetchStrategy(),
            renderFetchStrategy: new MinimalRenderFetchStrategy(),
            minExtraTiles: 0,
            maxExtraTiles: 0,
            fetchTileAsFeature: null,
            httpClient: null)
        {
            Name = "nasa-gibs",
            Opacity = 0.65f,
            Enabled = false,
        };
    }

    private sealed record MapSample(uint DeviceId, MPoint Point, MapSampleSource Source);
    private sealed record WaypointSample(uint DeviceId, MPoint Point, string? Label, MapSampleSource Source);
    private sealed record OfflineRoute(string Id, string Name, IReadOnlyList<MPoint> Points)
    {
        public (double West, double South, double East, double North) Bounds
        {
            get
            {
                if (Points.Count == 0)
                    return (0, 0, 0, 0);

                var minX = Points.Min(p => p.X);
                var maxX = Points.Max(p => p.X);
                var minY = Points.Min(p => p.Y);
                var maxY = Points.Max(p => p.Y);
                var (west, south) = SphericalMercator.ToLonLat(minX, minY);
                var (east, north) = SphericalMercator.ToLonLat(maxX, maxY);
                return ClampBounds((west, south, east, north));
            }
        }
    }

    private sealed class BulkUpdateScope : IDisposable
    {
        private MapViewModel? _owner;

        public BulkUpdateScope(MapViewModel owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndBulkUpdate();
        }
    }

    private sealed class ClusterBucket
    {
        private readonly HashSet<uint> _uniqueNodeIds = new();
        private bool _hasUnknownNode;
        private double _sumX;
        private double _sumY;

        public int SampleCount { get; private set; }
        public int UniqueNodeCount => _uniqueNodeIds.Count + (_hasUnknownNode ? 1 : 0);

        public MPoint Center => new(_sumX / SampleCount, _sumY / SampleCount);

        public void Add(uint deviceId, MPoint point)
        {
            _sumX += point.X;
            _sumY += point.Y;
            SampleCount++;

            if (deviceId == 0)
            {
                _hasUnknownNode = true;
                return;
            }

            _uniqueNodeIds.Add(deviceId);
        }

        public double DistanceTo(MPoint point)
        {
            var dx = point.X - Center.X;
            var dy = point.Y - Center.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool TryGetSingleNodeId(out uint nodeId)
        {
            if (!_hasUnknownNode && _uniqueNodeIds.Count == 1)
            {
                nodeId = _uniqueNodeIds.First();
                return true;
            }

            nodeId = 0;
            return false;
        }
    }

    private readonly record struct TileRange(int MinX, int MaxX, int MinY, int MaxY)
    {
        public static TileRange Empty => new(0, -1, 0, -1);
        public bool IsEmpty => MaxX < MinX || MaxY < MinY;
    }

    private readonly record struct OfflineTileSource(
        string DisplayName,
        string CacheRoot,
        string Extension,
        int MinZoom,
        int MaxZoom,
        Func<int, int, int, string> BuildUrl);

    private enum TileCacheWriteResult
    {
        Downloaded = 0,
        Skipped = 1,
        Failed = 2,
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

    public readonly record struct OfflineRouteImportResult(
        bool Success,
        string? RouteName,
        int PointCount,
        string? ErrorMessage);

}
