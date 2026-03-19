namespace Tysl.Ai.Core.Models;

public sealed record SiteDashboardSnapshot
{
    public required int PointCount { get; init; }

    public required int MonitoredCount { get; init; }

    public required int FaultCount { get; init; }

    public required int DispatchedCount { get; init; }

    public required DateTimeOffset LastRefreshedAt { get; init; }

    public required IReadOnlyList<SiteMapPoint> VisiblePoints { get; init; }

    public required IReadOnlyList<SiteAlertDigest> VisibleAlerts { get; init; }
}
