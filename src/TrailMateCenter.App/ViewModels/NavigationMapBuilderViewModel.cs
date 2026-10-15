using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrailMateCenter.Localization;
using TrailMateCenter.Maps;
using TrailMateCenter.Maps.Tmap;

namespace TrailMateCenter.ViewModels;

public sealed partial class NavigationMapBuilderViewModel : ObservableObject
{
    private readonly Func<TmapNavigationExportPlan, CancellationToken, IProgress<TmapNavigationExportProgress>, Task<string>> _export;
    private CancellationTokenSource? _cancellation;
    private static string T(string key) => LocalizationService.Instance.GetString("Ui.NavigationMap." + key);

    public NavigationMapBuilderViewModel(string pbfPath = "", string outputRoot = "", GeoBounds? bounds = null,
        Func<TmapNavigationExportPlan, CancellationToken, IProgress<TmapNavigationExportProgress>, Task<string>>? export = null)
    {
        _export = export ?? ((plan, token, progress) => new TmapNavigationExportService().ExportAsync(plan, token, progress));
        _pbfPath = pbfPath;
        _outputRoot = outputRoot;
        var area = bounds ?? new GeoBounds(0, 0, 1, 1);
        _west = area.West; _south = area.South; _east = area.East; _north = area.North;
        _toolsDirectory = FindTools();
        _pythonExecutable = FindPython();
        _advancedExpanded = string.IsNullOrWhiteSpace(_toolsDirectory) || string.IsNullOrWhiteSpace(_pythonExecutable);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanCreate);
        CancelCommand = new RelayCommand(() => { _cancellation?.Cancel(); StatusText = T(HasResult ? "CancelingPreview" : "Canceling"); }, () => IsBusy);
        _statusText = T("Ready");
        AdditionalSources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AdditionalSources));
        RemoveSourceCommand = new RelayCommand(() =>
        {
            if (SelectedAdditionalSource is not { } selected) return;
            AdditionalSources.Remove(selected); SelectedAdditionalSource = AdditionalSources.FirstOrDefault();
        }, () => !IsBusy && SelectedAdditionalSource is not null);
    }

    public IAsyncRelayCommand CreateCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RemoveSourceCommand { get; }
    public ObservableCollection<string> AdditionalSources { get; } = new();
    [ObservableProperty] private string? _selectedAdditionalSource;
    [ObservableProperty] private string _pbfPath = "";
    [ObservableProperty] private string _outputRoot = "";
    [ObservableProperty] private string _packName = "outdoor-map";
    [ObservableProperty] private string _toolsDirectory = "";
    [ObservableProperty] private string _pythonExecutable = "";
    [ObservableProperty] private int _travelModeIndex;
    [ObservableProperty] private bool _includeTerrain;
    [ObservableProperty] private string _hgtPath = "";
    [ObservableProperty] private double _west, _south, _east, _north;
    [ObservableProperty] private int _contourInterval = 10;
    [ObservableProperty] private string _terrainAttribution = "";
    [ObservableProperty] private int _datumIndex;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private int _landmarkIndex = 1;
    [ObservableProperty] private bool _runtimeConditions = true;
    [ObservableProperty] private bool _advancedExpanded;
    [ObservableProperty] private bool _cropExpanded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorDetails = "";
    [ObservableProperty] private string _resultPath = "";
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private TmapOutdoorPreview? _preview;
    public bool HasPreview => Preview is not null;
    public bool HasError => ErrorDetails.Length != 0;
    public bool HasResult => ResultPath.Length != 0;
    public string CancelLabel => T(HasResult ? "CancelPreview" : "Cancel");
    public bool CanCreate => !IsBusy && ValidationMessage.Length == 0;
    public string OutputPreview => string.IsNullOrWhiteSpace(OutputRoot) ? T("ChooseOutput") : Path.Combine(OutputRoot, PackName);
    public string ValidationMessage
    {
        get
        {
            if (!File.Exists(PbfPath)) return T("NeedPbf");
            if (AdditionalSources.Count > 32 || AdditionalSources.Any(p => !File.Exists(p) ||
                string.Equals(Path.GetFullPath(p), Path.GetFullPath(PbfPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) return T("NeedRegionalSources");
            if (string.IsNullOrWhiteSpace(PackName) || PackName is "." or ".." || PackName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || PackName.Contains('/') || PackName.Contains('\\')) return T("NeedName");
            if (string.IsNullOrWhiteSpace(OutputRoot)) return T("ChooseOutput");
            if (!File.Exists(Path.Combine(ToolsDirectory, "import_osm.py")) || !File.Exists(Path.Combine(ToolsDirectory, "tmap_pack.py"))) return T("NeedTools");
            if (string.IsNullOrWhiteSpace(PythonExecutable)) return T("NeedPython");
            if (IncludeTerrain && !File.Exists(HgtPath)) return T("NeedHgt");
            if (IncludeTerrain && !TryHgtBounds(out _)) return T("NeedHgtName");
            if (IncludeTerrain && (!double.IsFinite(West) || !double.IsFinite(South) || !double.IsFinite(East) || !double.IsFinite(North) || West < -180 || East > 180 || South < -90 || North > 90 || West >= East || South >= North)) return T("NeedBounds");
            if (IncludeTerrain && ContourInterval is < 5 or > 1000) return T("NeedInterval");
            if (IncludeTerrain && TryHgtBounds(out var tile) && (West < tile.West || East > tile.East || South < tile.South || North > tile.North))
                return string.Format(CultureInfo.CurrentUICulture, T("NeedTileBounds"), tile.West, tile.South, tile.East, tile.North);
            if (PageIndex is < 0 or > 2 || LandmarkIndex is < 0 or > 2 || TravelModeIndex is < 0 or > 1) return T("InvalidOptions");
            try
            {
                if (Directory.Exists(OutputPreview) || File.Exists(OutputPreview))
                    return HasResult && Path.GetDirectoryName(ResultPath) == OutputPreview ? T("ResultSavedHint") : T("OutputExists");
            }
            catch (Exception) { return T("ChooseOutput"); }
            return "";
        }
    }

    public TmapNavigationExportPlan BuildPlan()
    {
        if (ValidationMessage.Length != 0) throw new InvalidOperationException(ValidationMessage);
        return new TmapNavigationExportPlan
        {
            PbfPath = PbfPath,
            OutputDirectory = OutputPreview,
            ToolsDirectory = ToolsDirectory,
            RegionalPbfPaths = AdditionalSources.ToArray(),
            PythonExecutable = PythonExecutable,
            TravelMode = TravelModeIndex == 0 ? "foot" : "bicycle",
            PageBytes = new[] { 1024, 2048, 4096 }[PageIndex],
            Landmarks = new[] { 0, 2, 4 }[LandmarkIndex],
            RuntimeConditions = RuntimeConditions,
            TopologyEpoch = "center-gui-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"),
            HgtPath = IncludeTerrain ? HgtPath : null,
            TerrainBounds = IncludeTerrain ? new GeoBounds(West, South, East, North) : null,
            ContourIntervalMeters = ContourInterval,
            TerrainVerticalDatum = (uint)Math.Clamp(DatumIndex, 0, 3),
            TerrainAttribution = TerrainAttribution
        };
    }

    public void AddSources(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        foreach (var path in paths)
            if (!AdditionalSources.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) AdditionalSources.Add(path);
        SelectedAdditionalSource ??= AdditionalSources.FirstOrDefault();
    }

    partial void OnHgtPathChanged(string value)
    {
        if (TryHgtBounds(out var tile) && (West < tile.West || East > tile.East || South < tile.South || North > tile.North)) CropExpanded = true;
    }
    private bool TryHgtBounds(out GeoBounds bounds)
    {
        bounds = default;
        var match = Regex.Match(Path.GetFileName(HgtPath), @"^([NS])([0-9]{2})([EW])([0-9]{3})\.hgt(?:\.gz)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var south = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * (match.Groups[1].Value.Equals("S", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
        var west = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * (match.Groups[3].Value.Equals("W", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
        if (south is < -90 or > 89 || west is < -180 or > 179) return false;
        bounds = new(west, south, west + 1, south + 1);
        return true;
    }

    public async Task OpenExistingAsync(string path)
    {
        if (IsBusy) return;
        ErrorDetails = ""; IsBusy = true; StatusText = T("ReadingExisting");
        _cancellation = new CancellationTokenSource();
        try
        {
            var token = _cancellation.Token;
            var data = await Task.Run(() => TmapOutdoorPreviewReader.Read(path, token), token);
            Preview = data; ResultPath = path;
            ResultSummary = $"{data.FileBytes / 1048576d:F2} MiB · {data.Nodes:N0} {T("Nodes")} · {data.DirectedEdges:N0} {T("Edges")}";
            StatusText = T("ResultReady");
        }
        catch (OperationCanceledException) { StatusText = T("PreviewCanceled"); }
        catch (Exception error) { ErrorDetails = error.Message; StatusText = T("PreviewFailed"); }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }

    private async Task CreateAsync()
    {
        if (!CanCreate) return;
        var plan = BuildPlan();
        ErrorDetails = ResultPath = ResultSummary = "";
        Preview = null;
        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            var token = _cancellation.Token;
            var progress = new Progress<TmapNavigationExportProgress>(p =>
            {
                if (IsBusy && !token.IsCancellationRequested) StatusText = T("Stage." + p.Stage);
            });
            var path = await Task.Run(() => _export(plan, token, progress), token);
            ResultPath = path;
            var reportPath = Path.Combine(Path.GetDirectoryName(path)!, "sections.json");
            try
            {
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
                var root = report.RootElement;
                var sections = root.GetProperty("sections");
                var nodes = sections.GetProperty("1101").GetProperty("records").GetInt64();
                var edges = sections.GetProperty("1102").GetProperty("records").GetInt64();
                ResultSummary = $"{root.GetProperty("bytes").GetInt64() / 1048576d:F2} MiB · {nodes:N0} {T("Nodes")} · {edges:N0} {T("Edges")}";
            }
            catch (Exception) { ResultSummary = T("ResultReady"); }
            StatusText = T("LoadingPreview");
            try { Preview = await Task.Run(() => TmapOutdoorPreviewReader.Read(path, token), token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { ErrorDetails = T("PreviewUnavailable") + Environment.NewLine + error.Message; }
            StatusText = T("Success");
        }
        catch (OperationCanceledException) { StatusText = T("Canceled"); }
        catch (Exception error)
        {
            ErrorDetails = error.Message;
            StatusText = T("Failed");
        }
        finally
        {
            _cancellation.Dispose(); _cancellation = null;
            IsBusy = false;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ValidationMessage) or nameof(CanCreate) or nameof(OutputPreview) or nameof(HasError) or nameof(HasResult) or nameof(HasPreview) or nameof(CancelLabel)) return;
        OnPropertyChanged(nameof(ValidationMessage)); OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(OutputPreview)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CancelLabel));
        CreateCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged();
        RemoveSourceCommand?.NotifyCanExecuteChanged();
    }

    private static string FindTools()
    {
        var configured = Environment.GetEnvironmentVariable("TMAP_TOOLS_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "share", "tmap", "tools"), Path.Combine(AppContext.BaseDirectory, "tmap-tools") })
            if (File.Exists(Path.Combine(path, "import_osm.py"))) return path;
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
        {
            foreach (var path in new[] { Path.Combine(parent.FullName, "tmap", "tools"), Path.Combine(parent.FullName, "share", "tmap", "tools") })
                if (File.Exists(Path.Combine(path, "import_osm.py"))) return path;
        }
        return "";
    }
    private static string FindPython()
    {
        var configured = Environment.GetEnvironmentVariable("PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var name in OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : new[] { "python3", "python" })
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
        }
        return "";
    }
}
