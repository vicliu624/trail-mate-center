using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps.Tmap;
using TrailMateCenter.Views.Controls;

namespace TrailMateCenter.Views;

public sealed class ElevationProfileWindow : Window
{
    private readonly TmapOutdoorPreview _map;
    private readonly NumericUpDown _lonA, _latA, _lonB, _latB, _spacing, _noise;
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ElevationProfileControl _chart = new() { MinHeight = 180 };
    private readonly Button _build, _cancel;
    private readonly StackPanel _inputs;
    private CancellationTokenSource? _job;
    private static string T(string key) => LocalizationService.Instance.GetString("Ui.ElevationProfile." + key);
    public ElevationProfileWindow(TmapOutdoorPreview map)
    {
        _map = map; Title = T("Title"); Width = 820; Height = 650; MinWidth = 680; MinHeight = 550;
        RequestedThemeVariant = ThemeVariant.Dark; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 16 };
        root.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#10151B"));
        _inputs = new StackPanel { Spacing = 10 };
        _inputs.Children.Add(new TextBlock { Text = T("Hint"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var middleLat = (map.Bounds.South + map.Bounds.North) / 2;
        _lonA = Number(map.Bounds.West, -180, 180); _latA = Number(middleLat, -90, 90);
        _lonB = Number(map.Bounds.East, -180, 180); _latB = Number(middleLat, -90, 90);
        _spacing = Number(30, 1, 10000); _noise = Number(3, 0, 1000);
        AddRow(T("Start"), _lonA, _latA); AddRow(T("End"), _lonB, _latB);
        AddRow(T("Sampling"), _spacing, _noise);
        root.Children.Add(_inputs); Grid.SetRow(_chart, 1); root.Children.Add(_chart);
        var footer = new StackPanel { Spacing = 12 }; Grid.SetRow(footer, 2);
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _build = new Button { Content = T("Calculate") }; _build.Click += async (_, _) => await CalculateAsync();
        _cancel = new Button { Content = T("Cancel"), IsEnabled = false }; _cancel.Click += (_, _) => _job?.Cancel();
        actions.Children.Add(_build); actions.Children.Add(_cancel); footer.Children.Add(actions);
        root.Children.Add(footer); Content = root;
        Closing += (_, _) => _job?.Cancel();
        _status.Text = T("Ready");
    }
    private static NumericUpDown Number(double value, decimal minimum, decimal maximum) => new()
    { Value = (decimal)value, Minimum = minimum, Maximum = maximum, Increment = .0001m, FormatString = "0.#######", Width = 220 };
    private void AddRow(string label, NumericUpDown first, NumericUpDown second)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(first); row.Children.Add(second); _inputs.Children.Add(row);
    }
    public async Task CalculateAsync()
    {
        if (_job is not null) return;
        if (new[] { _lonA, _latA, _lonB, _latB, _spacing, _noise }.Any(n => n.Value is null)) { _status.Text = T("Invalid"); return; }
        var points = new[] { new TmapPreviewPoint((double)_lonA.Value!.Value, (double)_latA.Value!.Value),
            new TmapPreviewPoint((double)_lonB.Value!.Value, (double)_latB.Value!.Value) };
        var spacing = (double)_spacing.Value!.Value; var noise = (double)_noise.Value!.Value;
        _job = new CancellationTokenSource(); var token = _job.Token;
        _inputs.IsEnabled = _build.IsEnabled = false; _cancel.IsEnabled = true; _chart.Profile = null; _status.Text = T("Working");
        try
        {
            var result = await Task.Run(() => TmapOutdoorPreviewReader.ReadElevationProfile(_map.SourcePath, points, spacing, noise, token: token), token);
            token.ThrowIfCancellationRequested(); _chart.Profile = result;
            _status.Text = string.Format(T("Summary"), result.DistanceMeters, result.AscentMeters, result.DescentMeters,
                result.MinimumMeters?.ToString("F0") ?? "—", result.MaximumMeters?.ToString("F0") ?? "—",
                result.DistanceMeters > 0 ? result.ValidDistanceMeters / result.DistanceMeters * 100 : 0,
                result.ResolutionMeters, result.Samples.Count);
        }
        catch (OperationCanceledException) { _status.Text = T("Canceled"); }
        catch (Exception error) { _status.Text = T("Failed") + "\n" + error.Message; }
        finally { _job.Dispose(); _job = null; _inputs.IsEnabled = _build.IsEnabled = true; _cancel.IsEnabled = false; }
    }
}
