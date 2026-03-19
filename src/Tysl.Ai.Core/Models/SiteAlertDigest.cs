namespace Tysl.Ai.Core.Models;

public sealed record SiteAlertDigest
{
    public required string PointId { get; init; }

    public required string PointDisplayName { get; init; }

    public required string IssueLabel { get; init; }

    public required string OccurredAtText { get; init; }
}
