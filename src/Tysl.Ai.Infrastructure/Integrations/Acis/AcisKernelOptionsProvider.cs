using TianyiVision.Acis.Reusable;

namespace Tysl.Ai.Infrastructure.Integrations.Acis;

public sealed class AcisKernelOptionsProvider
{
    private const string ConfigRelativePath = "configs\\acis-kernel.json";

    public AcisKernelOptionsLoadResult Load()
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            return new AcisKernelOptionsLoadResult(
                null,
                null,
                false,
                "未找到 ACIS 配置文件。应用将以受控降级模式运行。");
        }

        try
        {
            var options = AcisApiKernel.LoadOptions(configPath);
            if (!IsComplete(options))
            {
                return new AcisKernelOptionsLoadResult(
                    null,
                    configPath,
                    false,
                    "ACIS 配置不完整。应用将以受控降级模式运行。");
            }

            return new AcisKernelOptionsLoadResult(
                options,
                configPath,
                true,
                null);
        }
        catch (Exception ex)
        {
            return new AcisKernelOptionsLoadResult(
                null,
                configPath,
                false,
                $"ACIS 配置读取失败：{ex.Message}");
        }
    }

    private static string? FindConfigPath()
    {
        foreach (var root in GetSearchRoots())
        {
            var current = root;
            while (!string.IsNullOrWhiteSpace(current))
            {
                var candidate = Path.Combine(current, ConfigRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        var roots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsComplete(AcisKernelOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Ctyun.BaseUrl)
            && !string.IsNullOrWhiteSpace(options.Ctyun.AppId)
            && !string.IsNullOrWhiteSpace(options.Ctyun.AppSecret)
            && !string.IsNullOrWhiteSpace(options.Ctyun.EnterpriseUser)
            && !string.IsNullOrWhiteSpace(options.Ctyun.RsaPrivateKeyPem);
    }
}

public sealed record AcisKernelOptionsLoadResult(
    AcisKernelOptions? Options,
    string? ConfigPath,
    bool IsReady,
    string? Issue);
