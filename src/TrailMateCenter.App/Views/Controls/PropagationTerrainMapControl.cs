using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mapsui;
using Mapsui.UI.Avalonia;
using TrailMateCenter.Localization;
using TrailMateCenter.Services;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views.Controls;

public sealed class PropagationTerrainMapControl : Control
{
    private const double MinimumViewportResolution = 1e-6;

    private MapControl? _mapControl;
    private Map? _trackedMap;

    public static readonly StyledProperty<PropagationTerrainMapSceneViewModel?> SceneProperty =
        AvaloniaProperty.Register<PropagationTerrainMapControl, PropagationTerrainMapSceneViewModel?>(nameof(Scene));
    public static readonly StyledProperty<double> OverlayOpacityProperty =
        AvaloniaProperty.Register<PropagationTerrainMapControl, double>(nameof(OverlayOpacity), 0.5);
    public static readonly StyledProperty<string?> ActiveLayerKeyProperty =
        AvaloniaProperty.Register<PropagationTerrainMapControl, string?>(nameof(ActiveLayerKey));

    static PropagationTerrainMapControl()
    {
        AffectsRender<PropagationTerrainMapControl>(SceneProperty, OverlayOpacityProperty, ActiveLayerKeyProperty);
    }

    public PropagationTerrainMapSceneViewModel? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public double OverlayOpacity
    {
        get => GetValue(OverlayOpacityProperty);
        set => SetValue(OverlayOpacityProperty, value);
    }

    public string? ActiveLayerKey
    {
        get => GetValue(ActiveLayerKeyProperty);
        set => SetValue(ActiveLayerKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty)
            InvalidateVisual();
    }

    public void AttachMapControl(MapControl? mapControl)
    {
        if (ReferenceEquals(_mapControl, mapControl))
            return;

        if (_mapControl is not null)
            _mapControl.PropertyChanged -= OnMapControlPropertyChanged;

        DetachTrackedMap();
        _mapControl = mapControl;

        if (_mapControl is not null)
        {
            _mapControl.PropertyChanged += OnMapControlPropertyChanged;
            AttachTrackedMap(_mapControl.Map);
        }

        InvalidateVisual();
    }

    public bool TryScreenToScene(Point screenPoint, out double x, out double z)
    {
        x = 0d;
        z = 0d;

        var scene = Scene;
        var bounds = Bounds;
        if (scene is null || bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        if (ShouldUseViewportProjection(scene))
            return TryScreenToSceneViewport(bounds.Size, screenPoint, out x, out z);

        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        x = minX + (Math.Clamp(screenPoint.X / bounds.Width, 0d, 1d) * (maxX - minX));
        z = minZ + (Math.Clamp(screenPoint.Y / bounds.Height, 0d, 1d) * (maxZ - minZ));
        return true;
    }

    public string? TryResolveHitSiteId(Point position, double hitRadiusPx = 16d)
    {
        var scene = Scene;
        if (scene is null || scene.Sites.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return null;
        if (ShouldUseViewportProjection(scene) && !TryGetViewportState(Bounds.Size, out _))
            return null;

        var bestDistanceSq = double.MaxValue;
        string? bestId = null;

        foreach (var site in scene.Sites)
        {
            var sitePoint = ProjectScene(new Rect(0, 0, Bounds.Width, Bounds.Height), scene, site.X, site.Z);
            var dx = position.X - sitePoint.X;
            var dy = position.Y - sitePoint.Y;
            var distanceSq = dx * dx + dy * dy;
            if (distanceSq <= hitRadiusPx * hitRadiusPx && distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                bestId = site.Id;
            }
        }

        return bestId;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scene = Scene;
        var bounds = Bounds;
        var rect = new Rect(0, 0, bounds.Width, bounds.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using var clipScope = context.PushClip(rect);

        var overlayOpacity = Math.Clamp(OverlayOpacity, 0, 1);
        var showLandcoverLayer = string.Equals(ActiveLayerKey, "landcover", StringComparison.Ordinal);
        var projectionAvailable = scene is not null && (!ShouldUseViewportProjection(scene) || TryGetViewportState(rect.Size, out _));
        var suppressAnalyticalOverlay = scene is not null && scene.UsesLocalCoordinates;

        if (scene is null || !scene.HasTerrain)
        {
            if (scene is not null && !projectionAvailable)
                return;

            DrawEmptyState(context, rect);
            if (scene is not null)
            {
                DrawActiveLayerCoverage(context, rect, scene);
                DrawPoints(context, rect, scene, scene.Sites, "#7BEA49", "#EAF6D7", 5, true);
                DrawPendingSite(context, rect, scene, scene.PendingSite);
                DrawSelectedSiteHighlight(context, rect, scene);
            }
            return;
        }

        if (!projectionAvailable)
            return;

        if (suppressAnalyticalOverlay)
        {
            DrawPoints(context, rect, scene, scene.Sites, "#7BEA49", "#EAF6D7", 5, true);
            DrawPendingSite(context, rect, scene, scene.PendingSite);
            DrawSelectedSiteHighlight(context, rect, scene);
            return;
        }

        DrawTerrain(context, rect, scene, showLandcoverLayer ? overlayOpacity * 0.32d : overlayOpacity);
        if (showLandcoverLayer)
            DrawLandcover(context, rect, scene, Math.Clamp((overlayOpacity * 0.92d) + 0.08d, 0d, 1d));
        else
            DrawActiveLayerCoverage(context, rect, scene);
        if (!showLandcoverLayer)
            DrawSelectedSiteCoverage(context, rect, scene);
        DrawPolylines(context, rect, scene, scene.ContourLines, new Pen(new SolidColorBrush(Color.Parse("#C4D0E0"), 0.55), 1));
        DrawPolylines(context, rect, scene, scene.RidgeLines, new Pen(new SolidColorBrush(Color.Parse("#6BD6FF"), 0.95), 2));
        DrawPolylines(context, rect, scene, scene.ProfileLines, new Pen(new SolidColorBrush(Color.Parse("#4AA3FF")), 2, dashStyle: new DashStyle([6, 4], 0)));
        DrawPoints(context, rect, scene, scene.Sites, "#7BEA49", "#EAF6D7", 5, true);
        DrawPendingSite(context, rect, scene, scene.PendingSite);
        DrawPoints(context, rect, scene, scene.RelayCandidates, "#F0D14A", "#FFF5C9", 4, false);
        DrawPoints(context, rect, scene, scene.RelayRecommendations, "#4BD08F", "#DFFFEF", 6, true);
        DrawPoints(context, rect, scene, scene.ProfileObstacles, "#EA4F4F", "#FFE0E0", 5, true);
        DrawSelectedSiteHighlight(context, rect, scene);
    }

    private static void DrawEmptyState(DrawingContext context, Rect rect)
    {
        var loc = LocalizationService.Instance;
        var typeface = new Typeface("Segoe UI");
        var title = new FormattedText(
            loc.GetString("Ui.Propagation.Workbench.Empty.Title"),
            loc.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            18,
            Brushes.White);
        var hint = new FormattedText(
            loc.GetString("Ui.Propagation.Workbench.Empty.Hint"),
            loc.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            new SolidColorBrush(Color.Parse("#9AA9B8")));

        var cardWidth = Math.Min(rect.Width * 0.42, 360);
        var cardHeight = 72d;
        var cardX = 14d;
        var cardY = Math.Max(62d, rect.Height - cardHeight - 16d);
        var card = new Rect(cardX, cardY, cardWidth, cardHeight);

        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(112, 17, 27, 39)), new Pen(new SolidColorBrush(Color.Parse("#5E7A9A"), 0.75), 1), card, 8, 8);
        context.DrawText(title, new Point(card.X + 10, card.Y + 10));
        context.DrawText(hint, new Point(card.X + 10, card.Y + 36));
    }

    private void DrawTerrain(DrawingContext context, Rect rect, PropagationTerrainMapSceneViewModel scene, double overlayOpacity)
    {
        var columns = scene.Columns;
        var rows = scene.Rows;
        if (columns <= 0 || rows <= 0)
            return;

        ResolveSceneBounds(scene, out var minX, out var minZ, out _, out _);
        var cellWidthM = scene.WidthM / Math.Max(1d, columns);
        var cellHeightM = scene.HeightM / Math.Max(1d, rows);
        var minElevation = scene.MinElevationM;
        var maxElevation = Math.Max(scene.MinElevationM + 1, scene.MaxElevationM);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var elevation = scene.ElevationSamples[(row * columns) + col];
                var normalized = Math.Clamp((elevation - minElevation) / (maxElevation - minElevation), 0, 1);
                var terrainColor = ResolveTerrainColor(normalized);
                var alpha = (byte)Math.Round(overlayOpacity * 160);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, terrainColor.R, terrainColor.G, terrainColor.B));
                var cellRect = ProjectSceneRect(rect, scene, minX + (col * cellWidthM), minZ + (row * cellHeightM), cellWidthM, cellHeightM);
                if (!IsVisibleRect(rect, cellRect))
                    continue;
                context.DrawRectangle(brush, null, cellRect);
            }
        }
    }

    private void DrawLandcover(DrawingContext context, Rect rect, PropagationTerrainMapSceneViewModel scene, double overlayOpacity)
    {
        var columns = scene.Columns;
        var rows = scene.Rows;
        if (!scene.HasLandcover || columns <= 0 || rows <= 0)
            return;

        ResolveSceneBounds(scene, out var minX, out var minZ, out _, out _);
        var cellWidthM = scene.WidthM / Math.Max(1d, columns);
        var cellHeightM = scene.HeightM / Math.Max(1d, rows);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var landcoverClass = scene.LandcoverSamples[(row * columns) + col];
                var landcoverColor = ResolveLandcoverColor(landcoverClass);
                var alpha = (byte)Math.Round(overlayOpacity * 176);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, landcoverColor.R, landcoverColor.G, landcoverColor.B));
                var cellRect = ProjectSceneRect(rect, scene, minX + (col * cellWidthM), minZ + (row * cellHeightM), cellWidthM, cellHeightM);
                if (!IsVisibleRect(rect, cellRect))
                    continue;
                context.DrawRectangle(brush, null, cellRect);
            }
        }
    }

    private void DrawPolylines(
        DrawingContext context,
        Rect rect,
        PropagationTerrainMapSceneViewModel scene,
        IReadOnlyList<PropagationScenePolyline> polylines,
        Pen pen)
    {
        foreach (var line in polylines)
        {
            if (line.Points.Count < 2)
                continue;

            var geometry = new StreamGeometry();
            using var stream = geometry.Open();
            stream.BeginFigure(ProjectScene(rect, scene, line.Points[0].X, line.Points[0].Z), false);
            for (var i = 1; i < line.Points.Count; i++)
            {
                stream.LineTo(ProjectScene(rect, scene, line.Points[i].X, line.Points[i].Z));
            }
            stream.EndFigure(false);
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawPoints(
        DrawingContext context,
        Rect rect,
        PropagationTerrainMapSceneViewModel scene,
        IReadOnlyList<PropagationScenePoint> points,
        string fillHex,
        string labelHex,
        double radius,
        bool drawLabel)
    {
        var typeface = new Typeface("Segoe UI");

        foreach (var point in points)
        {
            var resolvedFill = ResolvePointColor(point.ColorHex, fillHex);
            var fill = new SolidColorBrush(resolvedFill);
            var stroke = new Pen(new SolidColorBrush(Color.Parse("#102033")), 1);
            var projected = ProjectScene(rect, scene, point.X, point.Z);
            context.DrawEllipse(fill, stroke, projected, radius, radius);

            if (!drawLabel || string.IsNullOrWhiteSpace(point.Label))
                continue;

            var text = new FormattedText(
                point.Label,
                LocalizationService.Instance.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                new SolidColorBrush(ResolvePointColor(point.ColorHex, labelHex)));
            context.DrawText(text, new Point(projected.X + radius + 4, projected.Y - 8));
        }
    }

    private void DrawPendingSite(
        DrawingContext context,
        Rect rect,
        PropagationTerrainMapSceneViewModel scene,
        PropagationScenePoint? point)
    {
        if (point is null)
            return;

        var projected = ProjectScene(rect, scene, point.X, point.Z);
        var markerColor = ResolvePointColor(point.ColorHex, "#FF8C42");
        var outerFill = new SolidColorBrush(markerColor);
        var innerFill = new SolidColorBrush(Color.Parse("#FFF3DA"));
        var stroke = new Pen(new SolidColorBrush(Color.Parse("#5C2B14")), 1.5);
        var ring = new Pen(new SolidColorBrush(markerColor, 0.8), 2);

        context.DrawEllipse(null, ring, projected, 14, 14);
        context.DrawEllipse(outerFill, stroke, projected, 7, 7);
        context.DrawEllipse(innerFill, null, projected, 3, 3);

        if (!string.IsNullOrWhiteSpace(point.Label))
        {
            var text = new FormattedText(
                point.Label,
                LocalizationService.Instance.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                new SolidColorBrush(markerColor));
            context.DrawText(text, new Point(projected.X + 11, projected.Y - 18));
        }
    }

    private void DrawSelectedSiteCoverage(DrawingContext context, Rect rect, PropagationTerrainMapSceneViewModel scene)
    {
        if (scene.SelectedSiteCoverageCells.Count == 0)
            return;

        DrawCoverageHeatmap(context, rect, scene.SelectedSiteCoverageCells, opacityMultiplier: 1d);
    }

    private void DrawActiveLayerCoverage(DrawingContext context, Rect rect, PropagationTerrainMapSceneViewModel scene)
    {
        if (scene.ActiveLayerCoverageCells.Count == 0)
            return;

        DrawCoverageHeatmap(context, rect, scene.ActiveLayerCoverageCells, opacityMultiplier: 0.74d);
    }

    private void DrawCoverageHeatmap(
        DrawingContext context,
        Rect rect,
        IReadOnlyList<PropagationCoverageCellViewModel> cells,
        double opacityMultiplier)
    {
        foreach (var cell in cells)
        {
            var color = ResolveCoverageColor(cell.MarginDb);
            var alpha = (byte)Math.Round(ResolveCoverageAlpha(cell.MarginDb) * Math.Clamp(opacityMultiplier, 0d, 1d));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var cellRect = ProjectSceneRect(
                rect,
                Scene ?? PropagationTerrainMapSceneViewModel.Empty,
                cell.X - (cell.WidthM / 2d),
                cell.Z - (cell.HeightM / 2d),
                cell.WidthM,
                cell.HeightM);
            if (!IsVisibleRect(rect, cellRect))
                continue;
            context.DrawRectangle(brush, null, cellRect);
        }
    }

    private void DrawSelectedSiteHighlight(DrawingContext context, Rect rect, PropagationTerrainMapSceneViewModel scene)
    {
        if (string.IsNullOrWhiteSpace(scene.SelectedSiteId))
            return;

        var site = scene.Sites.FirstOrDefault(point => string.Equals(point.Id, scene.SelectedSiteId, StringComparison.Ordinal));
        if (site is null)
            return;

        var center = ProjectScene(rect, scene, site.X, site.Z);
        var highlight = ResolvePointColor(site.ColorHex, "#4AA3FF");
        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(240, highlight.R, highlight.G, highlight.B)), 2.5),
            center,
            11,
            11);
    }

    private static Color ResolvePointColor(string? preferredHex, string fallbackHex)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(preferredHex) ? fallbackHex : preferredHex);
        }
        catch
        {
            return Color.Parse(fallbackHex);
        }
    }

    private static Color ResolveLandcoverColor(PropagationLandcoverClass landcoverClass)
    {
        return Color.Parse(PropagationLandcoverPresentation.ResolveAccentColorHex(landcoverClass));
    }

    private static bool IsVisibleRect(Rect viewportRect, Rect candidateRect)
    {
        if (!double.IsFinite(candidateRect.X) ||
            !double.IsFinite(candidateRect.Y) ||
            !double.IsFinite(candidateRect.Width) ||
            !double.IsFinite(candidateRect.Height))
        {
            return false;
        }

        if (candidateRect.Width <= 0 || candidateRect.Height <= 0)
            return false;

        return candidateRect.Intersects(viewportRect);
    }

    private Point ProjectScene(Rect rect, PropagationTerrainMapSceneViewModel scene, double x, double z)
    {
        if (ShouldUseViewportProjection(scene))
        {
            if (TryProjectSceneViewport(rect.Size, x, z, out var projected))
                return new Point(rect.X + projected.X, rect.Y + projected.Y);

            return new Point(double.NaN, double.NaN);
        }

        return ProjectNormalized(rect, scene, x, z);
    }

    private Rect ProjectSceneRect(Rect rect, PropagationTerrainMapSceneViewModel scene, double x, double z, double widthM, double heightM)
    {
        if (ShouldUseViewportProjection(scene))
        {
            if (TryProjectSceneViewport(rect.Size, x, z, out var topLeft) &&
                TryProjectSceneViewport(rect.Size, x + widthM, z + heightM, out var bottomRight))
            {
                var left = rect.X + Math.Min(topLeft.X, bottomRight.X);
                var top = rect.Y + Math.Min(topLeft.Y, bottomRight.Y);
                var width = Math.Abs(bottomRight.X - topLeft.X);
                var height = Math.Abs(bottomRight.Y - topLeft.Y);
                return new Rect(left, top, Math.Max(1d, width + 1d), Math.Max(1d, height + 1d));
            }

            return new Rect(0, 0, 0, 0);
        }

        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        var fallbackLeft = rect.X + (((x - minX) / Math.Max(1d, maxX - minX)) * rect.Width);
        var fallbackTop = rect.Y + (((z - minZ) / Math.Max(1d, maxZ - minZ)) * rect.Height);
        var fallbackWidth = Math.Max(1d, (widthM / Math.Max(1d, maxX - minX)) * rect.Width);
        var fallbackHeight = Math.Max(1d, (heightM / Math.Max(1d, maxZ - minZ)) * rect.Height);
        return new Rect(fallbackLeft, fallbackTop, fallbackWidth + 1d, fallbackHeight + 1d);
    }

    private bool TryProjectSceneViewport(Size size, double x, double z, out Point point)
    {
        point = default;
        if (!TryGetViewportState(size, out var viewportState))
            return false;

        var screenX = (viewportState.WidthPx * 0.5d) + ((x - viewportState.CenterX) / viewportState.Resolution);
        var screenY = (viewportState.HeightPx * 0.5d) - ((z - viewportState.CenterY) / viewportState.Resolution);
        point = new Point(screenX, screenY);
        return true;
    }

    private bool TryScreenToSceneViewport(Size size, Point screenPoint, out double x, out double z)
    {
        x = 0d;
        z = 0d;
        if (!TryGetViewportState(size, out var viewportState))
            return false;

        x = viewportState.CenterX + ((screenPoint.X - (viewportState.WidthPx * 0.5d)) * viewportState.Resolution);
        z = viewportState.CenterY - ((screenPoint.Y - (viewportState.HeightPx * 0.5d)) * viewportState.Resolution);
        return true;
    }

    private bool TryGetViewportState(Size size, out ViewportState viewportState)
    {
        viewportState = default;
        EnsureTrackedMapCurrent();
        if (_trackedMap?.Navigator?.Viewport is not { } viewport)
            return false;
        if (size.Width <= 0 || size.Height <= 0)
            return false;

        var resolution = Math.Max(MinimumViewportResolution, viewport.Resolution);
        viewportState = new ViewportState(size.Width, size.Height, viewport.CenterX, viewport.CenterY, resolution);
        return true;
    }

    private void EnsureTrackedMapCurrent()
    {
        var currentMap = _mapControl?.Map;
        if (!ReferenceEquals(_trackedMap, currentMap))
            AttachTrackedMap(currentMap);
    }

    private void OnMapControlPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, "Map", StringComparison.Ordinal))
            return;

        AttachTrackedMap(_mapControl?.Map);
        InvalidateVisual();
    }

    private void OnTrackedMapViewportChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void AttachTrackedMap(Map? map)
    {
        if (ReferenceEquals(_trackedMap, map))
            return;

        DetachTrackedMap();
        _trackedMap = map;
        if (_trackedMap is not null)
            _trackedMap.Navigator.ViewportChanged += OnTrackedMapViewportChanged;
    }

    private void DetachTrackedMap()
    {
        if (_trackedMap is null)
            return;

        _trackedMap.Navigator.ViewportChanged -= OnTrackedMapViewportChanged;
        _trackedMap = null;
    }

    private static bool ShouldUseViewportProjection(PropagationTerrainMapSceneViewModel scene)
    {
        return scene.UseViewportProjection && string.Equals(scene.Crs, "EPSG:3857", StringComparison.OrdinalIgnoreCase);
    }

    private static Point ProjectNormalized(Rect rect, PropagationTerrainMapSceneViewModel scene, double x, double z)
    {
        ResolveSceneBounds(scene, out var minX, out var minZ, out var maxX, out var maxZ);
        var px = rect.X + (((x - minX) / Math.Max(1d, maxX - minX)) * rect.Width);
        var py = rect.Y + (((z - minZ) / Math.Max(1d, maxZ - minZ)) * rect.Height);
        return new Point(px, py);
    }

    private static void ResolveSceneBounds(
        PropagationTerrainMapSceneViewModel scene,
        out double minX,
        out double minZ,
        out double maxX,
        out double maxZ)
    {
        if (scene.MaxX > scene.MinX && scene.MaxZ > scene.MinZ)
        {
            minX = scene.MinX;
            minZ = scene.MinZ;
            maxX = scene.MaxX;
            maxZ = scene.MaxZ;
            return;
        }

        minX = 0d;
        minZ = 0d;
        maxX = scene.WidthM;
        maxZ = scene.HeightM;
    }

    private static Color ResolveTerrainColor(double normalized)
    {
        if (normalized < 0.18)
            return Color.Parse("#213744");
        if (normalized < 0.34)
            return Color.Parse("#355F49");
        if (normalized < 0.5)
            return Color.Parse("#5D7D50");
        if (normalized < 0.68)
            return Color.Parse("#8C8453");
        if (normalized < 0.84)
            return Color.Parse("#AC9463");
        return Color.Parse("#C7BBB0");
    }

    private static Color ResolveCoverageColor(double marginDb)
    {
        var stops = new[]
        {
            new CoverageStop(20d, Color.Parse("#7BEA49")),
            new CoverageStop(10d, Color.Parse("#F0D14A")),
            new CoverageStop(3d, Color.Parse("#F29A34")),
            new CoverageStop(-4d, Color.Parse("#EA4F4F")),
            new CoverageStop(-10d, Color.Parse("#B5BDC8")),
        };

        if (marginDb >= stops[0].MarginDb)
            return stops[0].Color;
        if (marginDb <= stops[^1].MarginDb)
            return stops[^1].Color;

        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (marginDb > stops[index + 1].MarginDb)
            {
                var upper = stops[index];
                var lower = stops[index + 1];
                var t = (marginDb - lower.MarginDb) / (upper.MarginDb - lower.MarginDb);
                return LerpColor(lower.Color, upper.Color, t);
            }
        }

        return stops[^1].Color;
    }

    private static byte ResolveCoverageAlpha(double marginDb)
    {
        var normalized = Math.Clamp((marginDb + 10d) / 30d, 0d, 1d);
        return (byte)Math.Round(28d + (normalized * 104d));
    }

    private static Color LerpColor(Color from, Color to, double t)
    {
        var clamped = Math.Clamp(t, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((to.A - from.A) * clamped)),
            (byte)Math.Round(from.R + ((to.R - from.R) * clamped)),
            (byte)Math.Round(from.G + ((to.G - from.G) * clamped)),
            (byte)Math.Round(from.B + ((to.B - from.B) * clamped)));
    }

    private readonly record struct CoverageStop(double MarginDb, Color Color);
    private readonly record struct ViewportState(double WidthPx, double HeightPx, double CenterX, double CenterY, double Resolution);
}
