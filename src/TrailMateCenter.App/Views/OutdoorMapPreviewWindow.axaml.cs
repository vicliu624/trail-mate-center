using Avalonia.Controls;
using Avalonia.Interactivity;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.Views;

public partial class OutdoorMapPreviewWindow : Window
{
    public OutdoorMapPreviewWindow() { InitializeComponent(); }
    public OutdoorMapPreviewWindow(TmapOutdoorPreview data) : this() { DataContext = data; }
    private void OnReset(object? sender, RoutedEventArgs e) => PreviewMap.ResetView();
    private async void OnProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TmapOutdoorPreview data) await new ElevationProfileWindow(data).ShowDialog(this);
    }
    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        PreviewMap.ShowRoads = RoadToggle.IsChecked == true;
        PreviewMap.ShowContours = ContourToggle.IsChecked == true;
    }
}
