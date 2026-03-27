using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Map;
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
        IsIgnored = detail.IsIgnored;
        IsMonitored = detail.IsMonitored;
        FocusScopeText = detail.IsIgnored ? "已忽略，退出主值守视图" : "主值守视图";
        IgnoredAtText = detail.IgnoredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未忽略";
        IgnoredReasonText = string.IsNullOrWhiteSpace(detail.IgnoredReason) ? "未填写" : detail.IgnoredReason;
        MonitoringText = detail.IsIgnored
            ? "已忽略，退出巡检"
            : detail.IsMonitored ? "已纳入静默巡检" : "未纳入静默巡检";
        OnlineStateText = detail.DemoOnlineState switch
        {
            DemoOnlineState.Online => "在线",
            DemoOnlineState.Offline => "离线",
            _ => "未知"
        };
        CoordinateDisplayStatusText = detail.CoordinateDisplayStatusText;
        CoordinateSourceText = detail.CoordinateSourceText;
        CoordinateStatusText = BuildCoordinateStatusText(detail, displayCoordinateOverride);
        UnmappedReasonText = detail.UnmappedReasonText;
        CoordinateGovernanceHintText = detail.CoordinateGovernanceHintText;
        PlatformStatusSummary = detail.PlatformStatusSummary;
        RuntimeSummaryText = string.IsNullOrWhiteSpace(detail.RuntimeSummary) ? "尚未产生运行摘要。" : detail.RuntimeSummary!;
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
        ClosedArchivedAtText = detail.ClosedArchivedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未归档";
        RecoverySourceText = ResolveRecoverySourceText(detail.RecoverySource);
        RecoverySummaryText = string.IsNullOrWhiteSpace(detail.RecoverySummary) ? "暂无恢复摘要" : detail.RecoverySummary!;
        ClosingRemarkText = string.IsNullOrWhiteSpace(detail.ClosingRemark) ? "未填写" : detail.ClosingRemark;
        LastNotificationSummaryText = string.IsNullOrWhiteSpace(detail.DispatchMessageDigest) ? "暂无通知摘要" : detail.DispatchMessageDigest;
        CanConfirmRecovery = !detail.IsIgnored && detail.CanConfirmRecovery && detail.DispatchRecordId.HasValue;
        CloseActionText = "确认已处理";
        AbnormalReasonText = ResolveAbnormalReasonText(detail);
        CanManualDispatch = ResolveCanManualDispatch(detail);

        var displayCoordinate = ResolveCurrentDisplayCoordinate(detail, displayCoordinateOverride);
        LongitudeText = displayCoordinate?.Longitude.ToString("F6") ?? ResolveDisplayCoordinateFallback(detail);
        LatitudeText = displayCoordinate?.Latitude.ToString("F6") ?? ResolveDisplayCoordinateFallback(detail);
        CurrentDisplayCoordinateText = displayCoordinate is null
            ? ResolveDisplayCoordinateFallback(detail)
            : $"{displayCoordinate.Longitude:F6}, {displayCoordinate.Latitude:F6}";

        PlatformCoordinateText = detail.PlatformRawLongitude.HasValue && detail.PlatformRawLatitude.HasValue
            ? $"{detail.PlatformRawLongitude.Value:F6}, {detail.PlatformRawLatitude.Value:F6}"
            : "平台未返回";
        PlatformRawCoordinateTypeText = CoordinateTypeCatalog.GetDisplayLabel(detail.PlatformRawCoordinateType);
        ManualCoordinateText = detail.ManualLongitude.HasValue && detail.ManualLatitude.HasValue
            ? $"{detail.ManualLongitude.Value:F6}, {detail.ManualLatitude.Value:F6}"
            : "尚未补录";
        AddressText = string.IsNullOrWhiteSpace(detail.AddressText) ? "地址待补充" : detail.AddressText;
        ProductAccessNumber = string.IsNullOrWhiteSpace(detail.ProductAccessNumber) ? "未补充" : detail.ProductAccessNumber;
        ProductStatusText = string.IsNullOrWhiteSpace(detail.ProductStatus) ? "待接入" : detail.ProductStatus;
        ArrearsAmountText = detail.ArrearsAmount.HasValue ? $"{detail.ArrearsAmount.Value:F2} 元" : "待接入";
        MaintenanceUnit = string.IsNullOrWhiteSpace(detail.MaintenanceUnit) ? "维护单位待补充" : detail.MaintenanceUnit;
        MaintainerName = string.IsNullOrWhiteSpace(detail.MaintainerName) ? "维护人待补充" : detail.MaintainerName;
        MaintainerPhone = string.IsNullOrWhiteSpace(detail.MaintainerPhone) ? "联系电话待补充" : detail.MaintainerPhone;
        AreaName = string.IsNullOrWhiteSpace(detail.AreaName) ? "片区待补充" : detail.AreaName;
        DefaultDispatchRemark = string.IsNullOrWhiteSpace(detail.DefaultDispatchRemark) ? "未设置默认派单说明" : detail.DefaultDispatchRemark;
        IsAutoDispatchEnabled = detail.IsAutoDispatchEnabled;
        AllowRecoveryAutoArchive = detail.AllowRecoveryAutoArchive;
        RecoveryConfirmationModeText = ResolveRecoveryConfirmationModeText(detail.RecoveryConfirmationMode);
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

    public bool IsIgnored { get; }

    public bool IsMonitored { get; }

    public string FocusScopeText { get; }

    public string IgnoredAtText { get; }

    public string IgnoredReasonText { get; }

    public string MonitoringText { get; }

    public string OnlineStateText { get; }

    public string CoordinateDisplayStatusText { get; }

    public string CoordinateSourceText { get; }

    public string CoordinateStatusText { get; }

    public string UnmappedReasonText { get; }

    public string CoordinateGovernanceHintText { get; }

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

    public string ClosedArchivedAtText { get; }

    public string RecoverySourceText { get; }

    public string RecoverySummaryText { get; }

    public string ClosingRemarkText { get; }

    public string LastNotificationSummaryText { get; }

    public bool CanConfirmRecovery { get; }

    public string CloseActionText { get; }

    public string AbnormalReasonText { get; }

    public bool CanManualDispatch { get; }

    public string LongitudeText { get; }

    public string LatitudeText { get; }

    public string CurrentDisplayCoordinateText { get; }

    public string PlatformCoordinateText { get; }

    public string PlatformRawCoordinateTypeText { get; }

    public string ManualCoordinateText { get; }

    public string AddressText { get; }

    public string ProductAccessNumber { get; }

    public string ProductStatusText { get; }

    public string ArrearsAmountText { get; }

    public string MaintenanceUnit { get; }

    public string MaintainerName { get; }

    public string MaintainerPhone { get; }

    public string AreaName { get; }

    public string DefaultDispatchRemark { get; }

    public bool IsAutoDispatchEnabled { get; }

    public bool AllowRecoveryAutoArchive { get; }

    public string RecoveryConfirmationModeText { get; }

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
            MaintainerPhone = detail.MaintainerPhone,
            AreaName = detail.AreaName,
            DefaultDispatchRemark = detail.DefaultDispatchRemark,
            IsAutoDispatchEnabled = detail.IsAutoDispatchEnabled,
            AllowRecoveryAutoArchive = detail.AllowRecoveryAutoArchive,
            RecoveryConfirmationMode = detail.RecoveryConfirmationMode
        };
    }

    public SiteEditorViewModel CreateEditorViewModel()
    {
        return SiteEditorViewModel.CreateFromSite(detail);
    }

    private static string ResolveStatusText(SiteMergedView detail)
    {
        if (detail.IsIgnored)
        {
            return "已忽略";
        }

        if (!detail.IsMonitored)
        {
            return "未纳管";
        }

        if (detail.CanConfirmRecovery)
        {
            return "待恢复确认";
        }

        if (detail.WorkOrderStatus == DispatchWorkOrderStatus.ClosedArchived)
        {
            return "已归档";
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
            DispatchStatus.WebhookNotConfigured => "待发送",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.Offline => "设备离线",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.PreviewResolveFailed => "预览解析失败",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.SnapshotFailed => "截图留痕失败",
            DispatchStatus.None when detail.RuntimeFaultCode == RuntimeFaultCode.InspectionExecutionFailed => "巡检失败",
            _ => detail.DemoOnlineState == DemoOnlineState.Offline ? "设备离线" : "正常"
        };
    }

    private static string ResolveDispatchStatusText(SiteMergedView detail)
    {
        if (!detail.HasDispatchRecord)
        {
            return "未触发派单";
        }

        if (detail.WorkOrderStatus == DispatchWorkOrderStatus.ClosedArchived)
        {
            return "已归档";
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
            DispatchStatus.WebhookNotConfigured => "待发送",
            _ => "未触发派单"
        };
    }

    private static string ResolveRecoveryStatusText(SiteMergedView detail)
    {
        if (detail.WorkOrderStatus == DispatchWorkOrderStatus.ClosedArchived)
        {
            return "已归档";
        }

        return detail.RecoveryStatus switch
        {
            RecoveryStatus.PendingConfirmation => "待恢复确认",
            RecoveryStatus.Recovered => "已恢复",
            RecoveryStatus.NotificationFailed => "已恢复（通知未发送）",
            _ => detail.RecoveredAt.HasValue ? "已恢复" : "未恢复"
        };
    }

    private static string ResolveAbnormalReasonText(SiteMergedView detail)
    {
        if (detail.IsIgnored)
        {
            return "当前点位已忽略，不参与主值守派单。";
        }

        if (detail.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return "现场已处置，等待恢复确认。";
        }

        if (detail.HasDispatchRecord && detail.DispatchStatus != DispatchStatus.None && !detail.RecoveredAt.HasValue)
        {
            return string.IsNullOrWhiteSpace(detail.DispatchFaultSummary)
                ? "异常已进入派单链路。"
                : detail.DispatchFaultSummary!;
        }

        if (detail.RuntimeFaultCode != RuntimeFaultCode.None)
        {
            return string.IsNullOrWhiteSpace(detail.RuntimeSummary)
                ? ResolveStatusText(detail)
                : detail.RuntimeSummary!;
        }

        if (detail.LastInspectionRunState is InspectionRunState.Failed or InspectionRunState.SucceededWithFault)
        {
            return string.IsNullOrWhiteSpace(detail.RuntimeSummary)
                ? ResolveInspectionRunStateText(detail.LastInspectionRunState)
                : detail.RuntimeSummary!;
        }

        return "当前未发现需要派单的异常。";
    }

    private static bool ResolveCanManualDispatch(SiteMergedView detail)
    {
        if (detail.IsIgnored)
        {
            return false;
        }

        if (detail.HasDispatchRecord && detail.DispatchStatus == DispatchStatus.Dispatched && !detail.RecoveredAt.HasValue)
        {
            return false;
        }

        if (detail.RecoveryStatus == RecoveryStatus.PendingConfirmation)
        {
            return false;
        }

        if (detail.DispatchStatus is DispatchStatus.PendingDispatch or DispatchStatus.SendFailed or DispatchStatus.WebhookNotConfigured)
        {
            return true;
        }

        if (detail.RuntimeFaultCode != RuntimeFaultCode.None)
        {
            return true;
        }

        return detail.LastInspectionRunState is InspectionRunState.Failed or InspectionRunState.SucceededWithFault;
    }

    private static string BuildCoordinateStatusText(SiteMergedView detail, DemoCoordinate? displayCoordinateOverride)
    {
        if (!detail.HasMapPoint)
        {
            return $"未落图：{detail.UnmappedReasonText}";
        }

        var detailSuffix = detail.IsPlatformCoordinateEnrichedFromDetail
            ? "，坐标已由平台详情补全"
            : string.Empty;

        return detail.CoordinateDisplayStatus switch
        {
            CoordinateDisplayStatus.RequiresMapHostConversion when displayCoordinateOverride is not null
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），已由地图宿主转换后落图{detailSuffix}",
            CoordinateDisplayStatus.RequiresMapHostConversion
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），等待地图宿主转换后落图{detailSuffix}",
            _ when detail.CoordinateSource == CoordinateSource.ManualOverride
                => "当前使用本地手工坐标（GCJ-02）落图",
            _ => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），当前按 GCJ-02 直接落图{detailSuffix}"
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

        if (detail.DisplayLongitude.HasValue && detail.DisplayLatitude.HasValue)
        {
            return new DemoCoordinate
            {
                Longitude = detail.DisplayLongitude.Value,
                Latitude = detail.DisplayLatitude.Value
            };
        }

        return null;
    }

    private static string ResolveDisplayCoordinateFallback(SiteMergedView detail)
    {
        if (detail.CoordinateDisplayStatus == CoordinateDisplayStatus.RequiresMapHostConversion)
        {
            return "等待地图转换";
        }

        return detail.HasMapPoint ? "待同步" : "暂无";
    }

    private static string ResolveCoordinateTypeLabel(string coordinateType)
    {
        return CoordinateTypeCatalog.GetDisplayLabel(coordinateType);
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

    private static string ResolveRecoveryConfirmationModeText(RecoveryConfirmationMode mode)
    {
        return mode switch
        {
            RecoveryConfirmationMode.Automatic => "自动",
            RecoveryConfirmationMode.ManualPreferred => "手工优先",
            _ => "仅手工"
        };
    }

    private static string ResolveRecoverySourceText(RecoverySource? source)
    {
        return source switch
        {
            RecoverySource.SystemDetected => "系统检测",
            RecoverySource.ManualConfirmed => "管理员确认",
            _ => "未记录"
        };
    }
}
