using System.IO;
using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Infrastructure.Configuration;
using Tysl.Ai.Infrastructure.Integrations.Acis;
using Tysl.Ai.Infrastructure.Persistence.Sqlite;
using Tysl.Ai.Services.Sites;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views;

namespace Tysl.Ai.App;

public partial class App : Application
{
    private AcisKernelPlatformSiteProvider? platformSiteProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePath = GetDatabasePath();
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var databaseInitializer = new SqliteDatabaseInitializer(connectionFactory);
        databaseInitializer.InitializeAsync().GetAwaiter().GetResult();

        var optionsProvider = new AcisKernelOptionsProvider();
        var loadResult = optionsProvider.Load();
        var amapOptionsProvider = new AmapJsOptionsProvider();
        var amapLoadResult = amapOptionsProvider.Load();

        platformSiteProvider = new AcisKernelPlatformSiteProvider(loadResult);

        ISiteLocalProfileRepository repository = new SiteLocalProfileRepository(connectionFactory);
        ISiteLocalProfileService siteLocalProfileService = new SiteLocalProfileService(repository);
        ISiteMapQueryService siteMapQueryService = new SiteMapQueryService(
            platformSiteProvider,
            platformSiteProvider,
            repository);

        var shellViewModel = new ShellViewModel(
            siteMapQueryService,
            siteLocalProfileService,
            amapLoadResult.IsReady);
        var shellWindow = new ShellWindow(BuildMapHostConfiguration(amapLoadResult))
        {
            DataContext = shellViewModel
        };

        Exit += HandleExit;
        MainWindow = shellWindow;
        shellWindow.Show();
    }

    private void HandleExit(object? sender, ExitEventArgs e)
    {
        Exit -= HandleExit;
        platformSiteProvider?.Dispose();
        platformSiteProvider = null;
    }

    private static string GetDatabasePath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tysl.Ai",
            "data");

        Directory.CreateDirectory(appDataDirectory);
        return Path.Combine(appDataDirectory, "site-profile.db");
    }

    private static AmapHostConfiguration BuildMapHostConfiguration(AmapJsOptionsLoadResult loadResult)
    {
        var options = loadResult.Options;

        return new AmapHostConfiguration
        {
            IsConfigured = loadResult.IsReady,
            Key = options?.Key,
            SecurityJsCode = options?.SecurityJsCode,
            MapStyle = string.IsNullOrWhiteSpace(options?.MapStyle) ? "amap://styles/darkblue" : options.MapStyle,
            Zoom = options?.Zoom is > 0 and <= 20 ? options.Zoom : 11,
            Center = options?.Center is { Length: 2 }
                ? options.Center
                : [120.585316, 30.028105]
        };
    }
}
