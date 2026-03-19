using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SiteMergedView
{
    public required string DeviceCode { get; init; }

    public required string DeviceName { get; init; }

    public required string DisplayName { get; init; }

    public string? Alias { get; init; }

    public string? Remark { get; init; }

    public required bool IsMonitored { get; init; }

    public double? PlatformLongitude { get; init; }

    public double? PlatformLatitude { get; init; }

    public double? ManualLongitude { get; init; }

    public double? ManualLatitude { get; init; }

    public double? Longitude { get; init; }

    public double? Latitude { get; init; }

    public required bool HasMapPoint { get; init; }

    public required string CoordinateSourceText { get; init; }

    public string? AddressText { get; init; }

    public string? ProductAccessNumber { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public required DemoOnlineState DemoOnlineState { get; init; }

    public required PointDemoStatus DemoStatus { get; init; }

    public required DispatchDemoStatus DemoDispatchStatus { get; init; }

    public required SiteVisualState VisualState { get; init; }

    public required string StatusText { get; init; }

    public required bool HasLocalProfile { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
