using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteDetailViewModel
{
    private SiteDetailViewModel(SiteDetailSnapshot detail)
    {
        Id = detail.Id;
        DeviceCode = detail.DeviceCode;
        DeviceName = detail.DeviceName;
        DisplayName = detail.DisplayName;
        Alias = string.IsNullOrWhiteSpace(detail.Alias) ? "未设置别名" : detail.Alias!;
        Remark = string.IsNullOrWhiteSpace(detail.Remark) ? "暂无备注信息。" : detail.Remark!;
        IsMonitored = detail.IsMonitored;
        MonitoringText = detail.IsMonitored ? "纳入监测" : "未纳入监测";
        LongitudeText = detail.Longitude.ToString("F6");
        LatitudeText = detail.Latitude.ToString("F6");
        AddressText = string.IsNullOrWhiteSpace(detail.AddressText) ? "地址待补充" : detail.AddressText!;
        ProductAccessNumber = string.IsNullOrWhiteSpace(detail.ProductAccessNumber) ? "未配置" : detail.ProductAccessNumber!;
        MaintenanceUnit = string.IsNullOrWhiteSpace(detail.MaintenanceUnit) ? "维护单位待补充" : detail.MaintenanceUnit!;
        MaintainerName = string.IsNullOrWhiteSpace(detail.MaintainerName) ? "维护人员待补充" : detail.MaintainerName!;
        MaintainerPhone = string.IsNullOrWhiteSpace(detail.MaintainerPhone) ? "联系电话待补充" : detail.MaintainerPhone!;
        DemoStatus = detail.DemoStatus;
        DemoDispatchStatus = detail.DemoDispatchStatus;
        VisualState = detail.VisualState;
        StatusText = detail.StatusText;
        UpdatedAtText = detail.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public Guid Id { get; }

    public string DeviceCode { get; }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public string Alias { get; }

    public string Remark { get; }

    public bool IsMonitored { get; }

    public string MonitoringText { get; }

    public string LongitudeText { get; }

    public string LatitudeText { get; }

    public string AddressText { get; }

    public string ProductAccessNumber { get; }

    public string MaintenanceUnit { get; }

    public string MaintainerName { get; }

    public string MaintainerPhone { get; }

    public PointDemoStatus DemoStatus { get; }

    public DispatchDemoStatus DemoDispatchStatus { get; }

    public SiteVisualState VisualState { get; }

    public string StatusText { get; }

    public string UpdatedAtText { get; }

    public static SiteDetailViewModel FromSnapshot(SiteDetailSnapshot detail)
    {
        return new SiteDetailViewModel(detail);
    }
}
