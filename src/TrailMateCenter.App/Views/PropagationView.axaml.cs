using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Mapsui.UI.Avalonia;
using TrailMateCenter.Localization;
using TrailMateCenter.ViewModels;
using TrailMateCenter.Views.Controls;

namespace TrailMateCenter.Views;

public partial class PropagationView : UserControl
{
    private MapControl? _mapControl;
    private PropagationTerrainMapControl? _terrainOverlay;
    private ContextMenu? _siteContextMenu;
    private MenuItem? _addSiteMenuItem;
    private MenuItem? _editSiteMenuItem;
    private MenuItem? _promoteToBaseMenuItem;
    private MenuItem? _setAsTargetMenuItem;
    private MenuItem? _duplicateSiteMenuItem;
    private MenuItem? _deleteSiteMenuItem;
    private string? _contextSiteId;
    private (double X, double Z)? _contextPlacement;
    private string? _dragSiteId;
    private Point? _dragStartPosition;
    private bool _isDraggingSite;

    public PropagationView()
    {
        InitializeComponent();
        _mapControl = this.FindControl<MapControl>("PropagationMapControl");
        _terrainOverlay = this.FindControl<PropagationTerrainMapControl>("PropagationTerrainOverlay");
        _terrainOverlay?.AttachMapControl(_mapControl);
        if (_mapControl is not null)
        {
            _mapControl.PointerPressed += OnMapPointerPressed;
            _mapControl.PointerMoved += OnMapPointerMoved;
            _mapControl.PointerReleased += OnMapPointerReleased;
            _mapControl.PointerCaptureLost += OnMapPointerCaptureLost;
        }
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mapControl is null || e.GetCurrentPoint(_mapControl).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var bounds = _mapControl.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var siteId = _terrainOverlay?.TryResolveHitSiteId(e.GetPosition(_mapControl));
        if (string.IsNullOrWhiteSpace(siteId))
        {
            vm.Propagation.SelectScenarioSite(null);
            return;
        }

        _dragSiteId = siteId;
        _dragStartPosition = e.GetPosition(_mapControl);
        _isDraggingSite = false;
        e.Pointer.Capture(_mapControl);
        e.Handled = true;
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mapControl is null || !_isDraggingSite || string.IsNullOrWhiteSpace(_dragSiteId))
        {
            if (_mapControl is null || string.IsNullOrWhiteSpace(_dragSiteId) || _dragStartPosition is null)
                return;
            if (DataContext is not MainWindowViewModel pendingVm)
                return;

            var current = e.GetPosition(_mapControl);
            var delta = current - _dragStartPosition.Value;
            if (Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y)) < 4)
                return;

            _isDraggingSite = true;
            pendingVm.Propagation.SelectScenarioSite(_dragSiteId);
        }
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!TryResolveMapPosition(vm, e.GetPosition(_mapControl), out var x, out var z))
            return;
        vm.Propagation.MoveScenarioSite(_dragSiteId, x, z);
        e.Handled = true;
    }

    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mapControl is null)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        var bounds = _mapControl.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var position = e.GetPosition(_mapControl);

        if (_isDraggingSite)
        {
            _isDraggingSite = false;
            _dragSiteId = null;
            _dragStartPosition = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            var siteId = _terrainOverlay?.TryResolveHitSiteId(position);
            vm.Propagation.SelectScenarioSite(siteId);
            _dragSiteId = null;
            _dragStartPosition = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        if (!TryResolveMapPosition(vm, position, out var x, out var z))
            return;

        var contextSiteId = _terrainOverlay?.TryResolveHitSiteId(position);
        ShowSiteContextMenu(vm, x, z, contextSiteId);
        e.Handled = true;
    }

    private void OnMapPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDraggingSite = false;
        _dragSiteId = null;
        _dragStartPosition = null;
    }

    private void EnsureSiteContextMenu()
    {
        if (_siteContextMenu is not null)
            return;

        _addSiteMenuItem = new MenuItem();
        _editSiteMenuItem = new MenuItem();
        _promoteToBaseMenuItem = new MenuItem();
        _setAsTargetMenuItem = new MenuItem();
        _duplicateSiteMenuItem = new MenuItem();
        _deleteSiteMenuItem = new MenuItem();

        _addSiteMenuItem.Click += OnAddSiteMenuItemClicked;
        _editSiteMenuItem.Click += OnEditSiteMenuItemClicked;
        _promoteToBaseMenuItem.Click += OnPromoteToBaseMenuItemClicked;
        _setAsTargetMenuItem.Click += OnSetAsTargetMenuItemClicked;
        _duplicateSiteMenuItem.Click += OnDuplicateSiteMenuItemClicked;
        _deleteSiteMenuItem.Click += OnDeleteSiteMenuItemClicked;

        _siteContextMenu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new[] { _addSiteMenuItem, _editSiteMenuItem, _promoteToBaseMenuItem, _setAsTargetMenuItem, _duplicateSiteMenuItem, _deleteSiteMenuItem },
        };
    }

    private void ShowSiteContextMenu(MainWindowViewModel vm, double x, double z, string? siteId)
    {
        if (_mapControl is null)
            return;

        EnsureSiteContextMenu();
        if (_siteContextMenu is null || _addSiteMenuItem is null || _editSiteMenuItem is null || _promoteToBaseMenuItem is null || _setAsTargetMenuItem is null || _duplicateSiteMenuItem is null || _deleteSiteMenuItem is null)
            return;
        if (TopLevel.GetTopLevel(_mapControl) is null)
            return;

        _contextPlacement = (x, z);
        _contextSiteId = siteId;

        _addSiteMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.AddHere");
        _editSiteMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.EditSite");
        _promoteToBaseMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.SetBase");
        _setAsTargetMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.SetTarget");
        _duplicateSiteMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.CopySite");
        _deleteSiteMenuItem.Header = LocalizationService.Instance.GetString("Ui.Propagation.ContextMenu.DeleteSite");

        var hasScenarioSite = vm.Propagation.HasScenarioSite(siteId);
        var isBaseStation = vm.Propagation.IsScenarioSiteBaseStation(siteId);
        _editSiteMenuItem.IsVisible = hasScenarioSite;
        _promoteToBaseMenuItem.IsVisible = hasScenarioSite && !isBaseStation;
        _setAsTargetMenuItem.IsVisible = hasScenarioSite && isBaseStation;
        _duplicateSiteMenuItem.IsVisible = hasScenarioSite;
        _deleteSiteMenuItem.IsVisible = hasScenarioSite;

        _siteContextMenu.PlacementTarget = _mapControl;
        try
        {
            _siteContextMenu.Open(_mapControl);
        }
        catch (ArgumentNullException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnAddSiteMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !_contextPlacement.HasValue)
            return;

        vm.Propagation.BeginScenarioSitePlacement(_contextPlacement.Value.X, _contextPlacement.Value.Z);
    }

    private void OnEditSiteMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(_contextSiteId))
            return;

        vm.Propagation.BeginScenarioSiteEdit(_contextSiteId);
    }

    private void OnPromoteToBaseMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(_contextSiteId))
            return;

        vm.Propagation.SetScenarioSiteRole(_contextSiteId, Services.PropagationSiteRole.BaseStation);
    }

    private void OnSetAsTargetMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(_contextSiteId))
            return;

        vm.Propagation.SetScenarioSiteRole(_contextSiteId, Services.PropagationSiteRole.TargetNode);
    }

    private void OnDuplicateSiteMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(_contextSiteId))
            return;

        vm.Propagation.DuplicateScenarioSite(_contextSiteId);
    }

    private void OnDeleteSiteMenuItemClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(_contextSiteId))
            return;

        vm.Propagation.RequestDeleteScenarioSite(_contextSiteId);
    }

    private bool TryResolveMapPosition(MainWindowViewModel vm, Point position, out double x, out double z)
    {
        x = 0d;
        z = 0d;
        if (_mapControl?.Map?.Navigator?.Viewport is not { } viewport)
            return false;

        x = viewport.CenterX + ((position.X - (viewport.Width * 0.5d)) * viewport.Resolution);
        z = viewport.CenterY - ((position.Y - (viewport.Height * 0.5d)) * viewport.Resolution);
        return true;
    }
}
