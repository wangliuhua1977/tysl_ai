using System.IO;
using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Infrastructure.Background;
using Tysl.Ai.Infrastructure.Configuration;
using Tysl.Ai.Infrastructure.Dispatch;
using Tysl.Ai.Infrastructure.Integrations.Acis;
using Tysl.Ai.Infrastructure.Messaging;
using Tysl.Ai.Infrastructure.Persistence.Sqlite;
using Tysl.Ai.Infrastructure.Storage;
using Tysl.Ai.Services.Sites;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views;

namespace Tysl.Ai.App;

public partial class App : Application
{
    private AcisKernelPlatformSiteProvider? platformSiteProvider;
    private SilentInspectionHostedService? silentInspectionHostedService;
    private WeComWebhookSender? webhookSender;

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
        ISiteRuntimeStateRepository runtimeStateRepository = new SiteRuntimeStateRepository(connectionFactory);
        IInspectionSettingsProvider inspectionSettingsProvider = new InspectionSettingsProvider(connectionFactory);
        IDispatchPolicyProvider dispatchPolicyProvider = new DispatchPolicyProvider(connectionFactory);
        IDispatchRecordRepository dispatchRecordRepository = new DispatchRecordRepository(connectionFactory);
        ISnapshotStorage snapshotStorage = new SnapshotStorage(ProjectPathResolver.EnsureRuntimeDirectory("snapshots"));
        var snapshotRecordRepository = new SnapshotRecordRepository(connectionFactory);
        webhookSender = new WeComWebhookSender();
        IDispatchService dispatchService = new DispatchService(
            dispatchPolicyProvider,
            dispatchRecordRepository,
            repository,
            platformSiteProvider,
            webhookSender);
        ISiteLocalProfileService siteLocalProfileService = new SiteLocalProfileService(repository);
        ISiteMapQueryService siteMapQueryService = new SiteMapQueryService(
            platformSiteProvider,
            platformSiteProvider,
            repository,
            runtimeStateRepository,
            dispatchRecordRepository);

        ISilentInspectionService silentInspectionService = new SilentInspectionService(
            platformSiteProvider,
            repository,
            runtimeStateRepository,
            inspectionSettingsProvider,
            dispatchService,
            snapshotStorage,
            snapshotRecordRepository);
        silentInspectionHostedService = new SilentInspectionHostedService(
            silentInspectionService,
            inspectionSettingsProvider);
        silentInspectionHostedService.Start();

        var shellViewModel = new ShellViewModel(
            siteMapQueryService,
            siteLocalProfileService,
            dispatchService,
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
        silentInspectionHostedService?.Dispose();
        silentInspectionHostedService = null;
        webhookSender?.Dispose();
        webhookSender = null;
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
