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
    }

    public string PointId { get; }

    public string PointDisplayName { get; }

    public string IssueLabel { get; }

    public string OccurredAtText { get; }
}
