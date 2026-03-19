using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SiteMapPoint
{
    public required string DeviceCode { get; init; }

    public required string DeviceName { get; init; }

    public required string DisplayName { get; init; }

    public string? Alias { get; init; }

    public string? AddressText { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public required bool IsMonitored { get; init; }

    public required double Longitude { get; init; }

    public required double Latitude { get; init; }

    public required string CoordinateSourceText { get; init; }

    public required DemoOnlineState DemoOnlineState { get; init; }

    public required PointDemoStatus DemoStatus { get; init; }

    public required DispatchDemoStatus DemoDispatchStatus { get; init; }

    public required SiteVisualState VisualState { get; init; }

    public required string StatusText { get; init; }

    public required double MapX { get; init; }

    public required double MapY { get; init; }
}
