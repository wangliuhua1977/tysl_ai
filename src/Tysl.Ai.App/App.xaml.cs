using System.IO;
using System.Windows;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Infrastructure.Background;
using Tysl.Ai.Infrastructure.Configuration;
using Tysl.Ai.Infrastructure.Diagnostics;
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
    private int exitCleanupStarted;
    private ILocalDiagnosticService? diagnosticService;
    private AcisKernelPlatformSiteProvider? platformSiteProvider;
    private SilentInspectionHostedService? silentInspectionHostedService;
    private WeComWebhookSender? webhookSender;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var databasePath = GetDatabasePath();
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        diagnosticService = new LocalDiagnosticService(ProjectPathResolver.EnsureRuntimeDirectory("diagnostics"));
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        var dispatchOptionsProvider = new DispatchOptionsProvider();
        var dispatchLoadResult = dispatchOptionsProvider.Load();
        var databaseInitializer = new SqliteDatabaseInitializer(connectionFactory);
        databaseInitializer.InitializeAsync(dispatchLoadResult.InitialPolicy).GetAwaiter().GetResult();

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
            diagnosticService,
            amapOptionsProvider,
            amapLoadResult.IsReady,
            amapLoadResult.Options?.MapStyle);
        var shellWindow = new ShellWindow(
            BuildMapHostConfiguration(amapLoadResult),
            diagnosticService)
        {
            DataContext = shellViewModel
        };

        MainWindow = shellWindow;
        shellWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Interlocked.Exchange(ref exitCleanupStarted, 1) == 1)
        {
            base.OnExit(e);
            return;
        }

        DispatcherUnhandledException -= HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= HandleCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        silentInspectionHostedService?.RequestStop();

        using (var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            try
            {
                silentInspectionHostedService?.StopAsync(shutdownTimeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Best effort shutdown only.
            }
        }

        silentInspectionHostedService?.Dispose();
        silentInspectionHostedService = null;
        webhookSender?.Dispose();
        webhookSender = null;
        platformSiteProvider?.Dispose();
        platformSiteProvider = null;
        if (diagnosticService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        diagnosticService = null;
        base.OnExit(e);
    }

    private void HandleDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _ = diagnosticService?.WriteAsync(
            "exception-caught",
            $"source=dispatcher, type={e.Exception.GetType().FullName}, message={e.Exception.Message}");
    }

    private void HandleCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            _ = diagnosticService?.WriteAsync(
                "exception-caught",
                $"source=appdomain, terminating={e.IsTerminating}, detail=non-exception object");
            return;
        }

        _ = diagnosticService?.WriteAsync(
            "exception-caught",
            $"source=appdomain, terminating={e.IsTerminating}, type={exception.GetType().FullName}, message={exception.Message}");
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var exception = e.Exception.Flatten();
        _ = diagnosticService?.WriteAsync(
            "exception-caught",
            $"source=taskscheduler, type={exception.GetType().FullName}, message={exception.Message}");
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
            MapStyle = string.IsNullOrWhiteSpace(options?.MapStyle) ? "default" : options.MapStyle,
            Zoom = options?.Zoom is > 0 and <= 20 ? options.Zoom : 11,
            Center = options?.Center is { Length: 2 }
                ? options.Center
                : [120.585316, 30.028105]
        };
    }
}
