using System.Text.Json;

namespace Tysl.Ai.Infrastructure.Configuration;

public sealed class AmapJsOptionsProvider
{
    private const string ConfigRelativePath = "configs\\amap-js.json";

    public AmapJsOptionsLoadResult Load()
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            return new AmapJsOptionsLoadResult(
                null,
                null,
                false,
                "地图未配置");
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var options = JsonSerializer.Deserialize<AmapJsOptions>(json, JsonSerializerOptions) ?? new AmapJsOptions();

            if (string.IsNullOrWhiteSpace(options.Key) || string.IsNullOrWhiteSpace(options.SecurityJsCode))
            {
                return new AmapJsOptionsLoadResult(
                    options,
                    configPath,
                    false,
                    "地图未配置");
            }

            var normalized = options with
            {
                Zoom = options.Zoom is > 0 and <= 20 ? options.Zoom : 11,
                Center = NormalizeCenter(options.Center)
            };

            return new AmapJsOptionsLoadResult(
                normalized,
                configPath,
                true,
                null);
        }
        catch
        {
            return new AmapJsOptionsLoadResult(
                null,
                configPath,
                false,
                "地图未配置");
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

    private static double[] NormalizeCenter(double[]? value)
    {
        if (value is [var longitude, var latitude])
        {
            return
            [
                Math.Round(longitude, 6),
                Math.Round(latitude, 6)
            ];
        }

        return [120.585316, 30.028105];
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record AmapJsOptions
{
    public string? Key { get; init; }

    public string? SecurityJsCode { get; init; }

    public string? MapStyle { get; init; }

    public int Zoom { get; init; } = 11;

    public double[] Center { get; init; } = [120.585316, 30.028105];
}

public sealed record AmapJsOptionsLoadResult(
    AmapJsOptions? Options,
    string? ConfigPath,
    bool IsReady,
    string? Issue);
