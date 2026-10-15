using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using System.Diagnostics;
using TrailMateCenter.Localization;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class NavigationMapBuilderDialog : Window
{
    private bool _closeAfterCancel;
    private NavigationMapBuilderViewModel? ViewModel => DataContext as NavigationMapBuilderViewModel;
    private static string T(string key) => LocalizationService.Instance.GetString("Ui.NavigationMap." + key);
    public NavigationMapBuilderDialog() { InitializeComponent(); }
    public NavigationMapBuilderDialog(NavigationMapBuilderViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += OnStateChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnStateChanged;
        Closing += (_, e) =>
        {
            if (!viewModel.IsBusy) return;
            e.Cancel = true;
            _closeAfterCancel = true;
            viewModel.CancelCommand.Execute(null);
        };
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closeAfterCancel && e.PropertyName == nameof(NavigationMapBuilderViewModel.IsBusy) && ViewModel?.IsBusy == false) Close();
    }
    private async Task<string?> PickFile(string title, params string[] patterns)
    {
        if (ViewModel?.IsBusy != false || !StorageProvider.CanOpen) return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickFolder(string title)
    {
        if (ViewModel?.IsBusy != false || !StorageProvider.CanPickFolder) return null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    private async void OnPickPbf(object? sender, RoutedEventArgs e) { if (await PickFile(T("Pbf"), "*.pbf", "*.osm", "*.xml") is { } path) ViewModel!.PbfPath = path; }
    private async void OnPickHgt(object? sender, RoutedEventArgs e) { if (await PickFile(T("Hgt"), "*.hgt", "*.hgt.gz") is { } path) ViewModel!.HgtPath = path; }
    private async void OnPickPython(object? sender, RoutedEventArgs e) { if (await PickFile(T("Python"), "*") is { } path) ViewModel!.PythonExecutable = path; }
    private async void OnPickTools(object? sender, RoutedEventArgs e) { if (await PickFolder(T("Tools")) is { } path) ViewModel!.ToolsDirectory = path; }
    private async void OnPickOutput(object? sender, RoutedEventArgs e) { if (await PickFolder(T("Output")) is { } path) ViewModel!.OutputRoot = path; }
    private async void OnPickRegional(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.IsBusy != false || !StorageProvider.CanOpen) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("RegionalSources"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(T("Pbf")) { Patterns = ["*.pbf", "*.osm", "*.xml"] }]
        });
        ViewModel.AddSources(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private async void OnOpenExisting(object? sender, RoutedEventArgs e)
    {
        if (await PickFile(T("OpenExisting"), "*.tmap") is not { } path) return;
        await ViewModel!.OpenExistingAsync(path);
        if (!ViewModel.HasError && ViewModel.ResultPath == path && ViewModel.Preview is { } preview)
            await new OutdoorMapPreviewWindow(preview).ShowDialog(this);
    }
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Preview is { } preview) await new OutdoorMapPreviewWindow(preview).ShowDialog(this);
    }
    private void OnOpenResult(object? sender, RoutedEventArgs e)
    {
        var path = ViewModel?.ResultPath;
        if (path is null || !File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(new Uri(Path.GetDirectoryName(path)!).AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { ViewModel!.ErrorDetails = error.Message; }
    }
}
