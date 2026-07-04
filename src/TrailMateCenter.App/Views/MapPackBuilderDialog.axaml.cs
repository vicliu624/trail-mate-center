using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TrailMateCenter.Localization;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class MapPackBuilderDialog : Window
{
    private readonly MainWindowViewModel _ownerViewModel;
    private static LocalizationService Loc => LocalizationService.Instance;

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
            Title = Loc.GetString("Ui.MapPack.FilePicker.PbfTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.GetString("Ui.MapPack.FileType.Osmpbf"))
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
            Title = Loc.GetString("Ui.MapPack.FilePicker.BoundaryTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.GetString("Ui.MapPack.FileType.GeoJson"))
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
            Title = Loc.GetString("Ui.MapPack.FilePicker.OutputTitle"),
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

        var cancellationToken = ViewModel.BeginExport();
        var progress = new Progress<MainWindowViewModel.OfflineCacheExportProgress>(ViewModel.ApplyExportProgress);
        MapCacheRegionViewModel? exportTaskRegion = null;
        try
        {
            ViewModel.ApplyManualBounds();
            var plan = ViewModel.BuildPlan();
            exportTaskRegion = await _ownerViewModel.RegisterMapPackExportTaskAsync(plan, cancellationToken);
            if (ViewModel.HasTileSelection)
            {
                ViewModel.ApplyTilePreparationProgress();
                await _ownerViewModel.Map.RunOfflineCacheForSelectionAsync(ViewModel.ToOfflineCacheBuildOptions() with
                {
                    EnablePoiSeparation = false,
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ViewModel.ApplyTilePreparationComplete();
            }

            var result = await _ownerViewModel.ExportMapPackAsync(plan, cancellationToken, progress, exportTaskRegion);
            ViewModel.StatusText = result.Success
                ? $"{Loc.Format("Ui.MapPack.Status.ExportComplete", result.TargetMapRoot)} {_ownerViewModel.OfflineCacheExportStatusText}"
                : Loc.Format("Ui.MapPack.Status.ExportFailed", result.ErrorMessage ?? Loc.GetString("Ui.MapPack.Unknown"));
        }
        catch (OperationCanceledException)
        {
            await _ownerViewModel.MarkMapPackExportTaskCanceledAsync(exportTaskRegion);
            ViewModel.ApplyOperationCanceled();
        }
        finally
        {
            ViewModel.EndExport();
        }
    }

    private async Task OnPickOutputFolderForExportAsync(MapPackBuilderViewModel vm)
    {
        if (!StorageProvider.CanPickFolder)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = Loc.GetString("Ui.MapPack.FilePicker.OutputTitle"),
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.SetOutputDirectory(path);
    }
}
