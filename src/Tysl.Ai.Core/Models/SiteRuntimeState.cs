using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SiteRuntimeState
{
    public required string DeviceCode { get; init; }

    public DateTimeOffset? LastInspectionAt { get; init; }

    public required DemoOnlineState LastOnlineState { get; init; }

    public string? LastProductState { get; init; }

    public required PreviewResolveState LastPreviewResolveState { get; init; }

    public string? LastSnapshotPath { get; init; }

    public DateTimeOffset? LastSnapshotAt { get; init; }

    public required RuntimeFaultCode LastFaultCode { get; init; }

    public string? LastFaultSummary { get; init; }

    public required int ConsecutiveFailureCount { get; init; }

    public required InspectionRunState LastInspectionRunState { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
