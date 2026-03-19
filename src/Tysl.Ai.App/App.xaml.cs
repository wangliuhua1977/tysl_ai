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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var databasePath = GetDatabasePath();
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var databaseInitializer = new SqliteDatabaseInitializer(connectionFactory);
        databaseInitializer.InitializeAsync().GetAwaiter().GetResult();

        IPlatformSiteProvider platformSiteProvider = new StubPlatformSiteProvider();
        ISiteLocalProfileRepository repository = new SiteLocalProfileRepository(connectionFactory);
        ISiteLocalProfileService siteLocalProfileService = new SiteLocalProfileService(repository);
        ISiteMapQueryService siteMapQueryService = new SiteMapQueryService(platformSiteProvider, repository);

        var shellViewModel = new ShellViewModel(siteMapQueryService, siteLocalProfileService);
        var shellWindow = new ShellWindow
        {
            DataContext = shellViewModel
        };

        MainWindow = shellWindow;
        shellWindow.Show();
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
