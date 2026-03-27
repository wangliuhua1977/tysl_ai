namespace Tysl.Ai.Core.Models;

public sealed record NotificationTemplateRenderContext
{
    public string? DeviceCode { get; init; }

    public string? DeviceName { get; init; }

    public string? Alias { get; init; }

    public string? ProductAccessNumber { get; init; }

    public string? Status { get; init; }

    public string? FaultReason { get; init; }

    public string? DispatchTime { get; init; }

    public string? RecoveryTime { get; init; }

    public string? RecoveryMethod { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public string? Remark { get; init; }

    public string? ClosingRemark { get; init; }

    public static NotificationTemplateRenderContext CreateSample()
    {
        return new NotificationTemplateRenderContext
        {
            DeviceCode = "ACIS-DEMO-001",
            DeviceName = "市府广场西门",
            Alias = "市府广场西门",
            ProductAccessNumber = "3306020001001",
            Status = "待派单",
            FaultReason = "设备离线",
            DispatchTime = "2026-03-26 09:30:00",
            RecoveryTime = "2026-03-26 10:10:00",
            RecoveryMethod = "系统检测",
            MaintenanceUnit = "城运联保组",
            MaintainerName = "王工",
            MaintainerPhone = "13800000001",
            Remark = "请优先现场核查供电与网络。",
            ClosingRemark = "现场复核后已恢复正常。"
        };
    }
}
