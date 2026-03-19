namespace Tysl.Ai.Core.Models;

public sealed record AlertDigest
{
    public required Guid PointId { get; init; }

    public required string PointAlias { get; init; }

    public required string IssueLabel { get; init; }

    public required string OccurredAtText { get; init; }
}
