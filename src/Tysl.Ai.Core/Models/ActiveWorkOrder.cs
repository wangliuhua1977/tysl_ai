using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record ActiveWorkOrder
{
    public long WorkOrderId { get; init; }

    public required string DeviceCode { get; init; }

    public required string SiteNameSnapshot { get; init; }

    public string? SiteAliasSnapshot { get; init; }

    public string? ProductAccessNumberSnapshot { get; init; }

    public required string CurrentFaultCode { get; init; }

    public required string CurrentFaultReason { get; init; }

    public required DispatchSource DispatchSource { get; init; }

    public required DispatchWorkOrderStatus Status { get; init; }

    public DateTimeOffset? FirstDispatchedAt { get; init; }

    public required DateTimeOffset LatestExceptionAt { get; init; }

    public DateTimeOffset? LatestNotificationAt { get; init; }

    public string? MaintenanceUnitSnapshot { get; init; }

    public string? MaintainerNameSnapshot { get; init; }

    public string? MaintainerPhoneSnapshot { get; init; }

    public string? DispatchRemarkSnapshot { get; init; }

    public required RecoveryConfirmationMode RecoveryConfirmationModeSnapshot { get; init; }

    public required bool AllowRecoveryAutoArchiveSnapshot { get; init; }

    public string? LastNotificationSummary { get; init; }

    public RecoverySource? RecoverySource { get; init; }

    public string? RecoverySummary { get; init; }

    public string? ClosingRemark { get; init; }

    public string? ProductStatusSnapshot { get; init; }

    public decimal? ArrearsAmountSnapshot { get; init; }

    public DateTimeOffset? RecoveredAt { get; init; }

    public DateTimeOffset? RecoveryConfirmedAt { get; init; }

    public DateTimeOffset? ClosedArchivedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsOpen => Status != DispatchWorkOrderStatus.ClosedArchived;
}
