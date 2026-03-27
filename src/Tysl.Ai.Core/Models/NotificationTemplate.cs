using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record NotificationTemplate
{
    public required NotificationTemplateKind Kind { get; init; }

    public required string Content { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public static NotificationTemplate CreateDefault(NotificationTemplateKind kind)
    {
        return new NotificationTemplate
        {
            Kind = kind,
            Content = kind switch
            {
                NotificationTemplateKind.Dispatch =>
                    """
                    # 派单通知
                    - 点位：{alias}
                    - 设备编码：{deviceCode}
                    - 设备名称：{deviceName}
                    - 产品接入号：{productAccessNumber}
                    - 当前状态：{status}
                    - 异常原因：{faultReason}
                    - 派单时间：{dispatchTime}
                    - 维护单位：{maintenanceUnit}
                    - 维护人：{maintainerName}
                    - 联系电话：{maintainerPhone}
                    - 现场备注：{remark}
                    """,
                NotificationTemplateKind.Recovery =>
                    """
                    # 恢复通知
                    - 点位：{alias}
                    - 设备编码：{deviceCode}
                    - 设备名称：{deviceName}
                    - 产品接入号：{productAccessNumber}
                    - 当前状态：{status}
                    - 恢复时间：{recoveryTime}
                    - 恢复方式：{recoveryMethod}
                    - 最近异常原因：{faultReason}
                    - 维护单位：{maintenanceUnit}
                    - 维护人：{maintainerName}
                    - 联系电话：{maintainerPhone}
                    - 处理结论 / 备注：{closingRemark}
                    """,
                _ => "{deviceCode}"
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
