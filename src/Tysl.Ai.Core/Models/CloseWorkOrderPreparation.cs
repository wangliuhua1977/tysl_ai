namespace Tysl.Ai.Core.Models;

public sealed record CloseWorkOrderPreparation
{
    public required long WorkOrderId { get; init; }

    public required string DeviceCode { get; init; }

    public required string SiteDisplayName { get; init; }

    public string? ProductAccessNumber { get; init; }

    public required string CurrentFaultReason { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public required string RecoveryStatusText { get; init; }

    public required string RecoveredAtText { get; init; }

    public string? LastNotificationSummary { get; init; }
}
