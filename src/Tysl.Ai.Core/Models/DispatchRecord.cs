using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record DispatchRecord
{
    public long Id { get; init; }

    public required string DeviceCode { get; init; }

    public required string FaultCode { get; init; }

    public required string FaultSummary { get; init; }

    public required DispatchStatus DispatchStatus { get; init; }

    public required DispatchMode DispatchMode { get; init; }

    public required DateTimeOffset TriggeredAt { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    public DateTimeOffset? CoolingUntil { get; init; }

    public DateTimeOffset? RecoveredAt { get; init; }

    public required RecoveryMode RecoveryMode { get; init; }

    public required RecoveryStatus RecoveryStatus { get; init; }

    public string? RecoverySummary { get; init; }

    public string? MessageDigest { get; init; }

    public string? SnapshotPath { get; init; }

    public DateTimeOffset? LastInspectionAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsRecovered =>
        RecoveredAt.HasValue
        || RecoveryStatus is RecoveryStatus.Recovered or RecoveryStatus.NotificationFailed;
}
