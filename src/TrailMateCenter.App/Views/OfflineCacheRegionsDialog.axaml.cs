using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TrailMateCenter.Localization;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class OfflineCacheRegionsDialog : Window
{
    private bool _statusAutoRefreshed;
    private readonly DispatcherTimer _statusAutoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _lastCachingState;

    public OfflineCacheRegionsDialog()
    {
        InitializeComponent();
        _statusAutoRefreshTimer.Tick += OnStatusAutoRefreshTick;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _statusAutoRefreshTimer.Start();

        if (_statusAutoRefreshed)
            return;

        _statusAutoRefreshed = true;
        await RefreshStatusesAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusAutoRefreshTimer.Stop();
        _statusAutoRefreshTimer.Tick -= OnStatusAutoRefreshTick;
        base.OnClosed(e);
    }

    private void OnJumpClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.FocusSelectedOfflineCacheRegionOnMap();
        }
    }

    private void OnRegionListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.FocusSelectedOfflineCacheRegionOnMap();
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnRefreshStatusClicked(object? sender, RoutedEventArgs e)
    {
        await RefreshStatusesAsync();
    }

    private async void OnStatusAutoRefreshTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var isCaching = vm.Map.IsOfflineCacheRunning;
        try
        {
            if (isCaching)
            {
                // Keep UI responsive while caching by checking selected region only.
                await vm.RefreshOfflineCacheRegionsHealthAsync(selectedOnly: true);
            }
            else if (_lastCachingState)
            {
                // Cache just finished; run a full refresh once.
                await vm.RefreshOfflineCacheRegionsHealthAsync();
            }
        }
        catch
        {
            // Ignore timer refresh failures; manual refresh remains available.
        }
        finally
        {
            _lastCachingState = isCaching;
        }
    }

    private async Task RefreshStatusesAsync()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshOfflineCacheRegionsHealthAsync();
        }
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.CanExportSelectedOfflineCacheRegion)
            return;

        var loc = LocalizationService.Instance;
        if (!StorageProvider.CanPickFolder)
        {
            vm.OfflineCacheExportStatusText = loc.GetString("Error.OfflineCacheExportFolderPickerUnavailable");
            return;
        }

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = loc.GetString("Ui.Dashboard.OfflineCacheRegionsDialog.ExportPickFolder"),
            });
        }
        catch (Exception ex)
        {
            vm.OfflineCacheExportStatusText = loc.Format("Status.OfflineCache.ExportFailed", ex.Message);
            return;
        }

        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        var destinationRoot = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            vm.OfflineCacheExportStatusText = loc.GetString("Error.OfflineCacheExportLocalPathUnavailable");
            return;
        }

        await vm.ExportSelectedOfflineCacheRegionAsync(destinationRoot, CancellationToken.None);
    }
}
