namespace Tysl.Ai.Core.Models;

public sealed record WebhookMessage
{
    public required string Content { get; init; }

    public IReadOnlyList<string> MentionMobiles { get; init; } = Array.Empty<string>();

    public bool MentionAll { get; init; }
}
