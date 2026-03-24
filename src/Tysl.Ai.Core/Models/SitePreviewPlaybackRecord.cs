using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SitePreviewPlaybackRecord
{
    public required string DeviceCode { get; init; }

    public required string PlaybackSessionId { get; init; }

    public required SitePreviewProtocol PreferredProtocol { get; init; }

    public required SitePreviewProtocol Protocol { get; init; }

    public required bool IsSuccess { get; init; }

    public required bool UsedFallback { get; init; }

    public required SitePreviewProtocol FailureProtocol { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
