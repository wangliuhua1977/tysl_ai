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
    private readonly ISiteProfileRepository repository;

    public SiteMapQueryService(ISiteProfileRepository repository)
    {
        this.repository = repository;
    }

    public async Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var allSites = await repository.ListAsync(cancellationToken);
        var positions = BuildMapPositions(allSites);
        var visibleSites = allSites
            .Where(site => MatchesFilter(site, filter))
            .Where(site => MatchesSearch(site, searchText))
            .ToList();

        return new SiteDashboardSnapshot
        {
            PointCount = allSites.Count,
            MonitoredCount = allSites.Count(site => site.IsMonitored),
            FaultCount = allSites.Count(IsAttentionSite),
            DispatchedCount = allSites.Count(site => site.DemoDispatchStatus == DispatchDemoStatus.Dispatched),
            LastRefreshedAt = DateTimeOffset.Now,
            VisiblePoints = visibleSites
                .Select(site => ToMapPoint(site, positions[site.Id]))
                .ToList(),
            VisibleAlerts = visibleSites
                .Where(IsAttentionSite)
                .OrderByDescending(site => site.UpdatedAt)
                .Select(ToAlertDigest)
                .ToList()
        };
    }

    public async Task<SiteDetailSnapshot?> GetSiteDetailAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var site = await repository.GetByIdAsync(siteId, cancellationToken);
        return site is null ? null : ToDetailSnapshot(site);
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

    private static Dictionary<Guid, (double X, double Y)> BuildMapPositions(IReadOnlyList<SiteProfile> allSites)
    {
        if (allSites.Count == 0)
        {
            return [];
        }

        var minLongitude = allSites.Min(site => site.Longitude);
        var maxLongitude = allSites.Max(site => site.Longitude);
        var minLatitude = allSites.Min(site => site.Latitude);
        var maxLatitude = allSites.Max(site => site.Latitude);

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

        return allSites.ToDictionary(
            site => site.Id,
            site =>
            {
                var x = MapHorizontalPadding + (((site.Longitude - minLongitude) / (maxLongitude - minLongitude)) * usableWidth);
                var y = MapVerticalPadding + (((maxLatitude - site.Latitude) / (maxLatitude - minLatitude)) * usableHeight);
                return (Math.Round(x, 2), Math.Round(y, 2));
            });
    }

    private static SiteMapPoint ToMapPoint(SiteProfile site, (double X, double Y) position)
    {
        return new SiteMapPoint
        {
            Id = site.Id,
            DeviceCode = site.DeviceCode,
            DeviceName = site.DeviceName,
            DisplayName = GetDisplayName(site),
            Alias = site.Alias,
            AddressText = site.AddressText,
            MaintenanceUnit = site.MaintenanceUnit,
            MaintainerName = site.MaintainerName,
            MaintainerPhone = site.MaintainerPhone,
            IsMonitored = site.IsMonitored,
            Longitude = site.Longitude,
            Latitude = site.Latitude,
            DemoStatus = site.DemoStatus,
            DemoDispatchStatus = site.DemoDispatchStatus,
            VisualState = ResolveVisualState(site),
            StatusText = ResolveStatusText(site),
            MapX = position.X,
            MapY = position.Y
        };
    }

    private static SiteDetailSnapshot ToDetailSnapshot(SiteProfile site)
    {
        return new SiteDetailSnapshot
        {
            Id = site.Id,
            DeviceCode = site.DeviceCode,
            DeviceName = site.DeviceName,
            DisplayName = GetDisplayName(site),
            Alias = site.Alias,
            Remark = site.Remark,
            IsMonitored = site.IsMonitored,
            Longitude = site.Longitude,
            Latitude = site.Latitude,
            AddressText = site.AddressText,
            ProductAccessNumber = site.ProductAccessNumber,
            MaintenanceUnit = site.MaintenanceUnit,
            MaintainerName = site.MaintainerName,
            MaintainerPhone = site.MaintainerPhone,
            DemoStatus = site.DemoStatus,
            DemoDispatchStatus = site.DemoDispatchStatus,
            VisualState = ResolveVisualState(site),
            StatusText = ResolveStatusText(site),
            CreatedAt = site.CreatedAt,
            UpdatedAt = site.UpdatedAt
        };
    }

    private static SiteAlertDigest ToAlertDigest(SiteProfile site)
    {
        return new SiteAlertDigest
        {
            PointId = site.Id,
            PointDisplayName = GetDisplayName(site),
            IssueLabel = ResolveAlertLabel(site),
            OccurredAtText = site.UpdatedAt.ToLocalTime().ToString("HH:mm")
        };
    }

    private static bool MatchesFilter(SiteProfile site, SiteDashboardFilter filter)
    {
        return filter switch
        {
            SiteDashboardFilter.Fault => IsAttentionSite(site),
            SiteDashboardFilter.Monitored => site.IsMonitored,
            SiteDashboardFilter.Dispatched => site.DemoDispatchStatus == DispatchDemoStatus.Dispatched,
            _ => true
        };
    }

    private static bool MatchesSearch(SiteProfile site, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();

        return GetDisplayName(site).Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || site.DeviceCode.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (site.AddressText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintenanceUnit?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (site.MaintainerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAttentionSite(SiteProfile site)
    {
        return site.DemoDispatchStatus != DispatchDemoStatus.None || site.DemoStatus != PointDemoStatus.Normal;
    }

    private static string GetDisplayName(SiteProfile site)
    {
        return string.IsNullOrWhiteSpace(site.Alias) ? site.DeviceName : site.Alias.Trim();
    }

    private static SiteVisualState ResolveVisualState(SiteProfile site)
    {
        if (!site.IsMonitored)
        {
            return SiteVisualState.Unmonitored;
        }

        return site.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => SiteVisualState.Dispatched,
            DispatchDemoStatus.Cooling => SiteVisualState.Cooling,
            _ => site.DemoStatus switch
            {
                PointDemoStatus.Fault => SiteVisualState.Fault,
                PointDemoStatus.Warning => SiteVisualState.Warning,
                PointDemoStatus.Idle => SiteVisualState.Idle,
                _ => SiteVisualState.Normal
            }
        };
    }

    private static string ResolveStatusText(SiteProfile site)
    {
        if (!site.IsMonitored)
        {
            return "未监测";
        }

        return site.DemoDispatchStatus switch
        {
            DispatchDemoStatus.Dispatched => "已派单",
            DispatchDemoStatus.Cooling => "冷却中",
            _ => site.DemoStatus switch
            {
                PointDemoStatus.Fault => "故障",
                PointDemoStatus.Warning => "预警",
                PointDemoStatus.Idle => "空闲",
                _ => "正常"
            }
        };
    }

    private static string ResolveAlertLabel(SiteProfile site)
    {
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
