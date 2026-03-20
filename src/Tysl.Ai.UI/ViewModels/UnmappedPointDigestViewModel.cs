using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class UnmappedPointDigestViewModel
{
    public UnmappedPointDigestViewModel(UnmappedPointDigest digest)
    {
        DeviceCode = digest.DeviceCode;
        DisplayName = digest.DisplayName;
        DeviceName = digest.DeviceName;
        IsMonitored = digest.IsMonitored;
        UnmappedReasonText = digest.UnmappedReasonText;
        CoordinateSourceText = digest.CoordinateSourceText;
        GovernanceHintText = digest.GovernanceHintText;
        PlatformCoordinateText = string.IsNullOrWhiteSpace(digest.PlatformCoordinateText)
            ? "平台无可用坐标"
            : $"{digest.PlatformCoordinateText} ({digest.PlatformCoordinateTypeText})";
        ManualCoordinateText = string.IsNullOrWhiteSpace(digest.ManualCoordinateText)
            ? "手工坐标未补录"
            : digest.ManualCoordinateText;
        MonitoringText = digest.IsMonitored ? "已纳管" : "未纳管";
    }

    public string DeviceCode { get; }

    public string DisplayName { get; }

    public string DeviceName { get; }

    public bool IsMonitored { get; }

    public string UnmappedReasonText { get; }

    public string CoordinateSourceText { get; }

    public string GovernanceHintText { get; }

    public string PlatformCoordinateText { get; }

    public string ManualCoordinateText { get; }

    public string MonitoringText { get; }
}
