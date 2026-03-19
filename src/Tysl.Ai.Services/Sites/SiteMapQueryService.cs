using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SiteMapQueryService : ISiteMapQueryService
{
    private const double MapCanvasWidth = 1000D;
    private const double MapCanvasHeight = 620D;
    private const double MapHorizontalPadding = 78D;
    private const double MapVerticalPadding = 68D;
    private const double PickMinLongitude = 120.53D;
    private const double PickMaxLongitude = 120.69D;
    private const double PickMinLatitude = 29.97D;
    private const double PickMaxLatitude = 30.05D;
    private readonly ISiteLocalProfileRepository localProfileRepository;
    private readonly IPlatformSiteProvider platformSiteProvider;

    public SiteMapQueryService(
        IPlatformSiteProvider platformSiteProvider,
        ISiteLocalProfileRepository localProfileRepository)
    {
        this.platformSiteProvider = platformSiteProvider;
        this.localProfileRepository = localProfileRepository;
    }

    public async Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var mergedSites = await BuildMergedSitesAsync(cancellationToken);
        var visibleSites = mergedSites
            .Where(site => MatchesFilter(site, filter))
            .Where(site => MatchesSearch(site, searchText))
            .ToList();

        var visibleMapSites = visibleSites
            .Where(site => site.HasMapPoint)
            .ToList();

        var positions = BuildMapPositions(visibleMapSites);

        return new SiteDashboardSnapshot
        {
            PointCount = mergedSites.Count,
            MonitoredCount = mergedSites.Count(site => site.IsMonitored),
            FaultCount = mergedSites.Count(IsAttentionSite),
            DispatchedCount = mergedSites.Count(site => site.DemoDispatchStatus == DispatchDemoStatus.Dispatched),
            LastRefreshedAt = DateTimeOffset.Now,
            VisiblePoints = visibleMapSites
                .Select(site => ToMapPoint(site, positions[site.DeviceCode]))
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
        var mergedSites = await BuildMergedSitesAsync(cancellationToken);
        return mergedSites.FirstOrDefault(site => site.DeviceCode.Equals(
            normalizedDeviceCode,
            StringComparison.OrdinalIgnoreCase));
    }

    public DemoCoordinate CreateDemoCoordinate(double relativeX, double relativeY)
    {
        var clampedX = Math.Clamp(relativeX, 0D, 1D);
        var clampedY = Math.Clamp(relativeY, 0D, 1D);

        var longitude = PickMinLongitude + ((PickMaxLongitude - PickMinLongitude) * clampedX);
        var latitude = PickMaxLatitude - ((PickMaxLatitude - PickMinLatitude) * clampedY);

        return new DemoCoordinate
        {
            Longitude = Math.Round(longitude, 6),
            Latitude = Math.Round(latitude, 6)
        };
    }

    private async Task<IReadOnlyList<SiteMergedView>> BuildMergedSitesAsync(CancellationToken cancellationToken)
    {
        var platformSites = await platformSiteProvider.ListAsync(cancellationToken);
        var localProfiles = await localProfileRepository.ListAsync(cancellationToken);
        var localProfileMap = localProfiles.ToDictionary(
            profile => profile.DeviceCode,
            profile => profile,
            StringComparer.OrdinalIgnoreCase);

        return platformSites
            .Select(platformSite =>
            {
                localProfileMap.TryGetValue(platformSite.DeviceCode, out var localProfile);
                return Merge(platformSite, localProfile);
            })
            .ToList();
    }

    private static Dictionary<string, (double X, double Y)> BuildMapPositions(
        IReadOnlyList<SiteMergedView> mapSites)
    {
        if (mapSites.Count == 0)
        {
            return [];
        }

        var minLongitude = mapSites.Min(site => site.Longitude ?? 0D);
        var maxLongitude = mapSites.Max(site => site.Longitude ?? 0D);
        var minLatitude = mapSites.Min(site => site.Latitude ?? 0D);
        var maxLatitude = mapSites.Max(site => site.Latitude ?? 0D);

        if (Math.Abs(maxLongitude - minLongitude) < 0.0001D)
        {
            maxLongitude += 0.01D;
            minLongitude -= 0.01D;
        }

        if (Math.Abs(maxLatitude - minLatitude) < 0.0001D)
        {
            maxLatitude += 0.01D;
            minLatitude -= 0.01D;
        }

        var usableWidth = MapCanvasWidth - (MapHorizontalPadding * 2);
        var usableHeight = MapCanvasHeight - (MapVerticalPadding * 2);

        return mapSites.ToDictionary(
            site => site.DeviceCode,
            site =>
            {
                var longitude = site.Longitude ?? 0D;
                var latitude = site.Latitude ?? 0D;
                var x = MapHorizontalPadding + (((longitude - minLongitude) / (maxLongitude - minLongitude)) * usableWidth);
                var y = MapVerticalPadding + (((maxLatitude - latitude) / (maxLatitude - minLatitude)) * usableHeight);
                return (Math.Round(x, 2), Math.Round(y, 2));
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static SiteMergedView Merge(PlatformSiteSnapshot platformSite, SiteLocalProfile? localProfile)
    {
        var longitude = platformSite.PlatformLongitude ?? localProfile?.ManualLongitude;
        var latitude = platformSite.PlatformLatitude ?? localProfile?.ManualLatitude;
        var hasMapPoint = longitude.HasValue && latitude.HasValue;
        var isMonitored = localProfile?.IsMonitored ?? true;

        return new SiteMergedView
        {
            DeviceCode = platformSite.DeviceCode,
            DeviceName = platformSite.DeviceName,
            DisplayName = ResolveDisplayName(platformSite, localProfile),
            Alias = localProfile?.Alias,
            Remark = localProfile?.Remark,
            IsMonitored = isMonitored,
            PlatformLongitude = platformSite.PlatformLongitude,
            PlatformLatitude = platformSite.PlatformLatitude,
            ManualLongitude = localProfile?.ManualLongitude,
            ManualLatitude = localProfile?.ManualLatitude,
            Longitude = longitude,
            Latitude = latitude,
            HasMapPoint = hasMapPoint,
            CoordinateSourceText = ResolveCoordinateSourceText(platformSite, localProfile, hasMapPoint),
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

    private static SiteMapPoint ToMapPoint(SiteMergedView site, (double X, double Y) position)
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
            Longitude = site.Longitude ?? 0D,
            Latitude = site.Latitude ?? 0D,
            CoordinateSourceText = site.CoordinateSourceText,
            DemoOnlineState = site.DemoOnlineState,
            DemoStatus = site.DemoStatus,
            DemoDispatchStatus = site.DemoDispatchStatus,
            VisualState = site.VisualState,
            StatusText = site.StatusText,
            MapX = position.X,
            MapY = position.Y
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
            : localProfile.Alias!.Trim();
    }

    private static string ResolveCoordinateSourceText(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        bool hasMapPoint)
    {
        if (!hasMapPoint)
        {
            return "暂无可用坐标";
        }

        if (platformSite.PlatformLongitude.HasValue && platformSite.PlatformLatitude.HasValue)
        {
            return "平台坐标";
        }

        if (localProfile?.ManualLongitude.HasValue == true && localProfile.ManualLatitude.HasValue)
        {
            return "手工补录坐标";
        }

        return "暂无可用坐标";
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
}
