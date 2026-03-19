using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class SiteMapPointViewModel : ObservableObject
{
    private bool isSelected;

    public SiteMapPointViewModel(SiteMapPoint point)
    {
        Id = point.Id;
        DeviceCode = point.DeviceCode;
        DeviceName = point.DeviceName;
        DisplayName = point.DisplayName;
        StatusText = point.StatusText;
        VisualState = point.VisualState;
        IsMonitored = point.IsMonitored;
        MapX = point.MapX;
        MapY = point.MapY;
        AddressText = string.IsNullOrWhiteSpace(point.AddressText) ? "地址待补充" : point.AddressText!;
        MaintenanceLine = string.IsNullOrWhiteSpace(point.MaintainerName)
            ? "维护信息待补充"
            : $"维护：{point.MaintainerName}";
    }

    public Guid Id { get; }

    public string DeviceCode { get; }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public string StatusText { get; }

    public SiteVisualState VisualState { get; }

    public bool IsMonitored { get; }

    public double MapX { get; }

    public double MapY { get; }

    public string AddressText { get; }

    public string MaintenanceLine { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
