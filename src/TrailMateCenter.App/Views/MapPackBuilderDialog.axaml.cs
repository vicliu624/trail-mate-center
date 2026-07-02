using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class MapPackBuilderDialog : Window
{
    private readonly MainWindowViewModel _ownerViewModel;

    public MapPackBuilderDialog()
    {
        _ownerViewModel = null!;
        InitializeComponent();
    }

    public MapPackBuilderDialog(MainWindowViewModel ownerViewModel)
    {
        _ownerViewModel = ownerViewModel ?? throw new ArgumentNullException(nameof(ownerViewModel));
        InitializeComponent();
        DataContext = new MapPackBuilderViewModel(ownerViewModel.Map);
    }

    private MapPackBuilderViewModel? ViewModel => DataContext as MapPackBuilderViewModel;

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnApplyBoundsClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ApplyManualBounds();
    }

    private async void OnPickPbfClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !StorageProvider.CanOpen)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select OSM PBF file",
            FileTypeFilter =
            [
                new FilePickerFileType("OSM PBF")
                {
                    Patterns = ["*.osm.pbf", "*.pbf"],
                },
                FilePickerFileTypes.All,
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            ViewModel.SetLocalPbfPath(path);
    }

    private async void OnImportBoundaryClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !StorageProvider.CanOpen)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select GeoJSON boundary",
            FileTypeFilter =
            [
                new FilePickerFileType("GeoJSON")
                {
                    Patterns = ["*.geojson", "*.json"],
                    MimeTypes = ["application/geo+json", "application/json"],
                },
                FilePickerFileTypes.All,
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await ViewModel.ImportBoundaryCommand.ExecuteAsync(path);
    }

    private async void OnPickOutputFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !StorageProvider.CanPickFolder)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select output folder or SD card root",
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            ViewModel.SetOutputDirectory(path);
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanExport)
            return;

        if (string.IsNullOrWhiteSpace(ViewModel.OutputDirectory))
        {
            await OnPickOutputFolderForExportAsync(ViewModel);
            if (string.IsNullOrWhiteSpace(ViewModel.OutputDirectory))
                return;
        }

        var result = await _ownerViewModel.ExportMapPackAsync(ViewModel.BuildPlan());
        ViewModel.StatusText = result.Success
            ? $"Export complete: {result.TargetMapRoot}"
            : $"Export failed: {result.ErrorMessage ?? "unknown"}";
    }

    private async void OnBuildRasterCacheClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.HasTileSelection)
            return;

        ViewModel.ApplyManualBounds();
        ViewModel.StatusText = "Raster cache task started on the map.";
        await _ownerViewModel.Map.RunOfflineCacheForSelectionAsync(ViewModel.ToOfflineCacheBuildOptions() with
        {
            EnablePoiSeparation = false,
        });
        ViewModel.StatusText = _ownerViewModel.Map.OfflineCacheStatusText;
    }

    private async Task OnPickOutputFolderForExportAsync(MapPackBuilderViewModel vm)
    {
        if (!StorageProvider.CanPickFolder)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select output folder or SD card root",
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.SetOutputDirectory(path);
    }
}
