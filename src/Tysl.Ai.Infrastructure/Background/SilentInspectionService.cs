using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;
using Tysl.Ai.Infrastructure.Integrations.Acis;
using Tysl.Ai.Infrastructure.Persistence.Sqlite;

namespace Tysl.Ai.Infrastructure.Background;

public sealed class SilentInspectionService : ISilentInspectionService
{
    private readonly IInspectionSettingsProvider inspectionSettingsProvider;
    private readonly IDispatchService dispatchService;
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly AcisKernelPlatformSiteProvider platformSiteProvider;
    private readonly SnapshotRecordRepository snapshotRecordRepository;
    private readonly ISnapshotStorage snapshotStorage;
    private readonly ISiteRuntimeStateRepository siteRuntimeStateRepository;

    public SilentInspectionService(
        AcisKernelPlatformSiteProvider platformSiteProvider,
        ISiteLocalProfileRepository localProfileRepository,
        ISiteRuntimeStateRepository siteRuntimeStateRepository,
        IInspectionSettingsProvider inspectionSettingsProvider,
        IDispatchService dispatchService,
        ISnapshotStorage snapshotStorage,
        SnapshotRecordRepository snapshotRecordRepository)
    {
        this.platformSiteProvider = platformSiteProvider;
        this.localProfileRepository = localProfileRepository;
        this.siteRuntimeStateRepository = siteRuntimeStateRepository;
        this.inspectionSettingsProvider = inspectionSettingsProvider;
        this.dispatchService = dispatchService;
        this.snapshotStorage = snapshotStorage;
        this.snapshotRecordRepository = snapshotRecordRepository;
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken = default)
    {
        var settings = await inspectionSettingsProvider.GetAsync(cancellationToken);
        if (!settings.Enabled || !settings.IsWithinWindow(DateTimeOffset.Now) || !platformSiteProvider.IsReady)
        {
            return;
        }

        IReadOnlyList<PlatformSiteSnapshot> platformSites;
        try
        {
            platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await platformSiteProvider.WriteDiagnosticAsync(
                "SilentInspection",
                $"Inspection cycle skipped because platform list failed: {ex.Message}",
                cancellationToken);
            return;
        }

        if (platformSites.Count == 0)
        {
            return;
        }

        var localProfiles = await localProfileRepository.ListAsync(cancellationToken);
        var runtimeStates = await siteRuntimeStateRepository.ListAsync(cancellationToken);

        var localProfileMap = localProfiles.ToDictionary(
            profile => profile.DeviceCode,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);
        var runtimeStateMap = runtimeStates.ToDictionary(
            state => state.DeviceCode,
            state => state,
            StringComparer.OrdinalIgnoreCase);

        var monitoredSites = platformSites
            .Where(site => !localProfileMap.TryGetValue(site.DeviceCode, out var profile) || profile.IsMonitored)
            .OrderByDescending(site => runtimeStateMap.TryGetValue(site.DeviceCode, out var runtime) ? runtime.ConsecutiveFailureCount : 0)
            .ThenBy(site => runtimeStateMap.TryGetValue(site.DeviceCode, out var runtime) ? runtime.LastInspectionAt : null)
            .ThenBy(site => site.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, settings.MaxPointsPerCycle))
            .ToArray();

        foreach (var batch in monitoredSites.Chunk(Math.Max(1, settings.DetailBatchSize)))
        {
            var tasks = batch.Select(site =>
            {
                localProfileMap.TryGetValue(site.DeviceCode, out var localProfile);
                runtimeStateMap.TryGetValue(site.DeviceCode, out var runtimeState);
                return InspectSiteAsync(site, localProfile, runtimeState, settings, cancellationToken);
            });

            await Task.WhenAll(tasks);
        }
    }

    private async Task InspectSiteAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState? previousState,
        InspectionSettings settings,
        CancellationToken cancellationToken)
    {
        var inspectedAt = DateTimeOffset.UtcNow;

        try
        {
            var previewState = settings.PreviewResolveEnabled
                ? await ResolvePreviewStateAsync(platformSite.DeviceCode, cancellationToken)
                : PreviewResolveState.Skipped;

            var provisionalFaultCode = DetermineFaultCode(platformSite.DemoOnlineState, previewState, snapshotFailed: false);
            var provisionalSummary = BuildSummary(platformSite.DemoOnlineState, previewState, provisionalFaultCode, settings.SnapshotEnabled);

            var snapshotResult = settings.SnapshotEnabled
                ? await snapshotStorage.CaptureAsync(
                    new SnapshotCaptureRequest
                    {
                        DeviceCode = platformSite.DeviceCode,
                        DisplayName = ResolveDisplayName(platformSite, localProfile),
                        CapturedAt = inspectedAt,
                        PreviewResolveState = previewState,
                        FaultCode = provisionalFaultCode,
                        Summary = provisionalSummary
                    },
                    cancellationToken)
                : new SnapshotCaptureResult
                {
                    IsSuccess = false,
                    IsPlaceholder = true
                };

            var faultCode = DetermineFaultCode(
                platformSite.DemoOnlineState,
                previewState,
                settings.SnapshotEnabled && !snapshotResult.IsSuccess);
            var summary = BuildSummary(
                platformSite.DemoOnlineState,
                previewState,
                faultCode,
                settings.SnapshotEnabled && snapshotResult.IsSuccess);

            var runtimeState = new SiteRuntimeState
            {
                DeviceCode = platformSite.DeviceCode,
                LastInspectionAt = inspectedAt,
                LastOnlineState = platformSite.DemoOnlineState,
                LastProductState = BuildProductState(platformSite.DemoOnlineState),
                LastPreviewResolveState = previewState,
                LastSnapshotPath = snapshotResult.IsSuccess ? snapshotResult.SnapshotPath : previousState?.LastSnapshotPath,
                LastSnapshotAt = snapshotResult.IsSuccess ? snapshotResult.CapturedAt : previousState?.LastSnapshotAt,
                LastFaultCode = faultCode,
                LastFaultSummary = summary,
                ConsecutiveFailureCount = faultCode == RuntimeFaultCode.None
                    ? 0
                    : (previousState?.ConsecutiveFailureCount ?? 0) + 1,
                LastInspectionRunState = faultCode == RuntimeFaultCode.None
                    ? InspectionRunState.Succeeded
                    : InspectionRunState.SucceededWithFault,
                UpdatedAt = inspectedAt
            };

            await siteRuntimeStateRepository.UpsertAsync(runtimeState, cancellationToken);
            await dispatchService.ProcessInspectionResultAsync(
                platformSite,
                localProfile,
                runtimeState,
                cancellationToken);

            if (settings.SnapshotEnabled && snapshotResult.IsSuccess && !string.IsNullOrWhiteSpace(snapshotResult.SnapshotPath))
            {
                await snapshotRecordRepository.AddAsync(
                    platformSite.DeviceCode,
                    snapshotResult.SnapshotPath!,
                    snapshotResult.CapturedAt ?? inspectedAt,
                    snapshotResult.IsPlaceholder,
                    summary,
                    cancellationToken);

                await TrimSnapshotsAsync(
                    platformSite.DeviceCode,
                    Math.Max(1, settings.SnapshotRetentionCount),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await platformSiteProvider.WriteDiagnosticAsync(
                "SilentInspection",
                $"Inspection failed for deviceCode={platformSite.DeviceCode}: {ex.Message}",
                cancellationToken);

            var failureState = new SiteRuntimeState
            {
                DeviceCode = platformSite.DeviceCode,
                LastInspectionAt = inspectedAt,
                LastOnlineState = platformSite.DemoOnlineState,
                LastProductState = BuildProductState(platformSite.DemoOnlineState),
                LastPreviewResolveState = previousState?.LastPreviewResolveState ?? PreviewResolveState.Unknown,
                LastSnapshotPath = previousState?.LastSnapshotPath,
                LastSnapshotAt = previousState?.LastSnapshotAt,
                LastFaultCode = RuntimeFaultCode.InspectionExecutionFailed,
                LastFaultSummary = "巡检失败。",
                ConsecutiveFailureCount = (previousState?.ConsecutiveFailureCount ?? 0) + 1,
                LastInspectionRunState = InspectionRunState.Failed,
                UpdatedAt = inspectedAt
            };

            await siteRuntimeStateRepository.UpsertAsync(failureState, cancellationToken);
            await dispatchService.ProcessInspectionResultAsync(
                platformSite,
                localProfile,
                failureState,
                cancellationToken);
        }
    }

    private async Task TrimSnapshotsAsync(
        string deviceCode,
        int retentionCount,
        CancellationToken cancellationToken)
    {
        var records = await snapshotRecordRepository.ListByDeviceCodeAsync(deviceCode, cancellationToken);
        var expired = records.Skip(retentionCount).ToArray();
        if (expired.Length == 0)
        {
            return;
        }

        await snapshotRecordRepository.DeleteAsync(expired.Select(record => record.Id).ToArray(), cancellationToken);

        foreach (var record in expired)
        {
            TryDeleteFile(record.SnapshotPath);
            TryDeleteFile(Path.ChangeExtension(record.SnapshotPath, ".txt"));
        }
    }

    private async Task<PreviewResolveState> ResolvePreviewStateAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var preview = await platformSiteProvider.ResolveInspectionPreviewAsync(deviceCode, cancellationToken);
        if (preview.IsSuccess)
        {
            return PreviewResolveState.Resolved;
        }

        await platformSiteProvider.WriteDiagnosticAsync(
            "InspectionPreview",
            $"Preview resolve failed for deviceCode={deviceCode}: {preview.FailureReason}",
            cancellationToken);

        return PreviewResolveState.Failed;
    }

    private static RuntimeFaultCode DetermineFaultCode(
        DemoOnlineState onlineState,
        PreviewResolveState previewResolveState,
        bool snapshotFailed)
    {
        if (onlineState == DemoOnlineState.Offline)
        {
            return RuntimeFaultCode.Offline;
        }

        if (previewResolveState == PreviewResolveState.Failed)
        {
            return RuntimeFaultCode.PreviewResolveFailed;
        }

        if (snapshotFailed)
        {
            return RuntimeFaultCode.SnapshotFailed;
        }

        return RuntimeFaultCode.None;
    }

    private static string BuildSummary(
        DemoOnlineState onlineState,
        PreviewResolveState previewResolveState,
        RuntimeFaultCode faultCode,
        bool snapshotWritten)
    {
        if (faultCode == RuntimeFaultCode.Offline || onlineState == DemoOnlineState.Offline)
        {
            return "设备离线。";
        }

        if (faultCode == RuntimeFaultCode.InspectionExecutionFailed)
        {
            return "巡检失败。";
        }

        if (previewResolveState == PreviewResolveState.Failed)
        {
            return "预览解析失败。";
        }

        if (faultCode == RuntimeFaultCode.SnapshotFailed)
        {
            return "截图留痕失败。";
        }

        if (previewResolveState == PreviewResolveState.Skipped)
        {
            return snapshotWritten ? "巡检完成，截图已更新。" : "巡检完成。";
        }

        return snapshotWritten ? "巡检正常，截图已更新。" : "巡检正常。";
    }

    private static string BuildProductState(DemoOnlineState onlineState)
    {
        return onlineState switch
        {
            DemoOnlineState.Online => "在线",
            DemoOnlineState.Offline => "离线",
            _ => "未知"
        };
    }

    private static string ResolveDisplayName(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        return string.IsNullOrWhiteSpace(localProfile?.Alias)
            ? platformSite.DeviceName
            : localProfile.Alias!.Trim();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
