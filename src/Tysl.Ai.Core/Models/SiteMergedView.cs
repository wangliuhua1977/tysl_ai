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

    public required bool IsIgnored { get; init; }

    public DateTimeOffset? IgnoredAt { get; init; }

    public string? IgnoredReason { get; init; }

    public double? PlatformRawLongitude { get; init; }

    public double? PlatformRawLatitude { get; init; }

    public required string PlatformRawCoordinateType { get; init; }

    public required bool IsPlatformCoordinateEnrichedFromDetail { get; init; }

    public double? ManualLongitude { get; init; }

    public double? ManualLatitude { get; init; }

    public double? DisplayLongitude { get; init; }

    public double? DisplayLatitude { get; init; }

    public required CoordinateDisplayStatus CoordinateDisplayStatus { get; init; }

    public required bool HasMapPoint { get; init; }

    public required bool HasDisplayCoordinate { get; init; }

    public required UnmappedReason UnmappedReason { get; init; }

    public required string UnmappedReasonText { get; init; }

    public required string CoordinateDisplayStatusText { get; init; }

    public required string CoordinateGovernanceHintText { get; init; }

    public required CoordinateSource CoordinateSource { get; init; }

    public required string CoordinateSourceText { get; init; }

    public required string PlatformStatusSummary { get; init; }

    public required bool HasRuntimeState { get; init; }

    public DateTimeOffset? LastInspectionAt { get; init; }

    public string? LastProductState { get; init; }

    public required PreviewResolveState LastPreviewResolveState { get; init; }

    public string? LastSnapshotPath { get; init; }

    public DateTimeOffset? LastSnapshotAt { get; init; }

    public required RuntimeFaultCode RuntimeFaultCode { get; init; }

    public string? RuntimeSummary { get; init; }

    public required int ConsecutiveFailureCount { get; init; }

    public required InspectionRunState LastInspectionRunState { get; init; }

    public DateTimeOffset? RuntimeUpdatedAt { get; init; }

    public long? DispatchRecordId { get; init; }

    public required bool HasDispatchRecord { get; init; }

    public string? DispatchFaultCode { get; init; }

    public string? DispatchFaultSummary { get; init; }

    public required DispatchStatus DispatchStatus { get; init; }

    public required DispatchMode DispatchMode { get; init; }

    public DateTimeOffset? DispatchTriggeredAt { get; init; }

    public DateTimeOffset? DispatchSentAt { get; init; }

    public DateTimeOffset? CoolingUntil { get; init; }

    public required RecoveryMode RecoveryMode { get; init; }

    public required RecoveryStatus RecoveryStatus { get; init; }

    public DateTimeOffset? RecoveredAt { get; init; }

    public RecoverySource? RecoverySource { get; init; }

    public DateTimeOffset? ClosedArchivedAt { get; init; }

    public string? RecoverySummary { get; init; }

    public string? ClosingRemark { get; init; }

    public string? DispatchMessageDigest { get; init; }

    public required bool IsDispatchCooling { get; init; }

    public required bool CanConfirmRecovery { get; init; }

    public required string DispatchStatusText { get; init; }

    public required string RecoveryStatusText { get; init; }

    public string? AddressText { get; init; }

    public string? ProductAccessNumber { get; init; }

    public string? ProductStatus { get; init; }

    public decimal? ArrearsAmount { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public string? AreaName { get; init; }

    public string? DefaultDispatchRemark { get; init; }

    public required bool IsAutoDispatchEnabled { get; init; }

    public required bool AllowRecoveryAutoArchive { get; init; }

    public required RecoveryConfirmationMode RecoveryConfirmationMode { get; init; }

    public DispatchSource? WorkOrderDispatchSource { get; init; }

    public DispatchWorkOrderStatus? WorkOrderStatus { get; init; }

    public required DemoOnlineState DemoOnlineState { get; init; }

    public required PointDemoStatus DemoStatus { get; init; }

    public required DispatchDemoStatus DemoDispatchStatus { get; init; }

    public required SiteVisualState VisualState { get; init; }

    public required string StatusText { get; init; }

    public required bool HasLocalProfile { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
