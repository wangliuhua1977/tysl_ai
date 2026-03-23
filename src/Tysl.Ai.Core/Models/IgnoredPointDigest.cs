namespace Tysl.Ai.Core.Models;

public sealed record IgnoredPointDigest
{
    public required string DeviceCode { get; init; }

    public required string DisplayName { get; init; }

    public required string DeviceName { get; init; }

    public required bool IsMonitored { get; init; }

    public DateTimeOffset? IgnoredAt { get; init; }

    public string? IgnoredReason { get; init; }
}
