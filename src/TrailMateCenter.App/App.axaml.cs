using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using TrailMateCenter.Localization;
using TrailMateCenter.Propagation.Engine;
using TrailMateCenter.Services;
using TrailMateCenter.Styling;
using TrailMateCenter.Storage;
using TrailMateCenter.Transport;
using TrailMateCenter.ViewModels;
using TrailMateCenter.Views;

namespace TrailMateCenter;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static ILogger<App> Logger { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationService.Instance.Initialize();
        ThemeService.Instance.ApplyTheme(ThemeService.DefaultTheme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();
        Logger = Services.GetRequiredService<ILogger<App>>();
        Logger.LogInformation("App starting");

        var persistence = Services.GetRequiredService<PersistenceService>();
        persistence.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        persistence.Start();

        var aprsGateway = Services.GetRequiredService<AprsGatewayService>();
        aprsGateway.Start();

        var mqttClient = Services.GetRequiredService<MeshtasticMqttClient>();
        mqttClient.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        var usePropagationMock = IsEnabledEnvironmentVariable("TRAILMATE_PROPAGATION_USE_MOCK");
        var useSimulationMock = ResolveFeatureToggle(
            "TRAILMATE_PROPAGATION_SIMULATION_USE_MOCK",
            usePropagationMock);
        var useUnityBridgeMock = ResolveFeatureToggle(
            "TRAILMATE_PROPAGATION_UNITY_BRIDGE_USE_MOCK",
            usePropagationMock);
        var useUnityProcessManagerMock = ResolveFeatureToggle(
            "TRAILMATE_PROPAGATION_UNITY_PROCESS_USE_MOCK",
            usePropagationMock || useUnityBridgeMock);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
            builder.AddConsole();
        });

        services.AddSingleton<LogStore>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ApproximateLocationService>();
        services.AddSingleton<PropagationServiceProcessManager>();
        services.AddSingleton<IPropagationSolver, FormalPropagationSolver>();
        services.AddSingleton(sp => new SqliteStore(SqliteStore.GetDefaultPath()));
        services.AddSingleton<PersistenceService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ISerialPortEnumerator, SerialPortEnumerator>();
        services.AddSingleton(sp => new SettingsStore(SettingsStore.GetDefaultPath()));
        services.AddSingleton<HostLinkClient>();
        services.AddSingleton<AprsIsClient>();
        services.AddSingleton<AprsGatewayService>();
        services.AddSingleton<MeshtasticMqttClient>();
        if (useSimulationMock)
            services.AddSingleton<IPropagationSimulationService, FakePropagationSimulationService>();
        else
            services.AddSingleton<IPropagationSimulationService, InProcessPropagationSimulationService>();

        if (useUnityBridgeMock)
            services.AddSingleton<IPropagationUnityBridge, FakePropagationUnityBridge>();
        else
            services.AddSingleton<IPropagationUnityBridge, UnityProcessPropagationBridge>();

        if (useUnityProcessManagerMock)
            services.AddSingleton<IPropagationUnityProcessManager, FakePropagationUnityProcessManager>();
        else
            services.AddSingleton<IPropagationUnityProcessManager, UnityProcessManager>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static bool IsEnabledEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        return value.Trim() switch
        {
            "1" => true,
            "yes" => true,
            "on" => true,
            _ => false,
        };
    }

    private static bool ResolveFeatureToggle(string environmentName, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return IsEnabledEnvironmentVariable(environmentName);
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
