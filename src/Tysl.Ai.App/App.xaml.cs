using System.IO;
using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Infrastructure.Integrations.Acis;
using Tysl.Ai.Infrastructure.Persistence.Sqlite;
using Tysl.Ai.Services.Sites;
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

        platformSiteProvider = new AcisKernelPlatformSiteProvider(loadResult);

        ISiteLocalProfileRepository repository = new SiteLocalProfileRepository(connectionFactory);
        ISiteLocalProfileService siteLocalProfileService = new SiteLocalProfileService(repository);
        ISiteMapQueryService siteMapQueryService = new SiteMapQueryService(
            platformSiteProvider,
            platformSiteProvider,
            repository);

        var shellViewModel = new ShellViewModel(siteMapQueryService, siteLocalProfileService);
        var shellWindow = new ShellWindow
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
}
