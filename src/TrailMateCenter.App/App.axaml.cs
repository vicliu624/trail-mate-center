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
using TrailMateCenter.Services;
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

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
            builder.AddConsole();
        });

        services.AddSingleton<LogStore>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton(sp => new SqliteStore(SqliteStore.GetDefaultPath()));
        services.AddSingleton<PersistenceService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ISerialPortEnumerator, SerialPortEnumerator>();
        services.AddSingleton(sp => new SettingsStore(SettingsStore.GetDefaultPath()));
        services.AddSingleton<HostLinkClient>();
        services.AddSingleton<AprsIsClient>();
        services.AddSingleton<AprsGatewayService>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
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
