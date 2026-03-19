using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteMapQueryService : ISiteMapQueryService
{
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformConnectionStateProvider platformConnectionStateProvider;
    private readonly IPlatformSiteProvider platformSiteProvider;
    private readonly ISiteRuntimeStateRepository runtimeStateRepository;

    public SiteMapQueryService(
        IPlatformSiteProvider platformSiteProvider,
        IPlatformConnectionStateProvider platformConnectionStateProvider,
        ISiteLocalProfileRepository localProfileRepository,
        ISiteRuntimeStateRepository runtimeStateRepository)
    {
        this.platformSiteProvider = platformSiteProvider;
        this.platformConnectionStateProvider = platformConnectionStateProvider;
        this.localProfileRepository = localProfileRepository;
        this.runtimeStateRepository = runtimeStateRepository;
    }

    public async Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var mergeBundle = await BuildMergedSitesAsync(cancellationToken);
        var visibleSites = mergeBundle.Sites
            .Where(site => MatchesFilter(site, filter))
            .Where(site => MatchesSearch(site, searchText))
            .ToList();

        var visibleMapSites = visibleSites
            .Where(site => site.HasMapPoint)
            .ToList();

        return new SiteDashboardSnapshot
        {
            PlatformStatusText = mergeBundle.ConnectionState.SummaryText,
            PlatformStatusDetailText = mergeBundle.ConnectionState.DetailText,
            IsPlatformConnected = mergeBundle.ConnectionState.IsConnected,
            PointCount = mergeBundle.Sites.Count,
            MonitoredCount = mergeBundle.Sites.Count(site => site.IsMonitored),
            FaultCount = mergeBundle.Sites.Count(IsAttentionSite),
            DispatchedCount = mergeBundle.Sites.Count(site => site.DemoDispatchStatus == DispatchDemoStatus.Dispatched),
            LastRefreshedAt = DateTimeOffset.Now,
            VisiblePoints = visibleMapSites
                .Select(ToMapPoint)
                .ToList(),
            VisibleAlerts = visibleSites
                .Where(IsAttentionSite)
                .OrderByDescending(GetAlertPriority)
                .ThenByDescending(GetAlertTimestamp)
                .Select(ToAlertDigest)
                .ToList()
        };
    }

    public async Task<SiteMergedView?> GetSiteDetailAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var normalizedDeviceCode = deviceCode.Trim();
        var mergeBundle = await BuildMergedSitesAsync(cancellationToken);

        return mergeBundle.Sites.FirstOrDefault(site => site.DeviceCode.Equals(
            normalizedDeviceCode,
            StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SiteMergeBundle> BuildMergedSitesAsync(CancellationToken cancellationToken)
    {
        var platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        var platformConnectionState = platformConnectionStateProvider.GetCurrentState();
        var localProfiles = await localProfileRepository.ListAsync(cancellationToken);
        var runtimeStates = await runtimeStateRepository.ListAsync(cancellationToken);

        var localProfileMap = localProfiles.ToDictionary(
            profile => profile.DeviceCode,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);
        var runtimeStateMap = runtimeStates.ToDictionary(
            state => state.DeviceCode,
            state => state,
            StringComparer.OrdinalIgnoreCase);

        var mergedSites = platformSites
            .Select(platformSite =>
            {
                localProfileMap.TryGetValue(platformSite.DeviceCode, out var localProfile);
                runtimeStateMap.TryGetValue(platformSite.DeviceCode, out var runtimeState);
                return Merge(platformSite, localProfile, runtimeState, platformConnectionState);
            })
            .ToList();

        return new SiteMergeBundle(mergedSites, platformConnectionState);
    }

    private static SiteMergedView Merge(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState? runtimeState,
        PlatformConnectionState platformConnectionState)
    {
        var coordinateSource = ResolveCoordinateSource(platformSite, localProfile);
        var displayLongitude = coordinateSource switch
        {
            CoordinateSource.PlatformRaw => platformSite.RawLongitude,
            CoordinateSource.ManualOverride => localProfile?.ManualLongitude,
            _ => null
        };
        var displayLatitude = coordinateSource switch
        {
            CoordinateSource.PlatformRaw => platformSite.RawLatitude,
            CoordinateSource.ManualOverride => localProfile?.ManualLatitude,
            _ => null
        };
        var hasMapPoint = displayLongitude.HasValue && displayLatitude.HasValue;
        var isMonitored = localProfile?.IsMonitored ?? true;

        return new SiteMergedView
        {
            DeviceCode = platformSite.DeviceCode,
            DeviceName = platformSite.DeviceName,
            DisplayName = ResolveDisplayName(platformSite, localProfile),
            Alias = localProfile?.Alias,
            Remark = localProfile?.Remark,
            IsMonitored = isMonitored,
            PlatformRawLongitude = platformSite.RawLongitude,
            PlatformRawLatitude = platformSite.RawLatitude,
            PlatformRawCoordinateType = platformSite.RawCoordinateType,
            ManualLongitude = localProfile?.ManualLongitude,
            ManualLatitude = localProfile?.ManualLatitude,
            DisplayLongitude = displayLongitude,
            DisplayLatitude = displayLatitude,
            HasMapPoint = hasMapPoint,
            CoordinateSource = coordinateSource,
            CoordinateSourceText = ResolveCoordinateSourceText(coordinateSource),
            PlatformStatusSummary = platformConnectionState.SummaryText,
            HasRuntimeState = runtimeState is not null,
            LastInspectionAt = runtimeState?.LastInspectionAt,
            LastProductState = runtimeState?.LastProductState,
            LastPreviewResolveState = runtimeState?.LastPreviewResolveState ?? PreviewResolveState.Unknown,
            LastSnapshotPath = runtimeState?.LastSnapshotPath,
            LastSnapshotAt = runtimeState?.LastSnapshotAt,
            RuntimeFaultCode = runtimeState?.LastFaultCode ?? RuntimeFaultCode.None,
            RuntimeSummary = runtimeState?.LastFaultSummary,
            ConsecutiveFailureCount = runtimeState?.ConsecutiveFailureCount ?? 0,
            LastInspectionRunState = runtimeState?.LastInspectionRunState ?? InspectionRunState.None,
            RuntimeUpdatedAt = runtimeState?.UpdatedAt,
            AddressText = localProfile?.AddressText,
            ProductAccessNumber = localProfile?.ProductAccessNumber,
            MaintenanceUnit = localProfile?.MaintenanceUnit,
            MaintainerName = localProfile?.MaintainerName,
            MaintainerPhone = localProfile?.MaintainerPhone,
            DemoOnlineState = runtimeState?.LastOnlineState ?? platformSite.DemoOnlineState,
            DemoStatus = platformSite.DemoStatus,
            DemoDispatchStatus = platformSite.DemoDispatchStatus,
            VisualState = ResolveVisualState(platformSite, runtimeState, isMonitored),
            StatusText = ResolveStatusText(platformSite, runtimeState, isMonitored),
            HasLocalProfile = localProfile is not null,
            CreatedAt = localProfile?.CreatedAt,
            UpdatedAt = localProfile?.UpdatedAt
        };
    }

    private static SiteMapPoint ToMapPoint(SiteMergedView site)
    {
        return new SiteMapPoint
        {
            DeviceCode = site.DeviceCode,
            DeviceName = site.DeviceName,
            DisplayName = site.DisplayName,
            Alias = site.Alias,
            AddressText = site.AddressText,
            MaintenanceUnit = site.MaintenanceUnit,
            MaintainerName = site.MaintainerName,
            MaintainerPhone = site.MaintainerPhone,
            IsMonitored = site.IsMonitored,
            CoordinatePayload = new MapCoordinatePayload
            {
                PlatformRawLongitude = site.PlatformRawLongitude,
                PlatformRawLatitude = site.PlatformRawLatitude,
                RawCoordinateType = site.PlatformRawCoordinateType,
                ManualLongitude = site.ManualLongitude,
                ManualLatitude = site.ManualLatitude,
                CoordinateSource = site.CoordinateSource,
                CoordinateSourceText = site.CoordinateSourceText
            },
            DemoOnlineState = site.DemoOnlineState,
            DemoStatus = site.DemoStatus,
            DemoDispatchStatus = site.DemoDispatchStatus,
            VisualState = site.VisualState,
            StatusText = site.StatusText,
            RuntimeSummaryText = ResolveRuntimeSummary(site),
            LastInspectionAt = site.LastInspectionAt,
            LastSnapshotPath = site.LastSnapshotPath,
            LastSnapshotAt = site.LastSnapshotAt,
            RuntimeFaultCode = site.RuntimeFaultCode
        };
    }

    private static SiteAlertDigest ToAlertDigest(SiteMergedView site)
    {
        return new SiteAlertDigest
        {
            PointId = site.DeviceCode,
            PointDisplayName = site.DisplayName,
            IssueLabel = ResolveAlertLabel(site),
            OccurredAtText = (site.LastInspectionAt ?? site.RuntimeUpdatedAt ?? site.UpdatedAt)?.ToLocalTime().ToString("HH:mm") ?? "--:--",
            RuntimeSummary = ResolveRuntimeSummary(site),
            SnapshotPath = site.LastSnapshotPath
        };
    }

    private static bool MatchesFilter(SiteMergedView site, SiteDashboardFilter filter)
    {
        return filter switch
        {
            SiteDashboardFilter.Fault => IsAttentionSite(site),
            SiteDashboardFilter.Monitored => site.IsMonitored,
            SiteDashboardFilter.Dispatched => site.DemoDispatchStatus == DispatchDemoStatus.Dispatched,
            _ => true
        };
    }

    private static bool MatchesSearch(SiteMergedView site, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();

        return site.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceCode.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (site.AddressText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintenanceUnit?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintainerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.RuntimeSummary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAttentionSite(SiteMergedView site)
    {
        if (!site.IsMonitored)
        {
            return false;
        }

        if (site.RuntimeFaultCode != RuntimeFaultCode.None)
        {
            return true;
        }

        if (site.LastInspectionRunState == InspectionRunState.Failed)
        {
            return true;
        }

        return site.DemoOnlineState == DemoOnlineState.Offline
            || site.DemoDispatchStatus != DispatchDemoStatus.None
            || site.DemoStatus != PointDemoStatus.Normal;
    }

    private static int GetAlertPriority(SiteMergedView site)
    {
        return site.RuntimeFaultCode switch
        {
            RuntimeFaultCode.Offline => 5,
            RuntimeFaultCode.InspectionExecutionFailed => 4,
            RuntimeFaultCode.PreviewResolveFailed => 3,
            RuntimeFaultCode.SnapshotFailed => 2,
            _ => site.LastInspectionRunState == InspectionRunState.Failed ? 1 : 0
        };
    }

    private static DateTimeOffset GetAlertTimestamp(SiteMergedView site)
    {
        return site.LastInspectionAt
            ?? site.RuntimeUpdatedAt
            ?? site.UpdatedAt
            ?? DateTimeOffset.MinValue;
    }

    private static string ResolveDisplayName(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        return string.IsNullOrWhiteSpace(localProfile?.Alias)
            ? platformSite.DeviceName
            : localProfile.Alias.Trim();
    }

    private static CoordinateSource ResolveCoordinateSource(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile)
    {
        if (platformSite.RawLongitude.HasValue && platformSite.RawLatitude.HasValue)
        {
            return CoordinateSource.PlatformRaw;
        }

        if (localProfile?.ManualLongitude.HasValue == true && localProfile.ManualLatitude.HasValue)
        {
            return CoordinateSource.ManualOverride;
        }

        return CoordinateSource.None;
    }

    private static string ResolveCoordinateSourceText(CoordinateSource coordinateSource)
    {
        return coordinateSource switch
        {
            CoordinateSource.PlatformRaw => "平台原始",
            CoordinateSource.ManualOverride => "本地手工",
            _ => "暂无坐标"
        };
    }

    private static SiteVisualState ResolveVisualState(
        PlatformSiteSnapshot platformSite,
        SiteRuntimeState? runtimeState,
        bool isMonitored)
    {
        if (!isMonitored)
        {
            return SiteVisualState.Unmonitored;
        }

        if (runtimeState is not null)
        {
            if (runtimeState.LastFaultCode == RuntimeFaultCode.Offline)
            {
                return SiteVisualState.Offline;
            }

            if (runtimeState.LastInspectionRunState == InspectionRunState.Failed
                || runtimeState.LastFaultCode == RuntimeFaultCode.InspectionExecutionFailed)
            {
                return SiteVisualState.Fault;
            }

            if (runtimeState.LastFaultCode is RuntimeFaultCode.PreviewResolveFailed or RuntimeFaultCode.SnapshotFailed)
            {
                return runtimeState.ConsecutiveFailureCount >= 3
                    ? SiteVisualState.Fault
                    : SiteVisualState.Warning;
            }
        }

        if (platformSite.DemoOnlineState == DemoOnlineState.Offline)
        {
            return SiteVisualState.Offline;
        }

        return platformSite.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => SiteVisualState.Dispatched,
            DispatchDemoStatus.Cooling => SiteVisualState.Cooling,
            _ => platformSite.DemoStatus switch
            {
                PointDemoStatus.Fault => SiteVisualState.Fault,
                PointDemoStatus.Warning => SiteVisualState.Warning,
                PointDemoStatus.Idle => SiteVisualState.Idle,
                _ => SiteVisualState.Normal
            }
        };
    }

    private static string ResolveStatusText(
        PlatformSiteSnapshot platformSite,
        SiteRuntimeState? runtimeState,
        bool isMonitored)
    {
        if (!isMonitored)
        {
            return "未纳入监测";
        }

        if (runtimeState is not null)
        {
            return runtimeState.LastFaultCode switch
            {
                RuntimeFaultCode.Offline => "设备离线",
                RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
                RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
                RuntimeFaultCode.InspectionExecutionFailed => "巡检执行失败",
                _ => runtimeState.LastInspectionRunState switch
                {
                    InspectionRunState.Succeeded => "巡检正常",
                    InspectionRunState.SucceededWithFault => "巡检发现异常",
                    InspectionRunState.Failed => "巡检失败",
                    _ => "待巡检"
                }
            };
        }

        if (platformSite.DemoOnlineState == DemoOnlineState.Offline)
        {
            return "设备离线";
        }

        return platformSite.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => "已派单",
            DispatchDemoStatus.Cooling => "冷却观察中",
            _ => platformSite.DemoStatus switch
            {
                PointDemoStatus.Fault => "设备故障",
                PointDemoStatus.Warning => "预警关注",
                PointDemoStatus.Idle => "长时空闲",
                _ => "正常"
            }
        };
    }

    private static string ResolveAlertLabel(SiteMergedView site)
    {
        return site.RuntimeFaultCode switch
        {
            RuntimeFaultCode.Offline => "设备离线",
            RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            RuntimeFaultCode.InspectionExecutionFailed => "巡检执行失败",
            _ => site.StatusText
        };
    }

    private static string ResolveRuntimeSummary(SiteMergedView site)
    {
        if (!string.IsNullOrWhiteSpace(site.RuntimeSummary))
        {
            return site.RuntimeSummary!;
        }

        if (site.LastInspectionAt.HasValue)
        {
            return site.LastInspectionRunState switch
            {
                InspectionRunState.Succeeded => "最近巡检完成，当前未发现异常。",
                InspectionRunState.SucceededWithFault => "最近巡检完成，运行态存在异常。",
                InspectionRunState.Failed => "最近巡检失败，请查看本地日志。",
                _ => "最近巡检已记录。"
            };
        }

        return site.IsMonitored ? "尚未产生运行态记录。" : "当前点位未纳入静默巡检。";
    }

    private sealed record SiteMergeBundle(
        IReadOnlyList<SiteMergedView> Sites,
        PlatformConnectionState ConnectionState);
}
