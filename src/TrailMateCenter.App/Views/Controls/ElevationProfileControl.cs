using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrailMateCenter.Maps.Tmap;
using System.Globalization;

namespace TrailMateCenter.Views.Controls;

public sealed class ElevationProfileControl : Control
{
    public static readonly StyledProperty<TmapElevationProfile?> ProfileProperty =
        AvaloniaProperty.Register<ElevationProfileControl, TmapElevationProfile?>(nameof(Profile));
    public TmapElevationProfile? Profile { get => GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    static ElevationProfileControl() => AffectsRender<ElevationProfileControl>(ProfileProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101C20")), null, rect);
        if (Profile is not { MinimumMeters: { } min, MaximumMeters: { } max } profile) return;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#6AD4C2")), 2);
        const double left = 58, top = 20, bottom = 28;
        var width = Math.Max(1, rect.Width - left - 18); var height = Math.Max(1, rect.Height - top - bottom);
        var dim = new SolidColorBrush(Color.Parse("#A9BAC5"));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#34444B")), 1);
        for (var tick = 0; tick < 3; tick++)
        {
            var y = top + height * tick / 2;
            context.DrawLine(grid, new(left, y), new(left + width, y));
            Label($"{max - (max - min) * tick / 2:F0} m", new(3, y - 8));
        }
        Label("0 m", new(left, top + height + 5));
        var end = $"{profile.DistanceMeters:F0} m";
        Label(end, new(Math.Max(left, left + width - 65), top + height + 5));
        void Label(string text, Point at) => context.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, dim), at);
        Point? previous = null;
        using var clip = context.PushClip(rect);
        foreach (var sample in profile.Samples)
        {
            if (sample.ElevationMeters is not { } z) { previous = null; continue; }
            var point = new Point(left + (profile.DistanceMeters > 0 ? sample.DistanceMeters / profile.DistanceMeters * width : 0),
                top + height - (max > min ? (z - min) / (max - min) * height : height / 2));
            if (previous is { } from) context.DrawLine(pen, from, point);
            previous = point;
        }
    }
}
