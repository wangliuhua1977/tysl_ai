using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed class SiteLocalProfile
{
    public string DeviceCode { get; set; } = string.Empty;

    public string? Alias { get; set; }

    public string? Remark { get; set; }

    public bool IsMonitored { get; set; }

    public bool IsIgnored { get; set; }

    public DateTimeOffset? IgnoredAt { get; set; }

    public string? IgnoredReason { get; set; }

    public double? ManualLongitude { get; set; }

    public double? ManualLatitude { get; set; }

    public string? AddressText { get; set; }

    public string? ProductAccessNumber { get; set; }

    public string? MaintenanceUnit { get; set; }

    public string? MaintainerName { get; set; }

    public string? MaintainerPhone { get; set; }

    public string? AreaName { get; set; }

    public string? DefaultDispatchRemark { get; set; }

    public bool IsAutoDispatchEnabled { get; set; }

    public bool AllowRecoveryAutoArchive { get; set; }

    public RecoveryConfirmationMode RecoveryConfirmationMode { get; set; } = RecoveryConfirmationMode.ManualOnly;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
