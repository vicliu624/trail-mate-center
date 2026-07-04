using System;
using Avalonia;
using Mapsui;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Mapsui.Extensions;
using Mapsui.Manipulations;
using Mapsui.Projections;
using Mapsui.UI.Avalonia;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class DashboardView : UserControl
{
    private const double OfflineSelectionClosePointRadiusPx = 14;

    private MapControl? _mapControl;
    private Canvas? _offlineSelectionCanvas;
    private Button? _offlineSelectionCompleteButton;
    private ToggleButton? _offlineSelectionModeToggle;
    private readonly List<Point> _offlineSelectionScreenPath = new();
    private readonly List<MPoint> _offlineSelectionWorldPath = new();
    private Point? _offlineSelectionPreviewPoint;
    private bool _suppressNextMapTap;
    private bool _offlineCacheDialogOpen;
    private bool _offlineCacheRegionsDialogOpen;
    private bool _mapPackBuilderDialogOpen;
    private ContextMenu? _offlineRouteContextMenu;
    private MenuItem? _offlineRouteCacheMenuItem;
    private MenuItem? _teamPositionMenuRootItem;
    private MenuItem? _teamPositionAreaClearedItem;
    private MenuItem? _teamPositionBaseCampItem;
    private MenuItem? _teamPositionGoodFindItem;
    private MenuItem? _teamPositionRallyItem;
    private MenuItem? _teamPositionSosItem;
    private ContextMenu? _teamPanelContextMenu;
    private MenuItem? _teamPanelStartChatMenuItem;
    private SubjectViewModel? _teamPanelContextSubject;
    private (double Latitude, double Longitude)? _lastRightClickLocation;

    public DashboardView()
    {
        InitializeComponent();
        _mapControl = this.FindControl<MapControl>("MainMapControl");
        if (_mapControl is null)
            return;

        _offlineSelectionCanvas = this.FindControl<Canvas>("OfflineSelectionCanvas");
        _offlineSelectionCompleteButton = this.FindControl<Button>("OfflineSelectionCompleteButton");
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

    private void EnsureTeamPanelContextMenu()
    {
        if (_teamPanelContextMenu is not null)
            return;

        _teamPanelStartChatMenuItem = new MenuItem();
        _teamPanelStartChatMenuItem.Click += OnTeamPanelStartChatMenuItemClicked;
        _teamPanelContextMenu = new ContextMenu
        {
            ItemsSource = new[] { _teamPanelStartChatMenuItem },
            Placement = PlacementMode.Pointer,
        };
    }

    private void OnTeamPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;
        if (sender is not Control target)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var subject = ResolveSubjectFromVisualSource(e.Source)
            ?? (target as ListBox)?.SelectedItem as SubjectViewModel;
        if (subject is null)
            return;

        vm.SelectedSubject = subject;
        ShowTeamPanelContextMenu(target, subject);
        e.Handled = true;
    }

    private void ShowTeamPanelContextMenu(Control target, SubjectViewModel subject)
    {
        EnsureTeamPanelContextMenu();
        if (_teamPanelContextMenu is null || _teamPanelStartChatMenuItem is null)
            return;
        if (TopLevel.GetTopLevel(target) is null)
            return;

        _teamPanelContextSubject = subject;
        _teamPanelStartChatMenuItem.Header = LocalizationService.Instance.GetString("Ui.Dashboard.Team.StartChat");
        _teamPanelStartChatMenuItem.IsEnabled = subject.Id != 0;

        _teamPanelContextMenu.PlacementTarget = target;
        try
        {
            _teamPanelContextMenu.Open(target);
        }
        catch (ArgumentNullException)
        {
            // Ignore transient detach between right click and popup open.
        }
        catch (InvalidOperationException)
        {
            // Ignore transient popup placement failures to avoid UI crash.
        }
    }

    private void OnTeamPanelStartChatMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (_teamPanelContextSubject is null)
            return;

        vm.StartChatWithSubject(_teamPanelContextSubject);
    }

    private static SubjectViewModel? ResolveSubjectFromVisualSource(object? source)
    {
        var element = source as StyledElement;
        while (element is not null)
        {
            if (element.DataContext is SubjectViewModel subject)
                return subject;
            element = element.Parent as StyledElement;
        }

        return null;
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
            AddOfflineSelectionVertex(e.GetPosition(_mapControl));
            _suppressNextMapTap = true;
            e.Handled = true;
            return;
        }

        DisableFollowLatestForManualNavigation();
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mapControl is null || !IsOfflineSelectionModeActive())
            return;

        _offlineSelectionPreviewPoint = _offlineSelectionScreenPath.Count > 0
            ? ClampPointToMapBounds(e.GetPosition(_mapControl))
            : null;
        UpdateOfflineSelectionVisual();
        e.Handled = true;
    }

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsOfflineSelectionModeActive())
        {
            e.Handled = true;
            return;
        }

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
        if (IsOfflineSelectionModeActive())
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                CompleteOfflineSelection();
            }

            e.Handled = true;
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        var position = e.GetPosition(_mapControl);
        var hasRouteAction = vm.Map.SelectOfflineRouteAtScreenPosition(
            new ScreenPosition(position.X, position.Y),
            _mapControl.GetMapInfo);

        var nodeId = vm.Map.ResolveNodeIdAtScreenPosition(
            new ScreenPosition(position.X, position.Y),
            _mapControl.GetMapInfo);
        if (nodeId.HasValue)
        {
            vm.SelectSubjectByNodeId(nodeId.Value, switchToChatTab: !vm.HasTeams);
        }

        if (_mapControl.Map is { } map)
        {
            var viewport = map.Navigator.Viewport;
            var world = viewport.ScreenToWorld(new ScreenPosition(position.X, position.Y));
            var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
            _lastRightClickLocation = (lat, lon);
        }
        else
        {
            _lastRightClickLocation = null;
        }

        if (hasRouteAction || vm.HasTeams)
        {
            ShowOfflineRouteContextMenu(vm, hasRouteAction);
            e.Handled = true;
            return;
        }

        if (!nodeId.HasValue)
            return;

        e.Handled = true;
    }

    private void EnsureOfflineRouteContextMenu()
    {
        if (_offlineRouteContextMenu is not null)
            return;

        _offlineRouteCacheMenuItem = new MenuItem();
        _offlineRouteCacheMenuItem.Click += OnOfflineRouteCacheMenuItemClicked;

        _teamPositionMenuRootItem = new MenuItem();
        _teamPositionAreaClearedItem = BuildTeamPositionMenuItem("Ui.Dashboard.TeamPosition.AreaCleared", "AreaCleared.png", TeamLocationSource.AreaCleared);
        _teamPositionBaseCampItem = BuildTeamPositionMenuItem("Ui.Dashboard.TeamPosition.BaseCamp", "BaseCamp.png", TeamLocationSource.BaseCamp);
        _teamPositionGoodFindItem = BuildTeamPositionMenuItem("Ui.Dashboard.TeamPosition.GoodFind", "GoodFind.png", TeamLocationSource.GoodFind);
        _teamPositionRallyItem = BuildTeamPositionMenuItem("Ui.Dashboard.TeamPosition.Rally", "rally.png", TeamLocationSource.Rally);
        _teamPositionSosItem = BuildTeamPositionMenuItem("Ui.Dashboard.TeamPosition.Sos", "sos.png", TeamLocationSource.Sos);
        _teamPositionMenuRootItem.ItemsSource = new MenuItem[]
        {
            _teamPositionAreaClearedItem,
            _teamPositionBaseCampItem,
            _teamPositionGoodFindItem,
            _teamPositionRallyItem,
            _teamPositionSosItem,
        };

        _offlineRouteContextMenu = new ContextMenu
        {
            ItemsSource = new[] { _offlineRouteCacheMenuItem, _teamPositionMenuRootItem },
            Placement = PlacementMode.Pointer,
        };
    }

    private MenuItem BuildTeamPositionMenuItem(string textKey, string iconFile, TeamLocationSource source)
    {
        var item = new MenuItem
        {
            Tag = source,
        };
        item.Click += OnTeamPositionMenuItemClicked;
        item.Icon = CreateMenuIcon(iconFile);
        item.Header = LocalizationService.Instance.GetString(textKey);
        return item;
    }

    private static Image? CreateMenuIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://TrailMateCenter.App/Assets/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Image
            {
                Source = new Bitmap(stream),
                Width = 16,
                Height = 16,
                Stretch = Avalonia.Media.Stretch.Uniform,
            };
        }
        catch
        {
            return null;
        }
    }

    private void ShowOfflineRouteContextMenu(MainWindowViewModel vm, bool hasRouteAction)
    {
        if (_mapControl is null)
            return;

        EnsureOfflineRouteContextMenu();
        if (_offlineRouteContextMenu is null ||
            _offlineRouteCacheMenuItem is null ||
            _teamPositionMenuRootItem is null ||
            _teamPositionAreaClearedItem is null ||
            _teamPositionBaseCampItem is null ||
            _teamPositionGoodFindItem is null ||
            _teamPositionRallyItem is null ||
            _teamPositionSosItem is null)
            return;

        var loc = LocalizationService.Instance;
        _offlineRouteCacheMenuItem.Header = loc.GetString("Ui.Dashboard.OfflineRoute.Cache");
        _offlineRouteCacheMenuItem.IsVisible = hasRouteAction;

        _teamPositionMenuRootItem.Header = loc.GetString("Ui.Dashboard.TeamPosition");
        _teamPositionMenuRootItem.IsVisible = vm.HasTeams;
        _teamPositionMenuRootItem.IsEnabled = vm.IsConnected &&
                                              vm.SupportsTeamAppPosting &&
                                              _lastRightClickLocation.HasValue;

        _teamPositionAreaClearedItem.Header = loc.GetString("Ui.Dashboard.TeamPosition.AreaCleared");
        _teamPositionBaseCampItem.Header = loc.GetString("Ui.Dashboard.TeamPosition.BaseCamp");
        _teamPositionGoodFindItem.Header = loc.GetString("Ui.Dashboard.TeamPosition.GoodFind");
        _teamPositionRallyItem.Header = loc.GetString("Ui.Dashboard.TeamPosition.Rally");
        _teamPositionSosItem.Header = loc.GetString("Ui.Dashboard.TeamPosition.Sos");

        var mapControl = _mapControl;
        if (TopLevel.GetTopLevel(mapControl) is null)
            return;

        _offlineRouteContextMenu.PlacementTarget = mapControl;
        try
        {
            _offlineRouteContextMenu.Open(mapControl);
        }
        catch (ArgumentNullException)
        {
            // Control can be detached between pointer release and menu opening.
        }
        catch (InvalidOperationException)
        {
            // Ignore transient popup placement failures to avoid UI crash.
        }
    }

    private async void OnOfflineRouteCacheMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.Map.BuildOfflineCacheSelectionFromSelectedRoute(8))
            return;

        await ShowOfflineCacheDialogAsync(vm);
    }

    private async void OnTeamPositionMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!_lastRightClickLocation.HasValue)
            return;
        if (sender is not MenuItem menuItem)
            return;
        if (menuItem.Tag is not TeamLocationSource source)
            return;

        var point = _lastRightClickLocation.Value;
        await vm.SendTeamLocationPostAsync(source, point.Latitude, point.Longitude);
    }

    private void OnMapPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        UpdateOfflineSelectionVisual();
    }

    private async Task ApplyOfflineSelectionAsync()
    {
        if (_mapControl?.Map is null)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;
        var worldPoints = _offlineSelectionWorldPath.ToList();

        if (worldPoints.Count < 3)
            return;

        if (!vm.Map.SetOfflineCacheSelectionPolygonFromWorldPoints(worldPoints))
            return;

        ResetOfflineSelectionDraft();
        vm.Map.IsOfflineCacheSelectionMode = false;
        SyncOfflineSelectionPanLock();

        await ShowOfflineCacheDialogAsync(vm);
    }

    private void CompleteOfflineSelection()
    {
        if (_offlineSelectionWorldPath.Count < 3)
            return;

        _ = ApplyOfflineSelectionAsync();
        SyncOfflineSelectionPanLock();
    }

    private void UpdateOfflineSelectionVisual()
    {
        if (_offlineSelectionCanvas is null)
            return;

        _offlineSelectionCanvas.Children.Clear();

        var previewPath = new List<Point>(_offlineSelectionScreenPath.Count + 1);
        previewPath.AddRange(_offlineSelectionScreenPath);
        if (_offlineSelectionPreviewPoint.HasValue && previewPath.Count > 0)
            previewPath.Add(_offlineSelectionPreviewPoint.Value);

        if (previewPath.Count >= 3)
        {
            _offlineSelectionCanvas.Children.Add(new Polygon
            {
                Points = previewPath,
                Fill = new SolidColorBrush(Color.FromArgb(42, 83, 199, 255)),
                StrokeThickness = 0,
            });
        }

        if (previewPath.Count >= 2)
        {
            _offlineSelectionCanvas.Children.Add(new Polyline
            {
                Points = previewPath,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 77, 213, 255)),
                StrokeThickness = 3,
                Fill = Brushes.Transparent,
            });
        }

        for (var i = 0; i < _offlineSelectionScreenPath.Count; i++)
        {
            var point = _offlineSelectionScreenPath[i];
            var isCloseTarget = i == 0 && _offlineSelectionScreenPath.Count >= 3;
            var size = isCloseTarget ? 14 : 10;
            var marker = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(isCloseTarget
                    ? Color.FromArgb(245, 159, 232, 112)
                    : Color.FromArgb(245, 83, 199, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 248, 250, 252)),
                StrokeThickness = isCloseTarget ? 2 : 1.5,
            };
            Canvas.SetLeft(marker, point.X - (size * 0.5));
            Canvas.SetTop(marker, point.Y - (size * 0.5));
            _offlineSelectionCanvas.Children.Add(marker);
        }

        _offlineSelectionCanvas.IsVisible = previewPath.Count > 0;
        UpdateOfflineSelectionActionState();
    }

    private void HideOfflineSelectionVisual()
    {
        if (_offlineSelectionCanvas is not null)
        {
            _offlineSelectionCanvas.Children.Clear();
            _offlineSelectionCanvas.IsVisible = false;
        }

        UpdateOfflineSelectionActionState();
    }

    private void OnOfflineSelectionModeToggled(object? sender, RoutedEventArgs e)
    {
        if (!IsOfflineSelectionModeActive())
        {
            ResetOfflineSelectionDraft();
        }
        else
        {
            _offlineSelectionPreviewPoint = null;
            UpdateOfflineSelectionVisual();
        }

        SyncOfflineSelectionPanLock();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SyncOfflineSelectionPanLock();
        UpdateOfflineSelectionActionState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        SyncOfflineSelectionPanLock();
        UpdateOfflineSelectionActionState();
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
        UpdateOfflineSelectionActionState();
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

    private void AddOfflineSelectionVertex(Point point)
    {
        if (_mapControl?.Map is null)
            return;

        var clamped = ClampPointToMapBounds(point);
        if (_offlineSelectionScreenPath.Count >= 3 && IsNearFirstOfflineSelectionPoint(clamped))
        {
            CompleteOfflineSelection();
            return;
        }

        if (_offlineSelectionScreenPath.Count > 0 && IsNearLastOfflineSelectionPoint(clamped))
            return;

        var viewport = _mapControl.Map.Navigator.Viewport;
        var world = viewport.ScreenToWorld(new ScreenPosition(clamped.X, clamped.Y));
        _offlineSelectionScreenPath.Add(clamped);
        _offlineSelectionWorldPath.Add(new MPoint(world.X, world.Y));
        _offlineSelectionPreviewPoint = null;
        UpdateOfflineSelectionVisual();
    }

    private bool IsNearFirstOfflineSelectionPoint(Point point)
    {
        if (_offlineSelectionScreenPath.Count == 0)
            return false;

        return GetPointDistanceSquared(point, _offlineSelectionScreenPath[0]) <=
               OfflineSelectionClosePointRadiusPx * OfflineSelectionClosePointRadiusPx;
    }

    private bool IsNearLastOfflineSelectionPoint(Point point)
    {
        if (_offlineSelectionScreenPath.Count == 0)
            return false;

        return GetPointDistanceSquared(point, _offlineSelectionScreenPath[^1]) <= 16;
    }

    private static double GetPointDistanceSquared(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

    private void ResetOfflineSelectionDraft()
    {
        _offlineSelectionScreenPath.Clear();
        _offlineSelectionWorldPath.Clear();
        _offlineSelectionPreviewPoint = null;
        HideOfflineSelectionVisual();
    }

    private void UpdateOfflineSelectionActionState()
    {
        if (_offlineSelectionCompleteButton is not null)
        {
            _offlineSelectionCompleteButton.IsEnabled =
                IsOfflineSelectionModeActive() && _offlineSelectionWorldPath.Count >= 3;
        }
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
            var defaults = vm.SelectedOfflineCacheRegion?.ToBuildOptions() ?? new OfflineCacheBuildOptions
            {
                IncludeOsm = true,
                IncludeTerrain = true,
                IncludeSatellite = true,
                IncludeContours = vm.ContoursEnabled,
                IncludeUltraFineContours = vm.ContoursEnabled && vm.ContoursUltraFine,
                MinimumZoom = OfflineCacheBuildOptions.DefaultMinimumZoom,
                MaximumZoom = OfflineCacheBuildOptions.DefaultMaximumZoom,
            };

            var dialogVm = new OfflineCacheDialogViewModel
            {
                RegionName = defaultName,
                SaveRegion = true,
                IncludeOsm = defaults.IncludeOsm,
                IncludeTerrain = defaults.IncludeTerrain,
                IncludeSatellite = defaults.IncludeSatellite,
                IncludeContours = defaults.IncludeContours,
                IncludeUltraFineContours = defaults.IncludeUltraFineContours,
                MinimumZoom = defaults.MinimumZoom,
                MaximumZoom = defaults.MaximumZoom,
                EnablePoiSeparation = defaults.EnablePoiSeparation,
                PoiPbfPath = defaults.PoiPbfPath,
                GenerateFullPoisJsonl = defaults.GenerateFullPoisJsonl,
                GenerateTileIndexedPoiFiles = defaults.GenerateTileIndexedPoiFiles,
                PoiIndexMinimumZoom = defaults.PoiIndexMinimumZoom,
                PoiIndexMaximumZoom = defaults.PoiIndexMaximumZoom,
                MaxPoiPerTile = defaults.MaxPoiPerTile,
                IncludePoiLabels = defaults.IncludePoiLabels,
                IncludeOriginalOsmTags = defaults.IncludeOriginalOsmTags,
                PoiOutputFormat = defaults.PoiOutputFormat,
            };
            foreach (var option in dialogVm.PoiTypes)
            {
                option.IsSelected = defaults.SelectedPoiTypes.Contains(option.Id, StringComparer.OrdinalIgnoreCase);
            }

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

    private async void OnMapPackBuilderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        await ShowMapPackBuilderDialogAsync(vm);
    }

    private void OnClearPoiPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.Map.ClearPoiPreview();
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

    private async Task ShowMapPackBuilderDialogAsync(MainWindowViewModel vm)
    {
        if (_mapPackBuilderDialogOpen)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        _mapPackBuilderDialogOpen = true;
        try
        {
            var dialog = new MapPackBuilderDialog(vm);
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _mapPackBuilderDialogOpen = false;
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

    private void OnCompleteOfflineCacheSelectionClicked(object? sender, RoutedEventArgs e)
    {
        CompleteOfflineSelection();
    }

    private void OnClearOfflineCacheSelectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        ResetOfflineSelectionDraft();
        vm.Map.IsOfflineCacheSelectionMode = false;
        if (vm.Map.HasOfflineCacheSelection)
        {
            vm.Map.ClearOfflineCacheSelection();
        }

        SyncOfflineSelectionPanLock();
    }

    private void OnMapLogPanelPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Keep wheel interaction inside log panel to prevent map zoom/pan passthrough.
        e.Handled = true;
    }
}
