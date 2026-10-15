using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Views.Controls;

public sealed class OutdoorMapPreviewControl : Control
{
    public static readonly StyledProperty<TmapOutdoorPreview?> DataProperty = AvaloniaProperty.Register<OutdoorMapPreviewControl, TmapOutdoorPreview?>(nameof(Data));
    public static readonly StyledProperty<bool> ShowRoadsProperty = AvaloniaProperty.Register<OutdoorMapPreviewControl, bool>(nameof(ShowRoads), true);
    public static readonly StyledProperty<bool> ShowContoursProperty = AvaloniaProperty.Register<OutdoorMapPreviewControl, bool>(nameof(ShowContours), true);
    static OutdoorMapPreviewControl() => AffectsRender<OutdoorMapPreviewControl>(DataProperty, ShowRoadsProperty, ShowContoursProperty);
    public TmapOutdoorPreview? Data { get => GetValue(DataProperty); set { SetValue(DataProperty, value); ResetView(); } }
    public bool ShowRoads { get => GetValue(ShowRoadsProperty); set => SetValue(ShowRoadsProperty, value); }
    public bool ShowContours { get => GetValue(ShowContoursProperty); set => SetValue(ShowContoursProperty, value); }
    private double _zoom = 1;
    private Vector _pan;
    private Point? _drag;
    public void ResetView() { _zoom = 1; _pan = default; InvalidateVisual(); }
    private static double Mercator(double latitude) => Math.Log(Math.Tan(Math.PI / 4 + Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 360)) * 180 / Math.PI;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101C20")), null, area);
        if (Data is not { } data || area.Width < 1 || area.Height < 1) return;
        var north = Mercator(data.Bounds.North); var south = Mercator(data.Bounds.South);
        var spanX = data.Bounds.East - data.Bounds.West; var spanY = north - south;
        if (spanX <= 0 || spanY <= 0) return;
        var scale = Math.Min(Math.Max(1, area.Width - 32) / spanX, Math.Max(1, area.Height - 32) / spanY) * _zoom;
        var middleX = (data.Bounds.East + data.Bounds.West) / 2; var middleY = (north + south) / 2;
        Point Project(TmapPreviewPoint p) => new(area.Center.X + _pan.X + (p.Longitude - middleX) * scale,
            area.Center.Y + _pan.Y + (middleY - Mercator(p.Latitude)) * scale);
        using (context.PushClip(area))
        {
            if (ShowContours) Draw(data.Contours, new Pen(new SolidColorBrush(Color.Parse("#B8A36F")), 1));
            if (ShowRoads) Draw(data.Roads, new Pen(new SolidColorBrush(Color.Parse("#6AD4C2")), 1.5));
        }
        void Draw(IReadOnlyList<TmapPreviewLine> lines, IPen pen)
        {
            foreach (var line in lines)
            {
                if (line.Points.Count < 2) continue;
                var geometry = new StreamGeometry();
                using (var draw = geometry.Open())
                {
                    draw.BeginFigure(Project(line.Points[0]), false);
                    for (var i = 1; i < line.Points.Count; i++) draw.LineTo(Project(line.Points[i]));
                    draw.EndFigure(false);
                }
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 1, 32);
        var anchor = e.GetPosition(this) - new Rect(Bounds.Size).Center;
        _pan = anchor - (anchor - _pan) * (zoom / _zoom);
        _zoom = zoom; e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetView(); return; }
        _drag = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } previous) return;
        var current = e.GetPosition(this); _pan += current - previous; _drag = current; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = null; e.Pointer.Capture(null); base.OnPointerReleased(e); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag = null; base.OnPointerCaptureLost(e); }
}
