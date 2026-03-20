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
        DeviceName = point.DeviceName;
        DisplayName = point.DisplayName;
        VisualState = point.VisualState;
        IsMonitored = point.IsMonitored;
        RawCoordinateType = point.CoordinatePayload.RawCoordinateType;
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
            ? BuildCoordinateSummary(point)
            : point.RuntimeSummaryText!;
        SummaryText = RuntimeSummaryText;
        LastInspectionAtText = point.LastInspectionAt?.ToLocalTime().ToString("HH:mm:ss") ?? "--:--:--";
        LastSnapshotPath = point.LastSnapshotPath;
        HasSnapshot = !string.IsNullOrWhiteSpace(point.LastSnapshotPath);

        var maintenanceUnit = string.IsNullOrWhiteSpace(point.MaintenanceUnit) ? "维护单位待补充" : point.MaintenanceUnit!;
        var maintainerName = string.IsNullOrWhiteSpace(point.MaintainerName) ? "维护人待补充" : point.MaintainerName!;
        MaintenanceLine = $"{maintenanceUnit} / {maintainerName}";
    }

    public string DeviceCode { get; }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public string StatusText { get; }

    public SiteVisualState VisualState { get; }

    public bool IsMonitored { get; }

    public double? PlatformRawLongitude { get; }

    public double? PlatformRawLatitude { get; }

    public string RawCoordinateType { get; }

    public double? ManualLongitude { get; }

    public double? ManualLatitude { get; }

    public string CoordinateSourceText { get; }

    public string AddressText { get; }

    public string OnlineStateText { get; }

    public string MonitoringText { get; }

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
            DisplayName = DisplayName,
            DeviceName = DeviceName,
            StatusText = StatusText,
            VisualState = VisualState.ToString().ToLowerInvariant(),
            OnlineStateText = OnlineStateText,
            MonitoringText = MonitoringText,
            SummaryText = SummaryText,
            DispatchStateKey = DispatchStateKey,
            DispatchStateText = DispatchStateText,
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
            return "待确认恢复";
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
            DispatchStatus.WebhookNotConfigured => "未配置 webhook",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.Offline => "设备离线",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            DispatchStatus.None when point.RuntimeFaultCode == RuntimeFaultCode.InspectionExecutionFailed => "巡检执行失败",
            _ => point.DemoOnlineState == DemoOnlineState.Offline ? "设备离线" : "正常"
        };
    }

    private static string ResolveDispatchStateText(SiteMapPoint point)
    {
        if (point.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return "待确认恢复";
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
            DispatchStatus.WebhookNotConfigured => "未配置 webhook",
            _ => "未处置"
        };
    }

    private static string BuildCoordinateSummary(SiteMapPoint point)
    {
        if (!point.IsMonitored)
        {
            return "当前未纳入静默巡检。";
        }

        return point.CoordinatePayload.CoordinateSource switch
        {
            CoordinateSource.ManualOverride => "当前使用本地手工坐标。",
            CoordinateSource.PlatformRaw => "当前使用平台原始坐标。",
            _ => "当前暂无可用坐标。"
        };
    }
}
