using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record SitePreviewResolveResult
{
    public required bool IsSuccess { get; init; }

    public SitePreviewSession? Session { get; init; }

    public required IReadOnlyList<SitePreviewProtocol> AttemptedProtocols { get; init; }

    public string? FailureReason { get; init; }
}
