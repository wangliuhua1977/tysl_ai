using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record UnmappedPointDigest
{
    public required string DeviceCode { get; init; }

    public required string DisplayName { get; init; }

    public required string DeviceName { get; init; }

    public required bool IsMonitored { get; init; }

    public required UnmappedReason UnmappedReason { get; init; }

    public required string UnmappedReasonText { get; init; }

    public required string CoordinateSourceText { get; init; }

    public required string GovernanceHintText { get; init; }

    public string? PlatformCoordinateText { get; init; }

    public required string PlatformCoordinateTypeText { get; init; }

    public string? ManualCoordinateText { get; init; }
}
