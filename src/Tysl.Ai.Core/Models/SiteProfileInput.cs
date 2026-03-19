using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SiteProfileInput
{
    public Guid? Id { get; init; }

    public required string DeviceCode { get; init; }

    public required string DeviceName { get; init; }

    public string? Alias { get; init; }

    public string? Remark { get; init; }

    public required bool IsMonitored { get; init; }

    public required double Longitude { get; init; }

    public required double Latitude { get; init; }

    public string? AddressText { get; init; }

    public string? ProductAccessNumber { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public required PointDemoStatus DemoStatus { get; init; }

    public required DispatchDemoStatus DemoDispatchStatus { get; init; }
}
