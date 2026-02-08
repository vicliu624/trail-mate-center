using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Fetcher;
using Mapsui.Tiling.Layers;
using Mapsui.Tiling.Rendering;
using Mapsui.Projections;
using BruTile.Cache;
using BruTile.Predefined;
using NetTopologySuite.Geometries;
using Mapsui.Nts;
using System.Collections.Generic;
using System.Linq;

namespace TrailMateCenter.ViewModels;

public sealed class MapViewModel
{
    private readonly object _gate = new();
    private readonly List<MapSample> _samples = new();
    private readonly List<WaypointSample> _waypoints = new();
    private readonly MemoryLayer _pointLayer;
    private readonly MemoryLayer _trackLayer;
    private readonly MemoryLayer _clusterLayer;
    private readonly MemoryLayer _waypointLayer;

    public MapViewModel()
    {
        Map = new Map
        {
            CRS = "EPSG:3857",
            BackColor = Color.FromArgb(255, 240, 240, 240),
        };

        var tileLayer = CreateOsmLayer();
        Map.Layers.Add(tileLayer);

        _trackLayer = new MemoryLayer { Name = "tracks" };
        _pointLayer = new MemoryLayer { Name = "points" };
        _clusterLayer = new MemoryLayer { Name = "clusters" };
        _waypointLayer = new MemoryLayer { Name = "waypoints" };

        Map.Layers.Add(_trackLayer);
        Map.Layers.Add(_clusterLayer);
        Map.Layers.Add(_pointLayer);
        Map.Layers.Add(_waypointLayer);

        Map.Navigator.ViewportChanged += (_, _) => RefreshLayers();
    }

    public Map Map { get; }

    public bool FollowLatest { get; set; } = true;
    public bool EnableCluster { get; set; } = true;
    public int ClusterRadiusPx { get; set; } = 60;

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
                Line = new Pen(Color.FromArgb(255, 30, 136, 229), 2),
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
                Fill = new Brush(Color.FromArgb(220, 33, 150, 243)),
                Outline = new Pen(Color.FromArgb(255, 25, 118, 210), 1),
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
                Fill = new Brush(Color.FromArgb(200, 255, 167, 38)),
                Outline = new Pen(Color.FromArgb(255, 245, 124, 0), 1),
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

    private static TileLayer CreateOsmLayer()
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrailMateCenter", "tilecache");
        Directory.CreateDirectory(cacheDir);
        var fileCache = new FileCache(cacheDir, "png");
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap, "TrailMateCenter", fileCache, null, 0, 19);

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
}
