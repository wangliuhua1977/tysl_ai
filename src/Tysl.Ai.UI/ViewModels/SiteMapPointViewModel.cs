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
        StatusText = point.StatusText;
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
        MonitoringText = point.IsMonitored ? "已纳入监测" : "未纳入监测";
        SummaryText = BuildSummaryText(point);

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
            PlatformRawLongitude = PlatformRawLongitude,
            PlatformRawLatitude = PlatformRawLatitude,
            RawCoordinateType = RawCoordinateType,
            ManualLongitude = ManualLongitude,
            ManualLatitude = ManualLatitude
        };
    }

    private static string BuildSummaryText(SiteMapPoint point)
    {
        if (!point.IsMonitored)
        {
            return "当前未纳入监测";
        }

        return point.CoordinatePayload.CoordinateSource switch
        {
            CoordinateSource.ManualOverride => "当前使用本地手工坐标",
            CoordinateSource.PlatformRaw => "当前使用平台原始坐标",
            _ => "当前暂无可用坐标"
        };
    }
}
