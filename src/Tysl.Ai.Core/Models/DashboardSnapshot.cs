namespace Tysl.Ai.Core.Models;

public sealed record DashboardSnapshot
{
    public required int PointCount { get; init; }

    public required int OnlineCount { get; init; }

    public required int AlertCount { get; init; }

    public required int DispatchedCount { get; init; }

    public required DateTimeOffset LastRefreshedAt { get; init; }

    public required IReadOnlyList<MonitoringPoint> Points { get; init; }

    public required IReadOnlyList<AlertDigest> Alerts { get; init; }
}
