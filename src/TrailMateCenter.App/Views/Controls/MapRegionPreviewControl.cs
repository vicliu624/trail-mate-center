using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mapsui.Projections;

namespace TrailMateCenter.Views.Controls;

public sealed class MapRegionPreviewControl : Control
{
    private const double PaddingSize = 10d;
    private const int MaxRingPoints = 520;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(22, 31, 30));
    private static readonly IBrush WaterBrush = new SolidColorBrush(Color.FromArgb(92, 37, 99, 134));
    private static readonly IBrush TerrainBrush = new SolidColorBrush(Color.FromArgb(72, 91, 125, 72));
    private static readonly IBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(92, 88, 221, 128));
    private static readonly IBrush RectangleFillBrush = new SolidColorBrush(Color.FromArgb(72, 82, 176, 255));
    private static readonly IBrush EmptyFillBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), 1);
    private static readonly IPen RidgePen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 214, 135)), 1);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(134, 239, 172)), 2);
    private static readonly IPen RectanglePen = new Pen(new SolidColorBrush(Color.FromRgb(96, 165, 250)), 2);
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromArgb(95, 180, 194, 206)), 1);

    private string? _cachedGeoJson;
    private IReadOnlyList<IReadOnlyList<GeoPoint>> _cachedRings = Array.Empty<IReadOnlyList<GeoPoint>>();

    public static readonly StyledProperty<double> WestProperty =
        AvaloniaProperty.Register<MapRegionPreviewControl, double>(nameof(West));

    public static readonly StyledProperty<double> SouthProperty =
        AvaloniaProperty.Register<MapRegionPreviewControl, double>(nameof(South));

    public static readonly StyledProperty<double> EastProperty =
        AvaloniaProperty.Register<MapRegionPreviewControl, double>(nameof(East));

    public static readonly StyledProperty<double> NorthProperty =
        AvaloniaProperty.Register<MapRegionPreviewControl, double>(nameof(North));

    public static readonly StyledProperty<string?> BoundaryGeoJsonProperty =
        AvaloniaProperty.Register<MapRegionPreviewControl, string?>(nameof(BoundaryGeoJson));

    static MapRegionPreviewControl()
    {
        AffectsRender<MapRegionPreviewControl>(
            WestProperty,
            SouthProperty,
            EastProperty,
            NorthProperty,
            BoundaryGeoJsonProperty);
    }

    public double West
    {
        get => GetValue(WestProperty);
        set => SetValue(WestProperty, value);
    }

    public double South
    {
        get => GetValue(SouthProperty);
        set => SetValue(SouthProperty, value);
    }

    public double East
    {
        get => GetValue(EastProperty);
        set => SetValue(EastProperty, value);
    }

    public double North
    {
        get => GetValue(NorthProperty);
        set => SetValue(NorthProperty, value);
    }

    public string? BoundaryGeoJson
    {
        get => GetValue(BoundaryGeoJsonProperty);
        set => SetValue(BoundaryGeoJsonProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundaryGeoJsonProperty)
        {
            _cachedGeoJson = null;
            _cachedRings = Array.Empty<IReadOnlyList<GeoPoint>>();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using var clip = context.PushClip(rect);
        DrawMapBackground(context, rect);

        var viewport = ResolveProjectedViewport();
        if (!viewport.HasValue)
        {
            DrawFallbackSelection(context, rect);
            DrawFrame(context, rect);
            return;
        }

        var content = FitProjectedViewport(rect.Deflate(PaddingSize), viewport.Value);
        var rings = GetBoundaryRings();
        if (rings.Count > 0)
        {
            DrawBoundarySelection(context, content, viewport.Value, rings);
        }
        else
        {
            var selectionRect = content.Deflate(Math.Min(content.Width, content.Height) * 0.08);
            context.DrawRectangle(RectangleFillBrush, RectanglePen, selectionRect, 4, 4);
        }

        DrawFrame(context, rect);
    }

    private static void DrawMapBackground(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(BackgroundBrush, null, rect, 5, 5);

        var water = new StreamGeometry();
        using (var stream = water.Open())
        {
            stream.BeginFigure(new Point(rect.Left, rect.Bottom * 0.62), true);
            stream.LineTo(new Point(rect.Right * 0.28, rect.Bottom * 0.58));
            stream.LineTo(new Point(rect.Right * 0.48, rect.Bottom));
            stream.LineTo(new Point(rect.Left, rect.Bottom));
            stream.EndFigure(true);
        }

        context.DrawGeometry(WaterBrush, null, water);

        var terrain = new StreamGeometry();
        using (var stream = terrain.Open())
        {
            stream.BeginFigure(new Point(rect.Right * 0.36, rect.Top), true);
            stream.LineTo(new Point(rect.Right, rect.Top));
            stream.LineTo(new Point(rect.Right, rect.Bottom * 0.42));
            stream.LineTo(new Point(rect.Right * 0.58, rect.Bottom * 0.56));
            stream.EndFigure(true);
        }

        context.DrawGeometry(TerrainBrush, null, terrain);

        for (var x = rect.Left + 18; x < rect.Right; x += 18)
            context.DrawLine(GridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
        for (var y = rect.Top + 18; y < rect.Bottom; y += 18)
            context.DrawLine(GridPen, new Point(rect.Left, y), new Point(rect.Right, y));
        for (var x = rect.Left - rect.Height; x < rect.Right; x += 24)
            context.DrawLine(RidgePen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
    }

    private void DrawBoundarySelection(
        DrawingContext context,
        Rect content,
        ProjectedViewport viewport,
        IReadOnlyList<IReadOnlyList<GeoPoint>> rings)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            foreach (var ring in rings)
            {
                var points = ProjectRing(ring, content, viewport);
                if (points.Count < 3)
                    continue;

                stream.BeginFigure(points[0], true);
                for (var i = 1; i < points.Count; i++)
                    stream.LineTo(points[i]);
                stream.EndFigure(true);
            }
        }

        context.DrawGeometry(SelectionFillBrush, SelectionPen, geometry);
    }

    private static void DrawFallbackSelection(DrawingContext context, Rect rect)
    {
        var selection = rect.Deflate(Math.Min(rect.Width, rect.Height) * 0.18);
        context.DrawRectangle(EmptyFillBrush, RectanglePen, selection, 4, 4);
    }

    private static void DrawFrame(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(null, FramePen, rect.Deflate(0.5), 5, 5);
    }

    private ProjectedViewport? ResolveProjectedViewport()
    {
        if (IsValidLonLat(West, South) &&
            IsValidLonLat(East, North) &&
            East > West &&
            North > South)
        {
            var (westX, southY) = SphericalMercator.FromLonLat(West, South);
            var (eastX, northY) = SphericalMercator.FromLonLat(East, North);
            return ProjectedViewport.Create(westX, southY, eastX, northY);
        }

        var rings = GetBoundaryRings();
        return rings.Count == 0 ? null : ProjectedViewport.FromRings(rings);
    }

    private static Rect FitProjectedViewport(Rect destination, ProjectedViewport viewport)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return destination;

        var scale = Math.Min(destination.Width / viewport.Width, destination.Height / viewport.Height);
        var width = viewport.Width * scale;
        var height = viewport.Height * scale;
        var x = destination.X + ((destination.Width - width) * 0.5);
        var y = destination.Y + ((destination.Height - height) * 0.5);
        return new Rect(x, y, width, height);
    }

    private static List<Point> ProjectRing(
        IReadOnlyList<GeoPoint> ring,
        Rect content,
        ProjectedViewport viewport)
    {
        var points = new List<Point>();
        var step = Math.Max(1, ring.Count / MaxRingPoints);
        for (var i = 0; i < ring.Count; i += step)
        {
            var point = ring[i];
            if (TryProject(point, content, viewport, out var screenPoint))
                points.Add(screenPoint);
        }

        if (points.Count > 0 && points[0] != points[^1])
            points.Add(points[0]);

        return points;
    }

    private static bool TryProject(
        GeoPoint point,
        Rect content,
        ProjectedViewport viewport,
        out Point screenPoint)
    {
        screenPoint = default;
        if (!IsValidLonLat(point.Lon, point.Lat) || viewport.Width <= 0 || viewport.Height <= 0)
            return false;

        var (x, y) = SphericalMercator.FromLonLat(point.Lon, point.Lat);
        var normalizedX = (x - viewport.MinX) / viewport.Width;
        var normalizedY = (viewport.MaxY - y) / viewport.Height;
        if (double.IsNaN(normalizedX) || double.IsNaN(normalizedY))
            return false;

        screenPoint = new Point(
            content.X + (Math.Clamp(normalizedX, -0.2, 1.2) * content.Width),
            content.Y + (Math.Clamp(normalizedY, -0.2, 1.2) * content.Height));
        return true;
    }

    private IReadOnlyList<IReadOnlyList<GeoPoint>> GetBoundaryRings()
    {
        var geoJson = BoundaryGeoJson;
        if (string.Equals(_cachedGeoJson, geoJson, StringComparison.Ordinal))
            return _cachedRings;

        _cachedGeoJson = geoJson;
        _cachedRings = TryParseBoundaryRings(geoJson);
        return _cachedRings;
    }

    private static IReadOnlyList<IReadOnlyList<GeoPoint>> TryParseBoundaryRings(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            return Array.Empty<IReadOnlyList<GeoPoint>>();

        try
        {
            using var document = JsonDocument.Parse(geoJson);
            var rings = new List<IReadOnlyList<GeoPoint>>();
            CollectGeoJsonRings(document.RootElement, rings);
            return rings;
        }
        catch
        {
            return Array.Empty<IReadOnlyList<GeoPoint>>();
        }
    }

    private static void CollectGeoJsonRings(JsonElement element, List<IReadOnlyList<GeoPoint>> output)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var type = typeElement.GetString();
        if (string.Equals(type, "FeatureCollection", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("features", out var features) &&
            features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
                CollectGeoJsonRings(feature, output);
            return;
        }

        if (string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("geometry", out var geometry))
        {
            CollectGeoJsonRings(geometry, output);
            return;
        }

        if (string.Equals(type, "GeometryCollection", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("geometries", out var geometries) &&
            geometries.ValueKind == JsonValueKind.Array)
        {
            foreach (var childGeometry in geometries.EnumerateArray())
                CollectGeoJsonRings(childGeometry, output);
            return;
        }

        if (!element.TryGetProperty("coordinates", out var coordinates))
            return;

        if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
        {
            CollectPolygonRings(coordinates, output);
            return;
        }

        if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
            coordinates.ValueKind == JsonValueKind.Array)
        {
            foreach (var polygonElement in coordinates.EnumerateArray())
                CollectPolygonRings(polygonElement, output);
        }
    }

    private static void CollectPolygonRings(JsonElement polygonElement, List<IReadOnlyList<GeoPoint>> output)
    {
        if (polygonElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var ringElement in polygonElement.EnumerateArray())
        {
            var ring = ParseRing(ringElement);
            if (ring.Count >= 4)
                output.Add(ring);
        }
    }

    private static IReadOnlyList<GeoPoint> ParseRing(JsonElement ringElement)
    {
        if (ringElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<GeoPoint>();

        var points = new List<GeoPoint>();
        foreach (var pointElement in ringElement.EnumerateArray())
        {
            if (TryReadGeoJsonPosition(pointElement, out var lon, out var lat))
                points.Add(new GeoPoint(lon, lat));
        }

        if (points.Count > 0 && !points[0].AlmostEquals(points[^1]))
            points.Add(points[0]);

        return points;
    }

    private static bool TryReadGeoJsonPosition(JsonElement pointElement, out double lon, out double lat)
    {
        lon = 0;
        lat = 0;
        if (pointElement.ValueKind != JsonValueKind.Array)
            return false;

        var values = pointElement.EnumerateArray().Take(2).ToArray();
        if (values.Length < 2)
            return false;
        if (!TryReadJsonDouble(values[0], out lon))
            return false;
        if (!TryReadJsonDouble(values[1], out lat))
            return false;

        return IsValidLonLat(lon, lat);
    }

    private static bool TryReadJsonDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);
        if (element.ValueKind == JsonValueKind.String)
            return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        value = 0;
        return false;
    }

    private static bool IsValidLonLat(double lon, double lat)
    {
        return !double.IsNaN(lon) &&
               !double.IsInfinity(lon) &&
               !double.IsNaN(lat) &&
               !double.IsInfinity(lat) &&
               lon is >= -180 and <= 180 &&
               lat is >= -90 and <= 90;
    }

    private readonly record struct GeoPoint(double Lon, double Lat)
    {
        public bool AlmostEquals(GeoPoint other)
        {
            return Math.Abs(Lon - other.Lon) < 0.0000001 &&
                   Math.Abs(Lat - other.Lat) < 0.0000001;
        }
    }

    private readonly record struct ProjectedViewport(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public static ProjectedViewport Create(double x1, double y1, double x2, double y2)
        {
            return new ProjectedViewport(
                Math.Min(x1, x2),
                Math.Min(y1, y2),
                Math.Max(x1, x2),
                Math.Max(y1, y2));
        }

        public static ProjectedViewport? FromRings(IReadOnlyList<IReadOnlyList<GeoPoint>> rings)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            foreach (var ring in rings)
            {
                foreach (var point in ring)
                {
                    if (!IsValidLonLat(point.Lon, point.Lat))
                        continue;

                    var (x, y) = SphericalMercator.FromLonLat(point.Lon, point.Lat);
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            return double.IsInfinity(minX) || minX >= maxX || minY >= maxY
                ? null
                : new ProjectedViewport(minX, minY, maxX, maxY);
        }
    }
}
