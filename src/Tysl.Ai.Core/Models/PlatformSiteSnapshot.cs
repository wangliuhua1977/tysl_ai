using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record PlatformSiteSnapshot
{
    public required string DeviceCode { get; init; }

    public required string DeviceName { get; init; }

    public double? RawLongitude { get; init; }

    public double? RawLatitude { get; init; }

    public required string RawCoordinateType { get; init; }

    public required bool IsCoordinateEnrichedFromDetail { get; init; }

    public required DemoOnlineState DemoOnlineState { get; init; }

    public required PointDemoStatus DemoStatus { get; init; }

    public required DispatchDemoStatus DemoDispatchStatus { get; init; }
}
