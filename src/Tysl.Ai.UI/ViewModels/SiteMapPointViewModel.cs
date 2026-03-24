using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;
using Tysl.Ai.UI.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteMapPointViewModel : ObservableObject
{
    private bool isSelected;

    public SiteMapPointViewModel(SiteMapPoint point)
    {
        DeviceCode = point.DeviceCode;
        Alias = point.Alias;
        DeviceName = point.DeviceName;
        DisplayName = point.DisplayName;
        VisualState = point.VisualState;
        IsMonitored = point.IsMonitored;
        RawCoordinateType = point.CoordinatePayload.RawCoordinateType;
        DisplayLongitude = point.DisplayLongitude;
        DisplayLatitude = point.DisplayLatitude;
        PlatformRawLongitude = point.CoordinatePayload.PlatformRawLongitude;
        PlatformRawLatitude = point.CoordinatePayload.PlatformRawLatitude;
        ManualLongitude = point.CoordinatePayload.ManualLongitude;
        ManualLatitude = point.CoordinatePayload.ManualLatitude;
        CoordinateSourceText = point.CoordinatePayload.CoordinateSourceText;
        AddressText = string.IsNullOrWhiteSpace(point.AddressText) ? "地址待补充" : point.AddressText!;
        OnlineStateText = point.DemoOnlineState == DemoOnlineState.Offline ? "离线" : "在线";
        MonitoringText = point.IsMonitored ? "已纳管" : "未纳管";
        DispatchStateKey = point.DispatchStateKey;
        DispatchStateText = ResolveDispatchStateText(point);
        StatusText = ResolveStatusText(point);
        RuntimeSummaryText = string.IsNullOrWhiteSpace(point.RuntimeSummaryText)
            ? BuildFallbackSummary(point)
            : point.RuntimeSummaryText!;
        SummaryText = RuntimeSummaryText;
        StatusBadges = BuildStatusBadges(StatusText, DispatchStateText);
        LastInspectionAtText = point.LastInspectionAt?.ToLocalTime().ToString("HH:mm:ss") ?? "--:--:--";
        LastSnapshotPath = point.LastSnapshotPath;
        HasSnapshot = !string.IsNullOrWhiteSpace(point.LastSnapshotPath);

        var maintenanceUnit = string.IsNullOrWhiteSpace(point.MaintenanceUnit) ? "维护单位待补充" : point.MaintenanceUnit!;
        var maintainerName = string.IsNullOrWhiteSpace(point.MaintainerName) ? "维护人待补充" : point.MaintainerName!;
        MaintenanceLine = $"{maintenanceUnit} / {maintainerName}";
    }

    public string DeviceCode { get; }

    public string? Alias { get; }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public string StatusText { get; }

    public SiteVisualState VisualState { get; }

    public bool IsMonitored { get; }

    public double? DisplayLongitude { get; }

    public double? DisplayLatitude { get; }

    public double? PlatformRawLongitude { get; }

    public double? PlatformRawLatitude { get; }

    public string RawCoordinateType { get; }

    public double? ManualLongitude { get; }

    public double? ManualLatitude { get; }

    public string CoordinateSourceText { get; }

    public string AddressText { get; }

    public string OnlineStateText { get; }

    public string MonitoringText { get; }

    public IReadOnlyList<string> StatusBadges { get; }

    public string SummaryText { get; }

    public string RuntimeSummaryText { get; }

    public string DispatchStateText { get; }

    public string DispatchStateKey { get; }

    public string LastInspectionAtText { get; }

    public string? LastSnapshotPath { get; }

    public bool HasSnapshot { get; }

    public string MaintenanceLine { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public MapHostPointDto ToMapHostPoint()
    {
        return new MapHostPointDto
        {
            DeviceCode = DeviceCode,
            Alias = Alias,
            DisplayName = DisplayName,
            DeviceName = DeviceName,
            StatusText = StatusText,
            VisualState = VisualState.ToString().ToLowerInvariant(),
            OnlineStateText = OnlineStateText,
            MonitoringText = MonitoringText,
            StatusBadges = StatusBadges,
            SummaryText = SummaryText,
            DispatchStateKey = DispatchStateKey,
            DispatchStateText = DispatchStateText,
            DisplayLongitude = DisplayLongitude,
            DisplayLatitude = DisplayLatitude,
            PlatformRawLongitude = PlatformRawLongitude,
            PlatformRawLatitude = PlatformRawLatitude,
            RawCoordinateType = RawCoordinateType,
            ManualLongitude = ManualLongitude,
            ManualLatitude = ManualLatitude
        };
    }

    private static string ResolveStatusText(SiteMapPoint point)
    {
        if (!point.IsMonitored)
        {
            return "未纳管";
        }

        if (point.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return "待恢复确认";
        }

        if (point.RecoveryStatus is RecoveryStatus.Recovered or RecoveryStatus.NotificationFailed)
        {
            return "已恢复";
        }

        if (point.IsDispatchCooling)
        {
            return "冷却中";
        }

        return point.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "待发送",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.Offline => "设备离线",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.InspectionExecutionFailed => "巡检失败",
            _ => point.DemoOnlineState == DemoOnlineState.Offline ? "设备离线" : "正常"
        };
    }

    private static string ResolveDispatchStateText(SiteMapPoint point)
    {
        if (point.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return "待恢复确认";
        }

        if (point.RecoveryStatus is RecoveryStatus.Recovered or RecoveryStatus.NotificationFailed)
        {
            return "已恢复";
        }

        if (point.IsDispatchCooling)
        {
            return "冷却中";
        }

        return point.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "待发送",
            _ => "未处置"
        };
    }

    private static string BuildFallbackSummary(SiteMapPoint point)
    {
        if (!point.IsMonitored)
        {
            return "当前点位未纳入静默巡检。";
        }

        if (point.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return "已派单，待恢复确认。";
        }

        if (point.RecoveryStatus is RecoveryStatus.Recovered or RecoveryStatus.NotificationFailed)
        {
            return "运行态已恢复。";
        }

        if (point.IsDispatchCooling)
        {
            return "已派单，处理中。";
        }

        return point.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "异常已识别，待派单。",
            DispatchStatus.Dispatched => "已派单，待现场处置。",
            DispatchStatus.SendFailed => "异常已识别，待重试派单。",
            DispatchStatus.WebhookNotConfigured => "异常已识别，待发送派单。",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.Offline => "设备离线。",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.PreviewResolveFailed => "预览解析失败。",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.SnapshotFailed => "截图留痕失败。",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.InspectionExecutionFailed => "巡检失败。",
            _ => "等待首次巡检。"
        };
    }

    private static IReadOnlyList<string> BuildStatusBadges(string statusText, string dispatchStateText)
    {
        var badges = new List<string>(2);
        TryAddBadge(badges, statusText);

        if (!string.Equals(dispatchStateText, "未处置", StringComparison.Ordinal))
        {
            TryAddBadge(badges, dispatchStateText);
        }

        return badges;
    }

    private static void TryAddBadge(ICollection<string> badges, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (badges.Contains(normalized) || badges.Count >= 2)
        {
            return;
        }

        badges.Add(normalized);
    }
}
