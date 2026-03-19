using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteMapQueryService : ISiteMapQueryService
{
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformConnectionStateProvider platformConnectionStateProvider;
    private readonly IPlatformSiteProvider platformSiteProvider;

    public SiteMapQueryService(
        IPlatformSiteProvider platformSiteProvider,
        IPlatformConnectionStateProvider platformConnectionStateProvider,
        ISiteLocalProfileRepository localProfileRepository)
    {
        this.platformSiteProvider = platformSiteProvider;
        this.platformConnectionStateProvider = platformConnectionStateProvider;
        this.localProfileRepository = localProfileRepository;
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
        var localProfileMap = localProfiles.ToDictionary(
            profile => profile.DeviceCode,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);

        var mergedSites = platformSites
            .Select(platformSite =>
            {
                localProfileMap.TryGetValue(platformSite.DeviceCode, out var localProfile);
                return Merge(platformSite, localProfile, platformConnectionState);
            })
            .ToList();

        return new SiteMergeBundle(mergedSites, platformConnectionState);
    }

    private static SiteMergedView Merge(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
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
            AddressText = localProfile?.AddressText,
            ProductAccessNumber = localProfile?.ProductAccessNumber,
            MaintenanceUnit = localProfile?.MaintenanceUnit,
            MaintainerName = localProfile?.MaintainerName,
            MaintainerPhone = localProfile?.MaintainerPhone,
            DemoOnlineState = platformSite.DemoOnlineState,
            DemoStatus = platformSite.DemoStatus,
            DemoDispatchStatus = platformSite.DemoDispatchStatus,
            VisualState = ResolveVisualState(platformSite, isMonitored),
            StatusText = ResolveStatusText(platformSite, isMonitored),
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
            StatusText = site.StatusText
        };
    }

    private static SiteAlertDigest ToAlertDigest(SiteMergedView site)
    {
        return new SiteAlertDigest
        {
            PointId = site.DeviceCode,
            PointDisplayName = site.DisplayName,
            IssueLabel = ResolveAlertLabel(site),
            OccurredAtText = site.UpdatedAt?.ToLocalTime().ToString("HH:mm") ?? "刚刚"
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
            || (site.MaintainerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAttentionSite(SiteMergedView site)
    {
        if (!site.IsMonitored)
        {
            return false;
        }

        return site.DemoOnlineState == DemoOnlineState.Offline
            || site.DemoDispatchStatus != DispatchDemoStatus.None
            || site.DemoStatus != PointDemoStatus.Normal;
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

    private static SiteVisualState ResolveVisualState(PlatformSiteSnapshot platformSite, bool isMonitored)
    {
        if (!isMonitored)
        {
            return SiteVisualState.Unmonitored;
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

    private static string ResolveStatusText(PlatformSiteSnapshot platformSite, bool isMonitored)
    {
        if (!isMonitored)
        {
            return "未纳入监测";
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
        if (site.DemoOnlineState == DemoOnlineState.Offline)
        {
            return "设备离线";
        }

        return site.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => "已派单待到场",
            DispatchDemoStatus.Cooling => "冷却观察中",
            _ => site.DemoStatus switch
            {
                PointDemoStatus.Fault => "设备故障",
                PointDemoStatus.Warning => "预警关注",
                PointDemoStatus.Idle => "长时空闲",
                _ => "状态关注"
            }
        };
    }

    private sealed record SiteMergeBundle(
        IReadOnlyList<SiteMergedView> Sites,
        PlatformConnectionState ConnectionState);
}
