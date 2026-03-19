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
        Remark = string.IsNullOrWhiteSpace(detail.Remark) ? "暂无补充说明。" : detail.Remark;
        IsMonitored = detail.IsMonitored;
        MonitoringText = detail.IsMonitored ? "已纳入监测" : "未纳入监测";
        OnlineStateText = detail.DemoOnlineState switch
        {
            DemoOnlineState.Online => "在线",
            DemoOnlineState.Offline => "离线",
            _ => "未知"
        };
        CoordinateSourceText = detail.CoordinateSourceText;
        CoordinateStatusText = BuildCoordinateStatusText(detail, displayCoordinateOverride);
        PlatformStatusSummary = detail.PlatformStatusSummary;

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
        ProductAccessNumber = string.IsNullOrWhiteSpace(detail.ProductAccessNumber) ? "未配置" : detail.ProductAccessNumber;
        MaintenanceUnit = string.IsNullOrWhiteSpace(detail.MaintenanceUnit) ? "维护单位待补充" : detail.MaintenanceUnit;
        MaintainerName = string.IsNullOrWhiteSpace(detail.MaintainerName) ? "维护人待补充" : detail.MaintainerName;
        MaintainerPhone = string.IsNullOrWhiteSpace(detail.MaintainerPhone) ? "联系电话待补充" : detail.MaintainerPhone;
        LocalProfileStatusText = detail.HasLocalProfile ? "已保存本地补充信息" : "尚未保存本地补充信息";
        VisualState = detail.VisualState;
        StatusText = detail.StatusText;
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

    private static string BuildCoordinateStatusText(SiteMergedView detail, DemoCoordinate? displayCoordinateOverride)
    {
        return detail.CoordinateSource switch
        {
            CoordinateSource.PlatformRaw when displayCoordinateOverride is not null
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），已由前端地图宿主转换后显示",
            CoordinateSource.PlatformRaw when RequiresFrontendConversion(detail.PlatformRawCoordinateType)
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），待前端地图宿主转换",
            CoordinateSource.PlatformRaw
                => $"平台原始坐标（{ResolveCoordinateTypeLabel(detail.PlatformRawCoordinateType)}），当前按 GCJ-02 直接显示",
            CoordinateSource.ManualOverride => "当前使用本地手工坐标（GCJ-02）",
            _ => "当前暂无可展示坐标"
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
            return "待地图转换";
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
}
