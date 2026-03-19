using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

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
        MapX = point.MapX;
        MapY = point.MapY;
        CoordinateSourceText = point.CoordinateSourceText;
        AddressText = string.IsNullOrWhiteSpace(point.AddressText) ? "地址待补充" : point.AddressText!;

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

    public double MapX { get; }

    public double MapY { get; }

    public string CoordinateSourceText { get; }

    public string AddressText { get; }

    public string MaintenanceLine { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
