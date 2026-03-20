using System.Text.Json;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Configuration;

public sealed class DispatchOptionsProvider
{
    private const string ConfigRelativePath = "configs\\dispatch.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DispatchOptionsLoadResult Load()
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            return new DispatchOptionsLoadResult(
                null,
                null,
                false,
                "未检测到派单配置，应用将以可运行但未配置的状态启动。");
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var options = JsonSerializer.Deserialize<DispatchOptions>(json, JsonOptions) ?? new DispatchOptions();

            var policy = DispatchPolicy.Default with
            {
                Mode = ParseMode(options.Mode),
                CoolingMinutes = options.CoolingMinutes > 0
                    ? options.CoolingMinutes
                    : DispatchPolicy.Default.CoolingMinutes,
                RecoveryMode = ParseRecoveryMode(options.RecoveryMode),
                NotifyOnRecovery = options.NotifyOnRecovery,
                WebhookUrl = NormalizeText(options.WebhookUrl),
                MentionMobiles = NormalizeMentionMobiles(options.MentionMobiles),
                MentionAll = options.MentionAll,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return new DispatchOptionsLoadResult(
                policy,
                configPath,
                true,
                null);
        }
        catch
        {
            return new DispatchOptionsLoadResult(
                null,
                configPath,
                false,
                "派单配置读取失败，应用将以可运行但未配置的状态启动。");
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

    private static DispatchMode ParseMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "manual" => DispatchMode.Manual,
            _ => DispatchMode.Automatic
        };
    }

    private static RecoveryMode ParseRecoveryMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "automatic" => RecoveryMode.Automatic,
            _ => RecoveryMode.Manual
        };
    }

    private static IReadOnlyList<string> NormalizeMentionMobiles(IReadOnlyList<string>? values)
    {
        return values?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record DispatchOptions
{
    public string? WebhookUrl { get; init; }

    public string? Mode { get; init; } = "automatic";

    public int CoolingMinutes { get; init; } = 30;

    public string? RecoveryMode { get; init; } = "manual";

    public bool NotifyOnRecovery { get; init; } = true;

    public IReadOnlyList<string>? MentionMobiles { get; init; } = Array.Empty<string>();

    public bool MentionAll { get; init; }
}

public sealed record DispatchOptionsLoadResult(
    DispatchPolicy? InitialPolicy,
    string? ConfigPath,
    bool HasConfig,
    string? Issue);
