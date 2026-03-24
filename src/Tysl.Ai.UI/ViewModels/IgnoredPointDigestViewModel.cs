using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class IgnoredPointDigestViewModel
{
    public IgnoredPointDigestViewModel(IgnoredPointDigest digest)
    {
        DeviceCode = digest.DeviceCode;
        DisplayName = digest.DisplayName;
        DeviceName = digest.DeviceName;
        IsMonitored = digest.IsMonitored;
        IgnoredAtText = digest.IgnoredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "时间未记录";
        IgnoredReasonText = string.IsNullOrWhiteSpace(digest.IgnoredReason) ? "未填写忽略说明" : digest.IgnoredReason;
        MonitoringText = digest.IsMonitored ? "恢复后继续巡检" : "恢复后仍为未纳管";
    }

    public string DeviceCode { get; }

    public string DisplayName { get; }

    public string DeviceName { get; }

    public bool IsMonitored { get; }

    public string IgnoredAtText { get; }

    public string IgnoredReasonText { get; }

    public string MonitoringText { get; }
}
