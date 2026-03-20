using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteDetailViewModel
{
    private readonly SiteMergedView detail;

    private SiteDetailViewModel(SiteMergedView detail, DemoCoordinate? displayCoordinateOverride)
    {
        this.detail = detail;

        DeviceCode = detail.DeviceCode;
        DeviceName = detail.DeviceName;
        DisplayName = detail.DisplayName;
        Alias = string.IsNullOrWhiteSpace(detail.Alias) ? "未设置别名" : detail.Alias;
        Remark = string.IsNullOrWhiteSpace(detail.Remark) ? "暂无补充说明" : detail.Remark;
        IsMonitored = detail.IsMonitored;
        MonitoringText = detail.IsMonitored ? "已纳入静默巡检" : "未纳入静默巡检";
        OnlineStateText = detail.DemoOnlineState switch
        {
            DemoOnlineState.Online => "在线",
            DemoOnlineState.Offline => "离线",
            _ => "未知"
        };
        CoordinateSourceText = detail.CoordinateSourceText;
        CoordinateStatusText = BuildCoordinateStatusText(detail, displayCoordinateOverride);
        PlatformStatusSummary = detail.PlatformStatusSummary;
        RuntimeSummaryText = string.IsNullOrWhiteSpace(detail.RuntimeSummary) ? "尚未产生运行态摘要。" : detail.RuntimeSummary!;
        LastInspectionAtText = detail.LastInspectionAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未巡检";
        LastInspectionRunStateText = ResolveInspectionRunStateText(detail.LastInspectionRunState);
        LastPreviewResolveStateText = ResolvePreviewResolveStateText(detail.LastPreviewResolveState);
        LastProductStateText = string.IsNullOrWhiteSpace(detail.LastProductState) ? "暂无" : detail.LastProductState!;
        ConsecutiveFailureText = detail.ConsecutiveFailureCount <= 0 ? "0 次" : $"{detail.ConsecutiveFailureCount} 次";
        LastSnapshotAtText = detail.LastSnapshotAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无截图";
        LastSnapshotPath = detail.LastSnapshotPath;
        HasSnapshot = !string.IsNullOrWhiteSpace(detail.LastSnapshotPath);
        HasDispatchRecord = detail.HasDispatchRecord;
        DispatchRecordId = detail.DispatchRecordId;
        DispatchStatusText = ResolveDispatchStatusText(detail);
        RecoveryStatusText = ResolveRecoveryStatusText(detail);
        DispatchHeadlineText = detail.HasDispatchRecord
            ? $"{DispatchStatusText} / {RecoveryStatusText}"
            : "未触发派单 / 未恢复";
        DispatchTriggeredAtText = detail.DispatchTriggeredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未触发";
        LastDispatchAtText = detail.DispatchSentAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未发送";
        CoolingUntilText = detail.CoolingUntil?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未进入冷却";
        RecoveredAtText = detail.RecoveredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未恢复";
        RecoverySummaryText = string.IsNullOrWhiteSpace(detail.RecoverySummary) ? "暂无恢复摘要" : detail.RecoverySummary!;
        CanConfirmRecovery = detail.CanConfirmRecovery && detail.DispatchRecordId.HasValue;

        var displayCoordinate = ResolveCurrentDisplayCoordinate(detail, displayCoordinateOverride);
        LongitudeText = displayCoordinate?.Longitude.ToString("F6") ?? ResolveDisplayCoordinateFallback(detail);
        LatitudeText = displayCoordinate?.Latitude.ToString("F6") ?? ResolveDisplayCoordinateFallback(detail);

        PlatformCoordinateText = detail.PlatformRawLongitude.HasValue && detail.PlatformRawLatitude.HasValue
            ? $"{detail.PlatformRawLongitude.Value:F6}, {detail.PlatformRawLatitude.Value:F6}"
            : "平台未返回";
        ManualCoordinateText = detail.ManualLongitude.HasValue && detail.ManualLatitude.HasValue
            ? $"{detail.ManualLongitude.Value:F6}, {detail.ManualLatitude.Value:F6}"
            : "尚未补录";
        AddressText = string.IsNullOrWhiteSpace(detail.AddressText) ? "地址待补充" : detail.AddressText;
        ProductAccessNumber = string.IsNullOrWhiteSpace(detail.ProductAccessNumber) ? "未补充" : detail.ProductAccessNumber;
        MaintenanceUnit = string.IsNullOrWhiteSpace(detail.MaintenanceUnit) ? "维护单位待补充" : detail.MaintenanceUnit;
        MaintainerName = string.IsNullOrWhiteSpace(detail.MaintainerName) ? "维护人待补充" : detail.MaintainerName;
        MaintainerPhone = string.IsNullOrWhiteSpace(detail.MaintainerPhone) ? "联系电话待补充" : detail.MaintainerPhone;
        LocalProfileStatusText = detail.HasLocalProfile ? "已保存本地补充信息" : "尚未保存本地补充信息";
        VisualState = detail.VisualState;
        StatusText = ResolveStatusText(detail);
        UpdatedAtText = detail.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "尚未保存";
    }

    public string DeviceCode { get; }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public string Alias { get; }

    public string Remark { get; }

    public bool IsMonitored { get; }

    public string MonitoringText { get; }

    public string OnlineStateText { get; }

    public string CoordinateSourceText { get; }

    public string CoordinateStatusText { get; }

    public string PlatformStatusSummary { get; }

    public string RuntimeSummaryText { get; }

    public string LastInspectionAtText { get; }

    public string LastInspectionRunStateText { get; }

    public string LastPreviewResolveStateText { get; }

    public string LastProductStateText { get; }

    public string ConsecutiveFailureText { get; }

    public string LastSnapshotAtText { get; }

    public string? LastSnapshotPath { get; }

    public bool HasSnapshot { get; }

    public bool HasDispatchRecord { get; }

    public long? DispatchRecordId { get; }

    public string DispatchHeadlineText { get; }

    public string DispatchStatusText { get; }

    public string DispatchTriggeredAtText { get; }

    public string LastDispatchAtText { get; }

    public string CoolingUntilText { get; }

    public string RecoveryStatusText { get; }

    public string RecoveredAtText { get; }

    public string RecoverySummaryText { get; }

    public bool CanConfirmRecovery { get; }

    public string LongitudeText { get; }

    public string LatitudeText { get; }

    public string PlatformCoordinateText { get; }

    public string ManualCoordinateText { get; }

    public string AddressText { get; }

    public string ProductAccessNumber { get; }

    public string MaintenanceUnit { get; }

    public string MaintainerName { get; }

    public string MaintainerPhone { get; }

    public string LocalProfileStatusText { get; }

    public SiteVisualState VisualState { get; }

    public string StatusText { get; }

    public string UpdatedAtText { get; }

    public static SiteDetailViewModel FromSnapshot(SiteMergedView detail, DemoCoordinate? displayCoordinateOverride = null)
    {
        return new SiteDetailViewModel(detail, displayCoordinateOverride);
    }

    public SiteLocalProfileInput CreateLocalProfileInput(bool? overrideIsMonitored = null)
    {
        return new SiteLocalProfileInput
        {
            DeviceCode = detail.DeviceCode,
            Alias = detail.Alias,
            Remark = detail.Remark,
            IsMonitored = overrideIsMonitored ?? detail.IsMonitored,
            ManualLongitude = detail.ManualLongitude,
            ManualLatitude = detail.ManualLatitude,
            AddressText = detail.AddressText,
            ProductAccessNumber = detail.ProductAccessNumber,
            MaintenanceUnit = detail.MaintenanceUnit,
            MaintainerName = detail.MaintainerName,
            MaintainerPhone = detail.MaintainerPhone
        };
    }

    public SiteEditorViewModel CreateEditorViewModel()
    {
        return SiteEditorViewModel.CreateFromSite(detail);
    }

    private static string ResolveStatusText(SiteMergedView detail)
    {
        if (!detail.IsMonitored)
        {
            return "未纳管";
        }

        if (detail.CanConfirmRecovery)
        {
            return "待确认恢复";
        }

        if (detail.RecoveryStatus is RecoveryStatus.Recovered or RecoveryStatus.NotificationFailed)
        {
            return "已恢复";
        }

        if (detail.IsDispatchCooling)
        {
            return "冷却中";
        }

        return detail.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "未配置 webhook",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.Offline => "设备离线",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.InspectionExecutionFailed => "巡检执行失败",
            _ => detail.DemoOnlineState == DemoOnlineState.Offline ? "设备离线" : "正常"
        };
    }

    private static string ResolveDispatchStatusText(SiteMergedView detail)
    {
        if (!detail.HasDispatchRecord)
        {
            return "未触发派单";
        }

        if (detail.IsDispatchCooling)
        {
            return "冷却中";
        }

        return detail.DispatchStatus switch
        {
            DispatchStatus.PendingDispatch => "待派单",
            DispatchStatus.Dispatched => "已派单",
            DispatchStatus.SendFailed => "发送失败",
            DispatchStatus.WebhookNotConfigured => "未配置 webhook",
            _ => "未触发派单"
        };
    }

    private static string ResolveRecoveryStatusText(SiteMergedView detail)
    {
        return detail.RecoveryStatus switch
        {
            RecoveryStatus.PendingConfirmation => "待人工确认恢复",
            RecoveryStatus.Recovered => "已恢复",
            RecoveryStatus.NotificationFailed => "已恢复（通知未发送）",
            _ => detail.RecoveredAt.HasValue ? "已恢复" : "未恢复"
        };
    }

    private static string BuildCoordinateStatusText(SiteMergedView detail, DemoCoordinate? displayCoordinateOverride)
    {
        return detail.CoordinateSource switch
        {
            CoordinateSource.PlatformRaw when displayCoordinateOverride is not null
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），已由地图宿主转换显示",
            CoordinateSource.PlatformRaw when RequiresFrontendConversion(detail.PlatformRawCoordinateType)
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），等待地图宿主转换",
            CoordinateSource.PlatformRaw
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），当前按 GCJ-02 直接显示",
            CoordinateSource.ManualOverride => "当前使用本地手工坐标（GCJ-02）",
            _ => "当前暂无可显示坐标"
        };
    }

    private static DemoCoordinate? ResolveCurrentDisplayCoordinate(
        SiteMergedView detail,
        DemoCoordinate? displayCoordinateOverride)
    {
        if (displayCoordinateOverride is not null)
        {
            return displayCoordinateOverride;
        }

        return detail.CoordinateSource switch
        {
            CoordinateSource.ManualOverride when detail.ManualLongitude.HasValue && detail.ManualLatitude.HasValue
                => new DemoCoordinate
                {
                    Longitude = detail.ManualLongitude.Value,
                    Latitude = detail.ManualLatitude.Value
                },
            CoordinateSource.PlatformRaw when !RequiresFrontendConversion(detail.PlatformRawCoordinateType)
                                               && detail.PlatformRawLongitude.HasValue
                                               && detail.PlatformRawLatitude.HasValue
                => new DemoCoordinate
                {
                    Longitude = detail.PlatformRawLongitude.Value,
                    Latitude = detail.PlatformRawLatitude.Value
                },
            _ => null
        };
    }

    private static string ResolveDisplayCoordinateFallback(SiteMergedView detail)
    {
        if (detail.CoordinateSource == CoordinateSource.PlatformRaw
            && RequiresFrontendConversion(detail.PlatformRawCoordinateType))
        {
            return "等待地图转换";
        }

        return "暂无";
    }

    private static bool RequiresFrontendConversion(string coordinateType)
    {
        return coordinateType.ToLowerInvariant() switch
        {
            "bd09" => true,
            "baidu" => true,
            "wgs84" => true,
            "gps" => true,
            "mapbar" => true,
            _ => false
        };
    }

    private static string ResolveCoordinateTypeLabel(string coordinateType)
    {
        return coordinateType.ToLowerInvariant() switch
        {
            "bd09" => "bd09",
            "baidu" => "bd09",
            "gcj02" => "gcj02",
            "wgs84" => "wgs84",
            "gps" => "wgs84/gps",
            "mapbar" => "mapbar",
            _ => "unknown"
        };
    }

    private static string ResolveInspectionRunStateText(InspectionRunState state)
    {
        return state switch
        {
            InspectionRunState.Succeeded => "巡检成功",
            InspectionRunState.SucceededWithFault => "巡检完成，存在异常",
            InspectionRunState.Failed => "巡检失败",
            InspectionRunState.Skipped => "巡检跳过",
            _ => "尚未巡检"
        };
    }

    private static string ResolvePreviewResolveStateText(PreviewResolveState state)
    {
        return state switch
        {
            PreviewResolveState.Resolved => "解析成功",
            PreviewResolveState.Failed => "解析失败",
            PreviewResolveState.Skipped => "已跳过",
            _ => "未知"
        };
    }
}
