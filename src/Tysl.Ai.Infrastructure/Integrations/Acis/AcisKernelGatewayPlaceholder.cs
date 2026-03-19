namespace Tysl.Ai.Infrastructure.Integrations.Acis;

public sealed class AcisKernelGatewayPlaceholder
{
    public AcisKernelProfile Profile { get; } = new(
        "ACIS Kernel Placeholder",
        "Token、平台接口、坐标转换、预览地址、宿主页、日志",
        false);
}
