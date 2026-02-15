using System;
using Mapsui;
using Avalonia.Controls;
using Avalonia.Input;
using Mapsui.Manipulations;
using Mapsui.UI.Avalonia;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class DashboardView : UserControl
{
    private MapControl? _mapControl;

    public DashboardView()
    {
        InitializeComponent();
        _mapControl = this.FindControl<MapControl>("MainMapControl");
        if (_mapControl is null)
            return;

        Gestures.AddPointerTouchPadGestureMagnifyHandler(_mapControl, OnMapTouchPadMagnify);
        _mapControl.PointerPressed += OnMapPointerPressed;
        _mapControl.PointerReleased += OnMapPointerReleased;
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
        DisableFollowLatestForManualNavigation();
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
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (e.GetMapInfo is null)
            return;

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
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var position = e.GetPosition(_mapControl);
        var nodeId = vm.Map.ResolveNodeIdAtScreenPosition(
            new ScreenPosition(position.X, position.Y),
            _mapControl.GetMapInfo);
        if (!nodeId.HasValue)
            return;

        vm.SelectSubjectByNodeId(nodeId.Value, switchToChatTab: true);
        e.Handled = true;
    }
}
