using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SitePreviewStreamBundle
{
    public int? ExpireIn { get; init; }

    public int? VideoEnc { get; init; }

    public required IReadOnlyList<SitePreviewStreamCandidate> Streams { get; init; }
}

public sealed record SitePreviewStreamCandidate
{
    public required int Order { get; init; }

    public int? PlatformProtocolCode { get; init; }

    public string RawProtocol { get; init; } = string.Empty;

    public required SitePreviewProtocol NormalizedProtocol { get; init; }

    public required string StreamUrl { get; init; }

    public int? Level { get; init; }

    public bool IsSupported { get; init; }

    public string? UnsupportedReason { get; init; }

    public string? WebRtcApiUrl { get; init; }
}
