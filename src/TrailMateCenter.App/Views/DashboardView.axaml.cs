using System;
using Avalonia;
using Mapsui;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mapsui.Extensions;
using Mapsui.Manipulations;
using Mapsui.Projections;
using Mapsui.UI.Avalonia;
using TrailMateCenter.Localization;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class DashboardView : UserControl
{
    private const double OfflineSelectionEdgePanThreshold = 28;
    private const double OfflineSelectionEdgePanMaxStepPx = 24;

    private MapControl? _mapControl;
    private Polyline? _offlineSelectionPolygon;
    private ToggleButton? _offlineSelectionModeToggle;
    private bool _isOfflineSelectionDragging;
    private readonly List<Point> _offlineSelectionScreenPath = new();
    private Point _offlineSelectionLastPoint;
    private bool _suppressNextMapTap;
    private bool _offlineCacheDialogOpen;
    private bool _offlineCacheRegionsDialogOpen;
    private ContextMenu? _offlineRouteContextMenu;
    private MenuItem? _offlineRouteCacheMenuItem;

    public DashboardView()
    {
        InitializeComponent();
        _mapControl = this.FindControl<MapControl>("MainMapControl");
        if (_mapControl is null)
            return;

        _offlineSelectionPolygon = this.FindControl<Polyline>("OfflineSelectionPolygon");
        _offlineSelectionModeToggle = this.FindControl<ToggleButton>("OfflineCacheSelectToggle");
        if (_offlineSelectionModeToggle is not null)
        {
            _offlineSelectionModeToggle.IsCheckedChanged += OnOfflineSelectionModeToggled;
        }

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;

        Gestures.AddPointerTouchPadGestureMagnifyHandler(_mapControl, OnMapTouchPadMagnify);
        _mapControl.PointerPressed += OnMapPointerPressed;
        _mapControl.PointerMoved += OnMapPointerMoved;
        _mapControl.PointerReleased += OnMapPointerReleased;
        _mapControl.PointerCaptureLost += OnMapPointerCaptureLost;
        _mapControl.PointerWheelChanged += OnMapPointerWheelChanged;
        _mapControl.MapTapped += OnMapTapped;
    }

    private void OnMapTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_mapControl?.Map is null)
            return;

        DisableFollowLatestForManualNavigation();

        var navigator = _mapControl.Map.Navigator;
        if (navigator.Viewport.Resolution <= 0)
            return;

        // macOS touchpad magnify delta is incremental; convert it to a stable scale factor.
        var delta = Math.Abs(e.Delta.Y) > 0 ? e.Delta.Y : e.Delta.X;
        if (Math.Abs(delta) < 1e-6)
            return;

        // Invert sign so pinch-in zooms out and spread zooms in (mac trackpad expectation).
        var scaleFactor = Math.Clamp(Math.Exp(-delta), 0.2, 5.0);
        var center = e.GetPosition(_mapControl);
        navigator.MouseWheelZoomContinuous(scaleFactor, new ScreenPosition(center.X, center.Y));
        e.Handled = true;
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mapControl is null)
            return;

        if (IsOfflineSelectionModeActive() &&
            e.GetCurrentPoint(_mapControl).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            DisableFollowLatestForManualNavigation();
            SyncOfflineSelectionPanLock();
            _isOfflineSelectionDragging = true;
            _offlineSelectionLastPoint = ClampPointToMapBounds(e.GetPosition(_mapControl));
            _offlineSelectionScreenPath.Clear();
            AppendOfflineSelectionPoint(_offlineSelectionLastPoint, force: true);
            _suppressNextMapTap = true;
            e.Pointer.Capture(_mapControl);
            UpdateOfflineSelectionVisual();
            e.Handled = true;
            return;
        }

        DisableFollowLatestForManualNavigation();
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mapControl is null || !_isOfflineSelectionDragging)
            return;

        var pointer = e.GetPosition(_mapControl);
        TryAutoPanAtMapEdge(pointer);

        var current = ClampPointToMapBounds(pointer);
        _offlineSelectionLastPoint = current;
        AppendOfflineSelectionPoint(current, force: false);
        UpdateOfflineSelectionVisual();
        e.Handled = true;
    }

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        DisableFollowLatestForManualNavigation();
    }

    private void DisableFollowLatestForManualNavigation()
    {
        if (DataContext is MainWindowViewModel vm && vm.MapFollowLatest)
        {
            vm.MapFollowLatest = false;
        }
    }

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_suppressNextMapTap)
        {
            _suppressNextMapTap = false;
            e.Handled = true;
            return;
        }

        if (DataContext is not MainWindowViewModel vm)
            return;
        if (vm.Map.IsOfflineCacheSelectionMode)
        {
            e.Handled = true;
            return;
        }
        if (e.GetMapInfo is null)
            return;

        if (vm.Map.SelectOfflineRouteFromMapInfo(e.GetMapInfo))
        {
            e.Handled = true;
            return;
        }

        var nodeId = vm.Map.ResolveNodeIdFromMapInfo(e.GetMapInfo);
        if (!nodeId.HasValue)
            return;

        vm.SelectSubjectByNodeId(nodeId.Value, switchToChatTab: false);
        e.Handled = true;
    }

    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mapControl is null)
            return;
        if (_isOfflineSelectionDragging)
        {
            _offlineSelectionLastPoint = ClampPointToMapBounds(e.GetPosition(_mapControl));
            AppendOfflineSelectionPoint(_offlineSelectionLastPoint, force: true);
            e.Pointer.Capture(null);
            CompleteOfflineSelection();
            e.Handled = true;
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var position = e.GetPosition(_mapControl);
        if (vm.Map.SelectOfflineRouteAtScreenPosition(
                new ScreenPosition(position.X, position.Y),
                _mapControl.GetMapInfo))
        {
            ShowOfflineRouteContextMenu();
            e.Handled = true;
            return;
        }

        var nodeId = vm.Map.ResolveNodeIdAtScreenPosition(
            new ScreenPosition(position.X, position.Y),
            _mapControl.GetMapInfo);
        if (!nodeId.HasValue)
            return;

        vm.SelectSubjectByNodeId(nodeId.Value, switchToChatTab: true);
        e.Handled = true;
    }

    private void EnsureOfflineRouteContextMenu()
    {
        if (_offlineRouteContextMenu is not null)
            return;

        _offlineRouteCacheMenuItem = new MenuItem();
        _offlineRouteCacheMenuItem.Click += OnOfflineRouteCacheMenuItemClicked;
        _offlineRouteContextMenu = new ContextMenu
        {
            ItemsSource = new[] { _offlineRouteCacheMenuItem },
            Placement = PlacementMode.Pointer,
        };
    }

    private void ShowOfflineRouteContextMenu()
    {
        if (_mapControl is null)
            return;

        EnsureOfflineRouteContextMenu();
        if (_offlineRouteContextMenu is null || _offlineRouteCacheMenuItem is null)
            return;

        var loc = LocalizationService.Instance;
        _offlineRouteCacheMenuItem.Header = loc.GetString("Ui.Dashboard.OfflineRoute.Cache");
        _offlineRouteContextMenu.PlacementTarget = _mapControl;
        _offlineRouteContextMenu.Open();
    }

    private async void OnOfflineRouteCacheMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.Map.BuildOfflineCacheSelectionFromSelectedRoute(8))
            return;

        await ShowOfflineCacheDialogAsync(vm);
    }

    private void OnMapPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isOfflineSelectionDragging)
            return;

        CompleteOfflineSelection();
    }

    private async Task ApplyOfflineSelectionAsync()
    {
        if (_mapControl?.Map is null)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;
        var viewport = _mapControl.Map.Navigator.Viewport;
        var worldPoints = _offlineSelectionScreenPath
            .Select(p => viewport.ScreenToWorld(new ScreenPosition(p.X, p.Y)))
            .Select(p => new MPoint(p.X, p.Y))
            .ToList();
        HideOfflineSelectionVisual();
        _offlineSelectionScreenPath.Clear();

        if (worldPoints.Count < 3)
            return;

        if (!vm.Map.SetOfflineCacheSelectionPolygonFromWorldPoints(worldPoints))
            return;

        vm.Map.IsOfflineCacheSelectionMode = false;
        SyncOfflineSelectionPanLock();

        await ShowOfflineCacheDialogAsync(vm);
    }

    private void CompleteOfflineSelection()
    {
        if (!_isOfflineSelectionDragging)
            return;

        _isOfflineSelectionDragging = false;
        _ = ApplyOfflineSelectionAsync();
        SyncOfflineSelectionPanLock();
    }

    private void UpdateOfflineSelectionVisual()
    {
        if (_offlineSelectionPolygon is null || _offlineSelectionScreenPath.Count < 2)
            return;

        var points = new List<Point>(_offlineSelectionScreenPath.Count + 1);
        points.AddRange(_offlineSelectionScreenPath);
        points.Add(_offlineSelectionScreenPath[0]);
        _offlineSelectionPolygon.Points = points;
        _offlineSelectionPolygon.IsVisible = true;
    }

    private void HideOfflineSelectionVisual()
    {
        if (_offlineSelectionPolygon is not null)
            _offlineSelectionPolygon.IsVisible = false;
    }

    private void OnOfflineSelectionModeToggled(object? sender, RoutedEventArgs e)
    {
        if (!IsOfflineSelectionModeActive())
        {
            _isOfflineSelectionDragging = false;
            _offlineSelectionScreenPath.Clear();
            HideOfflineSelectionVisual();
        }

        SyncOfflineSelectionPanLock();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SyncOfflineSelectionPanLock();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        SyncOfflineSelectionPanLock();
    }

    private bool IsOfflineSelectionModeActive()
    {
        if (_offlineSelectionModeToggle?.IsChecked == true)
            return true;

        return DataContext is MainWindowViewModel vm && vm.Map.IsOfflineCacheSelectionMode;
    }

    private void SyncOfflineSelectionPanLock()
    {
        if (_mapControl?.Map is null)
            return;

        _mapControl.Map.Navigator.PanLock = IsOfflineSelectionModeActive();
    }

    private Point ClampPointToMapBounds(Point point)
    {
        if (_mapControl is null)
            return point;

        var width = Math.Max(1, _mapControl.Bounds.Width);
        var height = Math.Max(1, _mapControl.Bounds.Height);
        return new Point(
            Math.Clamp(point.X, 0, width),
            Math.Clamp(point.Y, 0, height));
    }

    private void AppendOfflineSelectionPoint(Point point, bool force)
    {
        if (_offlineSelectionScreenPath.Count == 0)
        {
            _offlineSelectionScreenPath.Add(point);
            return;
        }

        var last = _offlineSelectionScreenPath[_offlineSelectionScreenPath.Count - 1];
        var dx = point.X - last.X;
        var dy = point.Y - last.Y;
        if (!force && (dx * dx + dy * dy) < 16)
            return;

        _offlineSelectionScreenPath.Add(point);
    }

    private void TryAutoPanAtMapEdge(Point pointerPosition)
    {
        if (_mapControl?.Map is null)
            return;

        var bounds = _mapControl.Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        var panX = ComputeEdgePanDelta(pointerPosition.X, bounds.Width);
        var panY = ComputeEdgePanDelta(pointerPosition.Y, bounds.Height);
        if (Math.Abs(panX) < 0.01 && Math.Abs(panY) < 0.01)
            return;

        var viewport = _mapControl.Map.Navigator.Viewport;
        var centerScreen = new ScreenPosition((bounds.Width * 0.5) + panX, (bounds.Height * 0.5) + panY);
        var newCenter = viewport.ScreenToWorld(centerScreen);
        _mapControl.Map.Navigator.CenterOn(newCenter, 0, null);
    }

    private static double ComputeEdgePanDelta(double pointerAxis, double extent)
    {
        if (pointerAxis < OfflineSelectionEdgePanThreshold)
        {
            var intensity = Math.Clamp((OfflineSelectionEdgePanThreshold - pointerAxis) / OfflineSelectionEdgePanThreshold, 0.0, 1.0);
            return -OfflineSelectionEdgePanMaxStepPx * intensity;
        }

        var farEdgeStart = extent - OfflineSelectionEdgePanThreshold;
        if (pointerAxis > farEdgeStart)
        {
            var intensity = Math.Clamp((pointerAxis - farEdgeStart) / OfflineSelectionEdgePanThreshold, 0.0, 1.0);
            return OfflineSelectionEdgePanMaxStepPx * intensity;
        }

        return 0;
    }

    private async Task ShowOfflineCacheDialogAsync(MainWindowViewModel vm)
    {
        if (_offlineCacheDialogOpen)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        _offlineCacheDialogOpen = true;
        try
        {
            var defaultName = string.IsNullOrWhiteSpace(vm.OfflineCacheRegionName)
                ? $"Area {vm.OfflineCacheRegions.Count + 1}"
                : vm.OfflineCacheRegionName.Trim();

            var dialogVm = new OfflineCacheDialogViewModel
            {
                RegionName = defaultName,
                SaveRegion = true,
                IncludeOsm = true,
                IncludeTerrain = true,
                IncludeSatellite = true,
                IncludeContours = vm.ContoursEnabled,
                IncludeUltraFineContours = vm.ContoursEnabled && vm.ContoursUltraFine,
            };

            var dialog = new OfflineCacheDialog
            {
                DataContext = dialogVm,
            };

            var result = await dialog.ShowDialogWithResultAsync(owner);
            if (result is null)
                return;

            if (result.Action == OfflineCacheDialogAction.SaveOnly || result.SaveRegion)
            {
                vm.SaveOfflineCacheRegionFromCurrentSelection(result.RegionName, result.BuildOptions);
            }

            if (result.Action == OfflineCacheDialogAction.Build)
            {
                await vm.RunOfflineCacheForCurrentSelectionAsync(result.BuildOptions);
            }
        }
        finally
        {
            _offlineCacheDialogOpen = false;
        }
    }

    private async void OnOfflineCacheRegionsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        await ShowOfflineCacheRegionsDialogAsync(vm);
    }

    private async void OnImportTrackClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
            return;

        if (!topLevel.StorageProvider.CanOpen)
            return;

        var loc = LocalizationService.Instance;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = loc.GetString("Ui.Dashboard.ImportTrackPickFile"),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KML")
                    {
                        Patterns = new[] { "*.kml" },
                        MimeTypes = new[] { "application/vnd.google-earth.kml+xml", "application/xml", "text/xml" },
                    },
                },
            });
        }
        catch
        {
            return;
        }

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var result = vm.Map.ImportOfflineRouteFromKml(path);
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            vm.LastError = result.ErrorMessage;
        }
    }

    private async Task ShowOfflineCacheRegionsDialogAsync(MainWindowViewModel vm)
    {
        if (_offlineCacheRegionsDialogOpen)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        _offlineCacheRegionsDialogOpen = true;
        try
        {
            var dialog = new OfflineCacheRegionsDialog
            {
                DataContext = vm,
            };
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _offlineCacheRegionsDialogOpen = false;
        }
    }

    private async void OnRunOfflineCacheClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        await vm.Map.RunOfflineCacheForSelectionAsync();
    }

    private void OnCancelOfflineCacheClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.Map.CancelOfflineCache();
    }

    private void OnClearOfflineCacheSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.Map.ClearOfflineCacheSelection();
        HideOfflineSelectionVisual();
    }

    private void OnMapLogPanelPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Keep wheel interaction inside log panel to prevent map zoom/pan passthrough.
        e.Handled = true;
    }
}
