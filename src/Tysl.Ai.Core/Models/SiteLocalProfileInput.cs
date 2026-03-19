namespace Tysl.Ai.Core.Models;

public sealed record SiteLocalProfileInput
{
    public required string DeviceCode { get; init; }

    public string? Alias { get; init; }

    public string? Remark { get; init; }

    public required bool IsMonitored { get; init; }

    public double? ManualLongitude { get; init; }

    public double? ManualLatitude { get; init; }

    public string? AddressText { get; init; }

    public string? ProductAccessNumber { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }
}
