using Tysl.Ai.Core.Abstractions;
using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record MonitoringPoint : IEntity
{
    public required Guid Id { get; init; }

    public required string Alias { get; init; }

    public required string DeviceName { get; init; }

    public required string RegionName { get; init; }

    public required string MaintenanceUnit { get; init; }

    public required string Maintainer { get; init; }

    public required string ScreenshotHint { get; init; }

    public required string MaintenanceNote { get; init; }

    public required PointStatus Status { get; init; }

    public required bool IsOnline { get; init; }

    public string StatusText => Status switch
    {
        PointStatus.Monitoring => "已监测",
        PointStatus.Normal => "在线",
        PointStatus.Alert => "异常",
        PointStatus.Dispatched => "已派单",
        PointStatus.Offline => "离线",
        _ => "未知"
    };
}
