using Avalonia.Controls;
using Avalonia.Interactivity;
using TrailMateCenter.ViewModels;

namespace TrailMateCenter.Views;

public partial class OfflineCacheDialog : Window
{
    public OfflineCacheDialog()
    {
        InitializeComponent();
    }

    public OfflineCacheDialogResult? Result { get; private set; }

    public async Task<OfflineCacheDialogResult?> ShowDialogWithResultAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnSaveOnlyClicked(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as OfflineCacheDialogViewModel;
        if (vm is null)
        {
            Close();
            return;
        }

        Result = new OfflineCacheDialogResult(
            OfflineCacheDialogAction.SaveOnly,
            NormalizeRegionName(vm.RegionName),
            SaveRegion: true,
            vm.ToBuildOptions());
        Close();
    }

    private void OnBuildClicked(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as OfflineCacheDialogViewModel;
        if (vm is null || !vm.CanBuild)
            return;

        Result = new OfflineCacheDialogResult(
            OfflineCacheDialogAction.Build,
            NormalizeRegionName(vm.RegionName),
            vm.SaveRegion,
            vm.ToBuildOptions());
        Close();
    }

    private static string NormalizeRegionName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
