using Tysl.Ai.Core.Abstractions;
using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed class SiteProfile : IEntity
{
    public Guid Id { get; set; }

    public string DeviceCode { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string? Alias { get; set; }

    public string? Remark { get; set; }

    public bool IsMonitored { get; set; }

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    public string? AddressText { get; set; }

    public string? ProductAccessNumber { get; set; }

    public string? MaintenanceUnit { get; set; }

    public string? MaintainerName { get; set; }

    public string? MaintainerPhone { get; set; }

    public PointDemoStatus DemoStatus { get; set; }

    public DispatchDemoStatus DemoDispatchStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
