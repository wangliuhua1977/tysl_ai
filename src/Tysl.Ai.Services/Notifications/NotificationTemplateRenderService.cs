using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Notifications;

public sealed class NotificationTemplateRenderService : INotificationTemplateRenderService
{
    private static readonly IReadOnlyDictionary<string, string> SupportedVariables =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{deviceCode}"] = "设备编码",
            ["{deviceName}"] = "设备名称",
            ["{alias}"] = "点位别名",
            ["{productAccessNumber}"] = "产品接入号",
            ["{status}"] = "当前状态",
            ["{faultReason}"] = "最近异常原因",
            ["{dispatchTime}"] = "派单时间",
            ["{recoveryTime}"] = "恢复时间",
            ["{recoveryMethod}"] = "恢复方式",
            ["{maintenanceUnit}"] = "维护单位",
            ["{maintainerName}"] = "维护人姓名",
            ["{maintainerPhone}"] = "维护人电话",
            ["{remark}"] = "备注",
            ["{closingRemark}"] = "处理结论或归档说明"
        };

    public IReadOnlyDictionary<string, string> GetSupportedVariables()
    {
        return SupportedVariables;
    }

    public string Render(string templateContent, NotificationTemplateRenderContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateContent);
        ArgumentNullException.ThrowIfNull(context);

        var rendered = templateContent;
        foreach (var pair in BuildValueMap(context))
        {
            rendered = rendered.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static IReadOnlyDictionary<string, string> BuildValueMap(NotificationTemplateRenderContext context)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{deviceCode}"] = NormalizeValue(context.DeviceCode, "未提供"),
            ["{deviceName}"] = NormalizeValue(context.DeviceName, "未提供"),
            ["{alias}"] = NormalizeValue(context.Alias ?? context.DeviceName ?? context.DeviceCode, "未提供"),
            ["{productAccessNumber}"] = NormalizeValue(context.ProductAccessNumber, "未填写"),
            ["{status}"] = NormalizeValue(context.Status, "未提供"),
            ["{faultReason}"] = NormalizeValue(context.FaultReason, "未提供"),
            ["{dispatchTime}"] = NormalizeValue(context.DispatchTime, "未提供"),
            ["{recoveryTime}"] = NormalizeValue(context.RecoveryTime, "未提供"),
            ["{recoveryMethod}"] = NormalizeValue(context.RecoveryMethod, "未提供"),
            ["{maintenanceUnit}"] = NormalizeValue(context.MaintenanceUnit, "未填写"),
            ["{maintainerName}"] = NormalizeValue(context.MaintainerName, "未填写"),
            ["{maintainerPhone}"] = NormalizeValue(context.MaintainerPhone, "未填写"),
            ["{remark}"] = NormalizeValue(context.Remark, "未填写"),
            ["{closingRemark}"] = NormalizeValue(context.ClosingRemark ?? context.Remark, "未填写")
        };
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
