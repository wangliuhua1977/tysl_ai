using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteAlertDigestViewModel
{
    public SiteAlertDigestViewModel(SiteAlertDigest alertDigest)
    {
        PointId = alertDigest.PointId;
        PointDisplayName = alertDigest.PointDisplayName;
        IssueLabel = alertDigest.IssueLabel;
        OccurredAtText = alertDigest.OccurredAtText;
        RuntimeSummary = string.IsNullOrWhiteSpace(alertDigest.RuntimeSummary)
            ? alertDigest.IssueLabel
            : alertDigest.RuntimeSummary;
        SnapshotPath = alertDigest.SnapshotPath;
    }

    public string PointId { get; }

    public string PointDisplayName { get; }

    public string IssueLabel { get; }

    public string OccurredAtText { get; }

    public string RuntimeSummary { get; }

    public string? SnapshotPath { get; }

    public bool HasSnapshot => !string.IsNullOrWhiteSpace(SnapshotPath);
}
