using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SitePreviewSession
{
    public required string PlaybackSessionId { get; init; }

    public required string DeviceCode { get; init; }

    public required SitePreviewProtocol SelectedProtocol { get; init; }

    public required IReadOnlyList<SitePreviewProtocol> AttemptedProtocols { get; init; }

    public required string SourceUrl { get; init; }

    public string? WebRtcApiUrl { get; init; }

    public required bool WebRtcUrlAcquired { get; init; }

    public required int ReadyTimeoutSeconds { get; init; }

    public required bool UsedFallback { get; init; }
}
