using System.Collections.Concurrent;
using System.Text.Json;
using TianyiVision.Acis.Reusable;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Integrations.Acis;

public sealed class AcisKernelPlatformSiteProvider :
    IPlatformSiteProvider,
    IPlatformConnectionStateProvider,
    IDisposable
{
    private const int CatalogPageSize = 20;
    private const int MaxCatalogPages = 3;
    private const int MaxCatalogItems = 60;
    private const int MaxDetailRequests = 24;
    private const int MaxDetailConcurrency = 4;
    private const string UnknownCoordinateType = "unknown";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly AcisApiKernel? kernel;
    private readonly string? configPath;
    private readonly SemaphoreSlim refreshSync = new(1, 1);
    private PlatformCacheEntry? cacheEntry;
    private PlatformConnectionState currentState;

    public AcisKernelPlatformSiteProvider(AcisKernelOptionsLoadResult loadResult)
    {
        if (loadResult is null)
        {
            throw new ArgumentNullException(nameof(loadResult));
        }

        configPath = loadResult.ConfigPath;
        kernel = loadResult.Options is null ? null : new AcisApiKernel(loadResult.Options);
        currentState = loadResult.IsReady
            ? CreateState(
                PlatformConnectionStatus.Degraded,
                "平台连接准备中",
                "等待首次目录同步。")
            : CreateState(
                PlatformConnectionStatus.NotConfigured,
                "平台未连接",
                loadResult.Issue ?? "未检测到可用的 ACIS 配置。");
    }

    public PlatformConnectionState GetCurrentState() => currentState;

    public async Task<IReadOnlyList<PlatformSiteSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (kernel is null)
        {
            return Array.Empty<PlatformSiteSnapshot>();
        }

        var now = DateTimeOffset.UtcNow;
        if (cacheEntry is not null && cacheEntry.ExpiresAt > now)
        {
            return cacheEntry.Snapshots;
        }

        await refreshSync.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (cacheEntry is not null && cacheEntry.ExpiresAt > now)
            {
                return cacheEntry.Snapshots;
            }

            try
            {
                var loadResult = await LoadSnapshotsAsync(cancellationToken);
                var summary = loadResult.Snapshots.Count == 0
                    ? "平台已连接，暂无点位"
                    : "平台已连接";
                var detail = $"配置：{Path.GetFileName(configPath ?? "acis-kernel.json")}，目录 {loadResult.CatalogCount} 条，详情补全 {loadResult.DetailCount} 条。";

                cacheEntry = new PlatformCacheEntry(loadResult.Snapshots, DateTimeOffset.UtcNow.Add(CacheLifetime));
                currentState = CreateState(PlatformConnectionStatus.Connected, summary, detail);
                return loadResult.Snapshots;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await TryWriteDiagnosticAsync("PlatformSiteProvider", $"加载 ACIS 平台设备失败：{ex.Message}", cancellationToken);

                if (cacheEntry is not null && cacheEntry.Snapshots.Count > 0)
                {
                    currentState = CreateState(
                        PlatformConnectionStatus.Degraded,
                        "平台数据暂不可用",
                        "已回退到最近一次同步快照。");
                    return cacheEntry.Snapshots;
                }

                currentState = CreateState(
                    PlatformConnectionStatus.Degraded,
                    "平台数据暂不可用",
                    "平台目录读取失败，当前未加载到可用点位。");
                return Array.Empty<PlatformSiteSnapshot>();
            }
        }
        finally
        {
            refreshSync.Release();
        }
    }

    public void Dispose()
    {
        refreshSync.Dispose();
        kernel?.Dispose();
    }

    private async Task<PlatformLoadResult> LoadSnapshotsAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var catalogItems = await LoadCatalogItemsAsync(cancellationToken);
        var candidates = catalogItems
            .Select(CreateCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.DeviceCode))
            .ToList();

        var detailMap = await LoadDeviceDetailsAsync(candidates, cancellationToken);
        var snapshots = candidates
            .Select(candidate => MergeCandidate(candidate, detailMap.TryGetValue(candidate.DeviceCode, out var detail) ? detail : null))
            .Select(record => new PlatformSiteSnapshot
            {
                DeviceCode = record.DeviceCode,
                DeviceName = record.DeviceName,
                RawLongitude = record.RawLongitude,
                RawLatitude = record.RawLatitude,
                RawCoordinateType = record.RawCoordinateType,
                DemoOnlineState = record.IsOnline == false ? DemoOnlineState.Offline : DemoOnlineState.Online,
                DemoStatus = PointDemoStatus.Normal,
                DemoDispatchStatus = DispatchDemoStatus.None
            })
            .ToList();

        return new PlatformLoadResult(snapshots, candidates.Count, detailMap.Count);
    }

    private async Task<IReadOnlyList<DeviceCatalogItem>> LoadCatalogItemsAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var results = new List<DeviceCatalogItem>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long lastId = 0;

        for (var pageIndex = 0; pageIndex < MaxCatalogPages && results.Count < MaxCatalogItems; pageIndex++)
        {
            var page = await kernel.GetDeviceCatalogPageAsync(lastId, CatalogPageSize, cancellationToken);
            if (!page.IsSuccess)
            {
                throw new InvalidOperationException($"平台目录拉取失败：{page.ResponseMessage}");
            }

            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var item in page.Items)
            {
                if (string.IsNullOrWhiteSpace(item.DeviceCode) || !seenCodes.Add(item.DeviceCode))
                {
                    continue;
                }

                results.Add(item);
                if (results.Count >= MaxCatalogItems)
                {
                    break;
                }
            }

            if (page.NextLastId <= lastId)
            {
                break;
            }

            lastId = page.NextLastId;
        }

        return results;
    }

    private async Task<IReadOnlyDictionary<string, DeviceDetailResult>> LoadDeviceDetailsAsync(
        IReadOnlyList<CatalogCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var detailCodes = candidates
            .OrderByDescending(NeedsDetail)
            .ThenBy(candidate => candidate.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.DeviceCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(MaxDetailRequests, candidates.Count))
            .ToArray();

        var details = new ConcurrentDictionary<string, DeviceDetailResult>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            detailCodes,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxDetailConcurrency
            },
            async (deviceCode, token) =>
            {
                try
                {
                    var detail = await kernel.GetDeviceDetailAsync(deviceCode, token);
                    if (detail.Snapshots.Any(snapshot => snapshot.IsSuccess))
                    {
                        details[deviceCode] = detail;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await TryWriteDiagnosticAsync("PlatformDetail", $"设备详情补拉失败：deviceCode={deviceCode}, reason={ex.Message}", token);
                }
            });

        return details;
    }

    private static CatalogCandidate CreateCandidate(DeviceCatalogItem item)
    {
        if (!TryParseJson(item.Raw, out var root))
        {
            return new CatalogCandidate(
                item.DeviceCode.Trim(),
                item.DeviceName.Trim(),
                null,
                null,
                UnknownCoordinateType,
                false);
        }

        return new CatalogCandidate(
            string.IsNullOrWhiteSpace(item.DeviceCode) ? ReadString(root, "deviceCode") ?? string.Empty : item.DeviceCode.Trim(),
            string.IsNullOrWhiteSpace(item.DeviceName) ? ReadString(root, "deviceName") ?? ReadString(root, "name") ?? string.Empty : item.DeviceName.Trim(),
            ReadDouble(root, "longitude", "lng"),
            ReadDouble(root, "latitude", "lat"),
            ResolveCoordinateType(root),
            ReadBool(root, "onlineStatus", "online", "isOnline"));
    }

    private static DevicePlatformRecord MergeCandidate(CatalogCandidate candidate, DeviceDetailResult? detail)
    {
        var detailCoordinateType = ResolveCoordinateType(detail?.RawResponse);
        var rawCoordinateType = IsKnownCoordinateType(detailCoordinateType)
            ? detailCoordinateType
            : candidate.RawCoordinateType;

        var rawLongitude = detail?.Longitude is decimal detailLongitude
            ? (double)detailLongitude
            : candidate.RawLongitude;
        var rawLatitude = detail?.Latitude is decimal detailLatitude
            ? (double)detailLatitude
            : candidate.RawLatitude;

        return new DevicePlatformRecord
        {
            DeviceCode = candidate.DeviceCode,
            DeviceName = string.IsNullOrWhiteSpace(detail?.DeviceName) ? candidate.DeviceName : detail.DeviceName.Trim(),
            RawLongitude = rawLongitude,
            RawLatitude = rawLatitude,
            RawCoordinateType = rawCoordinateType,
            IsOnline = detail?.IsOnline ?? candidate.IsOnline
        };
    }

    private static bool NeedsDetail(CatalogCandidate candidate)
    {
        return !candidate.RawLongitude.HasValue
            || !candidate.RawLatitude.HasValue
            || !candidate.IsOnline.HasValue;
    }

    private static PlatformConnectionState CreateState(
        PlatformConnectionStatus status,
        string summary,
        string? detail)
    {
        return new PlatformConnectionState
        {
            Status = status,
            SummaryText = summary,
            DetailText = detail,
            IsConfigured = status != PlatformConnectionStatus.NotConfigured,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private async Task TryWriteDiagnosticAsync(
        string category,
        string message,
        CancellationToken cancellationToken)
    {
        if (kernel is null)
        {
            return;
        }

        try
        {
            await kernel.WriteDiagnosticAsync(category, message, cancellationToken);
        }
        catch
        {
            // Best effort only. Diagnostics must never break the UI path.
        }
    }

    private static bool TryParseJson(string raw, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numberValue))
            {
                return numberValue;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), out var stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            {
                return intValue != 0;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "online", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "offline", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return null;
    }

    private static string ResolveCoordinateType(JsonElement element)
    {
        var coordinateType = ReadString(
            element,
            "coordinateType",
            "coordinateSystem",
            "coordType",
            "coordSystem",
            "mapType",
            "sourceCoordType");

        return ResolveCoordinateType(coordinateType);
    }

    private static string ResolveCoordinateType(string? raw)
    {
        if (TryParseJson(raw ?? string.Empty, out var root))
        {
            return ResolveCoordinateType(root);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return UnknownCoordinateType;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Contains("baidu", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bd09", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("百度", StringComparison.OrdinalIgnoreCase))
        {
            return "bd09";
        }

        if (normalized.Contains("gcj02", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gaode", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("amap", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("高德", StringComparison.OrdinalIgnoreCase))
        {
            return "gcj02";
        }

        if (normalized.Contains("wgs84", StringComparison.OrdinalIgnoreCase))
        {
            return "wgs84";
        }

        if (normalized.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return UnknownCoordinateType;
        }

        return normalized;
    }

    private static bool IsKnownCoordinateType(string? coordinateType)
    {
        return !string.IsNullOrWhiteSpace(coordinateType)
            && !string.Equals(coordinateType, UnknownCoordinateType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CatalogCandidate(
        string DeviceCode,
        string DeviceName,
        double? RawLongitude,
        double? RawLatitude,
        string RawCoordinateType,
        bool? IsOnline);

    private sealed class DevicePlatformRecord
    {
        public required string DeviceCode { get; init; }

        public required string DeviceName { get; init; }

        public double? RawLongitude { get; init; }

        public double? RawLatitude { get; init; }

        public required string RawCoordinateType { get; init; }

        public bool? IsOnline { get; init; }
    }

    private sealed record PlatformCacheEntry(
        IReadOnlyList<PlatformSiteSnapshot> Snapshots,
        DateTimeOffset ExpiresAt);

    private sealed record PlatformLoadResult(
        IReadOnlyList<PlatformSiteSnapshot> Snapshots,
        int CatalogCount,
        int DetailCount);
}
