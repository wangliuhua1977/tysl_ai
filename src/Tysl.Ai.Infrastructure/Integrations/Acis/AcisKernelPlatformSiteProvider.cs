using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TianyiVision.Acis.Reusable;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Integrations.Acis;

public sealed class AcisKernelPlatformSiteProvider :
    IPlatformSiteProvider,
    IPlatformConnectionStateProvider,
    IPlatformPreviewProvider,
    IDisposable
{
    private const int CatalogPageSize = 50;
    private const int MaxCatalogPages = 12;
    private const int MaxCatalogItems = 500;
    private const int MaxCoordinateDetailRequests = 24;
    private const int MaxMetadataDetailRequests = 8;
    private const int MaxDetailRequests = 28;
    private const int MaxDetailConcurrency = 3;
    private const string DefaultPlatformCoordinateType = "bd09";
    private const string UnknownCoordinateType = "unknown";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DetailCacheLifetime = TimeSpan.FromMinutes(3);
    private static readonly HashSet<string> ForwardedPreviewRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Accept-Language",
        "Cache-Control",
        "Pragma",
        "Range",
        "User-Agent"
    };
    private static readonly HashSet<string> IgnoredPreviewResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Access-Control-Allow-Credentials",
        "Access-Control-Allow-Headers",
        "Access-Control-Allow-Methods",
        "Access-Control-Allow-Origin",
        "Connection",
        "Content-Encoding",
        "Content-Length",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "Set-Cookie",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    private readonly AcisApiKernel? kernel;
    private readonly string? configPath;
    private readonly ConcurrentDictionary<string, DetailCacheEntry> detailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient previewProxyHttpClient;
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
        previewProxyHttpClient = new HttpClient(
            new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
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

    public bool IsReady => kernel is not null;

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
                var detail = $"配置：{Path.GetFileName(configPath ?? "acis-kernel.json")}，目录 {loadResult.CatalogCount} 条 / {loadResult.CatalogPageCount} 页，详情补全 {loadResult.DetailCount} 条，当前选中 {loadResult.FinalSelectedCount} 条。";

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
        previewProxyHttpClient.Dispose();
        kernel?.Dispose();
    }

    public Task<PreviewResolution> ResolveInspectionPreviewAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (kernel is null)
        {
            return Task.FromResult(
                PreviewResolution.Failure(
                    deviceCode,
                    string.Empty,
                    Array.Empty<string>(),
                    string.Empty,
                    -1,
                    "ACIS configuration unavailable.",
                    "preview unavailable",
                    PreviewFailureCategory.ProtocolFallbackStillFailed,
                    "ACIS configuration unavailable."));
        }

        return kernel.ResolvePreviewAsync(deviceCode, AcisPreviewIntent.Inspection, cancellationToken: cancellationToken);
    }

    public async Task<SitePreviewResolveResult> ResolvePreviewAsync(
        string deviceCode,
        IReadOnlyList<SitePreviewProtocol> protocolOrder,
        CancellationToken cancellationToken = default)
    {
        if (kernel is null)
        {
            return new SitePreviewResolveResult
            {
                IsSuccess = false,
                AttemptedProtocols = protocolOrder.ToArray(),
                FailureReason = "预览服务未配置。"
            };
        }

        var resolution = await kernel.ResolvePreviewAsync(
            deviceCode,
            AcisPreviewIntent.ClickPreview,
            protocolOrder.Select(ToProtocolKey).ToArray(),
            cancellationToken);

        if (!resolution.IsSuccess || string.IsNullOrWhiteSpace(resolution.PreviewUrl))
        {
            return new SitePreviewResolveResult
            {
                IsSuccess = false,
                AttemptedProtocols = resolution.AttemptedProtocols.Select(ToPreviewProtocol).ToArray(),
                FailureReason = resolution.FailureReason
            };
        }

        var selectedProtocol = ToPreviewProtocol(resolution.ParsedProtocolType ?? resolution.SelectedProtocol);
        var attemptedProtocols = resolution.AttemptedProtocols.Select(ToPreviewProtocol).ToArray();
        var webRtcUrlAcquired = selectedProtocol == SitePreviewProtocol.WebRtc
                                && !string.IsNullOrWhiteSpace(resolution.PreviewUrl);

        if (webRtcUrlAcquired)
        {
            await TryWriteDiagnosticAsync(
                "Preview",
                $"WebRTC URL acquired: deviceCode={resolution.DeviceCode}, attempted={string.Join(">", resolution.AttemptedProtocols)}, url={resolution.PreviewUrl}",
                cancellationToken);
        }

        return new SitePreviewResolveResult
        {
            IsSuccess = true,
            AttemptedProtocols = attemptedProtocols,
            Session = new SitePreviewSession
            {
                PlaybackSessionId = Guid.NewGuid().ToString("N"),
                DeviceCode = resolution.DeviceCode,
                SelectedProtocol = selectedProtocol,
                AttemptedProtocols = attemptedProtocols,
                SourceUrl = resolution.PreviewUrl,
                WebRtcApiUrl = selectedProtocol == SitePreviewProtocol.WebRtc
                    ? AcisApiKernel.BuildWebRtcPlayApiUrl(resolution.PreviewUrl)
                    : null,
                WebRtcUrlAcquired = webRtcUrlAcquired,
                ReadyTimeoutSeconds = selectedProtocol == SitePreviewProtocol.WebRtc ? 12 : 10,
                UsedFallback = attemptedProtocols.Length > 0 && selectedProtocol != attemptedProtocols[0]
            }
        };
    }

    public async Task<WebRtcPlaybackNegotiationResult> NegotiateWebRtcAsync(
        string deviceCode,
        string apiUrl,
        string streamUrl,
        string offerSdp,
        CancellationToken cancellationToken = default)
    {
        if (kernel is null)
        {
            return new WebRtcPlaybackNegotiationResult
            {
                IsSuccess = false,
                ApiUrl = apiUrl,
                ResponseCode = -1,
                FailureReason = "预览服务未配置。"
            };
        }

        var result = await kernel.NegotiateWebRtcPlayAsync(apiUrl, streamUrl, offerSdp, cancellationToken);
        return new WebRtcPlaybackNegotiationResult
        {
            IsSuccess = result.IsSuccess,
            ApiUrl = result.ApiUrl,
            AnswerSdp = result.AnswerSdp,
            ResponseCode = result.ResponseCode,
            SessionId = result.SessionId,
            Server = result.Server,
            FailureReason = result.IsSuccess ? null : result.FailureReason
        };
    }

    public async Task<PreviewProxyResourceResult> FetchPreviewResourceAsync(
        PreviewProxyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (kernel is null)
        {
            return new PreviewProxyResourceResult
            {
                IsSuccess = false,
                RequestUrl = request.RequestUrl,
                StatusCode = 503,
                ReasonPhrase = "Service Unavailable",
                FailureReason = "预览服务未配置。"
            };
        }

        if (!Uri.TryCreate(request.RequestUrl, UriKind.Absolute, out var requestUri)
            || requestUri.Scheme is not "http" and not "https")
        {
            return new PreviewProxyResourceResult
            {
                IsSuccess = false,
                RequestUrl = request.RequestUrl,
                StatusCode = 400,
                ReasonPhrase = "Bad Request",
                FailureReason = "预览代理仅支持 HTTP/HTTPS。"
            };
        }

        using var proxyRequest = new HttpRequestMessage(
            new HttpMethod(string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.Trim()),
            requestUri);

        var forwardedHeaderCount = 0;
        foreach (var header in request.Headers)
        {
            if (!ForwardedPreviewRequestHeaders.Contains(header.Key)
                || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            if (proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                forwardedHeaderCount++;
            }
        }

        if (!proxyRequest.Headers.Accept.Any())
        {
            proxyRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        if (proxyRequest.Headers.UserAgent.Count == 0)
        {
            proxyRequest.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Edg/136.0.0.0 Safari/537.36");
        }

        try
        {
            using var response = await previewProxyHttpClient.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var content = response.Content is null
                ? []
                : await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = BuildPreviewProxyResponseHeaders(response);

            return new PreviewProxyResourceResult
            {
                IsSuccess = response.IsSuccessStatusCode,
                RequestUrl = requestUri.AbsoluteUri,
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                    ? response.StatusCode.ToString()
                    : response.ReasonPhrase!,
                ContentType = response.Content?.Headers.ContentType?.ToString(),
                Content = content,
                Headers = headers,
                FailureReason = response.IsSuccessStatusCode
                    ? null
                    : $"status={(int)response.StatusCode}, forwardedHeaders={forwardedHeaderCount}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryWriteDiagnosticAsync(
                "PreviewProxy",
                $"requestUrl={requestUri.AbsoluteUri}, method={proxyRequest.Method.Method}, reason={ex.Message}",
                cancellationToken);

            return new PreviewProxyResourceResult
            {
                IsSuccess = false,
                RequestUrl = requestUri.AbsoluteUri,
                StatusCode = 502,
                ReasonPhrase = "Bad Gateway",
                FailureReason = ex.Message
            };
        }
    }

    public Task WriteDiagnosticAsync(
        string category,
        string message,
        CancellationToken cancellationToken = default)
    {
        return TryWriteDiagnosticAsync(category, message, cancellationToken);
    }

    private async Task<PlatformLoadResult> LoadSnapshotsAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var catalogLoad = await LoadCatalogItemsAsync(cancellationToken);
        var candidates = catalogLoad.Items
            .Select(CreateCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.DeviceCode))
            .ToList();

        var detailLoad = await LoadDeviceDetailsAsync(candidates, cancellationToken);
        var snapshots = candidates
            .Select(candidate => MergeCandidate(candidate, detailLoad.Details.TryGetValue(candidate.DeviceCode, out var detail) ? detail : null))
            .Select(record => new PlatformSiteSnapshot
            {
                DeviceCode = record.DeviceCode,
                DeviceName = record.DeviceName,
                RawLongitude = record.RawLongitude,
                RawLatitude = record.RawLatitude,
                RawCoordinateType = record.RawCoordinateType,
                IsCoordinateEnrichedFromDetail = record.IsCoordinateEnrichedFromDetail,
                DemoOnlineState = record.IsOnline == false ? DemoOnlineState.Offline : DemoOnlineState.Online,
                DemoStatus = PointDemoStatus.Normal,
                DemoDispatchStatus = DispatchDemoStatus.None
            })
            .ToList();

        await TryWriteDiagnosticAsync(
            "PlatformSiteProvider",
            $"catalogCount={catalogLoad.Items.Count}, catalogPageCount={catalogLoad.PageCount}, detailCandidateCount={detailLoad.SelectedCount}, detailCount={detailLoad.Details.Count}, finalSelectedCount={snapshots.Count}",
            cancellationToken);

        return new PlatformLoadResult(
            snapshots,
            catalogLoad.Items.Count,
            catalogLoad.PageCount,
            detailLoad.Details.Count,
            snapshots.Count);
    }

    private async Task<CatalogLoadResult> LoadCatalogItemsAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var results = new List<DeviceCatalogItem>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long lastId = 0;
        var pageCount = 0;
        var totalHint = 0;

        for (var pageIndex = 0; pageIndex < MaxCatalogPages && results.Count < MaxCatalogItems; pageIndex++)
        {
            var page = await kernel.GetDeviceCatalogPageAsync(lastId, CatalogPageSize, cancellationToken);
            if (!page.IsSuccess)
            {
                throw new InvalidOperationException($"平台目录拉取失败：{page.ResponseMessage}");
            }

            pageCount++;
            totalHint = Math.Max(totalHint, page.Total);

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

            if (totalHint > 0 && results.Count >= totalHint)
            {
                break;
            }

            if (page.NextLastId <= lastId)
            {
                break;
            }

            lastId = page.NextLastId;
        }

        return new CatalogLoadResult(results, pageCount, totalHint);
    }

    private async Task<DetailLoadResult> LoadDeviceDetailsAsync(
        IReadOnlyList<CatalogCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var uniqueCandidates = candidates
            .GroupBy(candidate => candidate.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var coordinateCandidates = uniqueCandidates
            .Where(NeedsCoordinateDetail)
            .OrderBy(GetCoordinateDetailPriority)
            .ThenBy(candidate => candidate.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCoordinateDetailRequests);

        var metadataCandidates = uniqueCandidates
            .Where(candidate => !NeedsCoordinateDetail(candidate) && NeedsMetadataDetail(candidate))
            .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.DeviceName) ? 0 : 1)
            .ThenBy(candidate => candidate.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Take(MaxMetadataDetailRequests);

        var detailCandidates = coordinateCandidates
            .Concat(metadataCandidates)
            .GroupBy(candidate => candidate.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Min(MaxDetailRequests, uniqueCandidates.Length))
            .ToArray();

        var details = new ConcurrentDictionary<string, DeviceDetailResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in detailCandidates)
        {
            if (TryGetCachedDetail(candidate.DeviceCode, out var cachedDetail))
            {
                details[candidate.DeviceCode] = cachedDetail;
            }
        }

        var uncachedCandidates = detailCandidates
            .Where(candidate => !details.ContainsKey(candidate.DeviceCode))
            .ToArray();

        await Parallel.ForEachAsync(
            uncachedCandidates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxDetailConcurrency
            },
            async (candidate, token) =>
            {
                try
                {
                    var detail = await kernel.GetDeviceDetailAsync(candidate.DeviceCode, token);
                    if (detail.Snapshots.Any(snapshot => snapshot.IsSuccess))
                    {
                        details[candidate.DeviceCode] = detail;
                        detailCache[candidate.DeviceCode] = new DetailCacheEntry(
                            detail,
                            DateTimeOffset.UtcNow.Add(DetailCacheLifetime));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await TryWriteDiagnosticAsync("PlatformDetail", $"设备详情补拉失败：deviceCode={candidate.DeviceCode}, reason={ex.Message}", token);
                }
            });

        return new DetailLoadResult(details, detailCandidates.Length);
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
                false,
                null);
        }

        var projection = ExtractProjection(root, item.DeviceCode, item.DeviceName);

        return new CatalogCandidate(
            projection.DeviceCode,
            projection.DeviceName,
            projection.Longitude,
            projection.Latitude,
            projection.RawCoordinateType,
            projection.IsCoordinateTypeExplicit,
            projection.IsOnline);
    }

    private static DevicePlatformRecord MergeCandidate(CatalogCandidate candidate, DeviceDetailResult? detail)
    {
        var detailProjection = detail is null ? null : BuildDetailProjection(detail);
        var detailHasCompleteCoordinate = HasCompleteCoordinate(detailProjection?.Longitude, detailProjection?.Latitude);
        var catalogHasCompleteCoordinate = HasCompleteCoordinate(candidate.RawLongitude, candidate.RawLatitude);
        var rawLongitude = SelectCoordinateValue(candidate.RawLongitude, candidate.RawLatitude, detailProjection?.Longitude, detailProjection?.Latitude, isLongitude: true);
        var rawLatitude = SelectCoordinateValue(candidate.RawLongitude, candidate.RawLatitude, detailProjection?.Longitude, detailProjection?.Latitude, isLongitude: false);
        var rawCoordinateType = ResolveFinalCoordinateType(candidate, detailProjection, rawLongitude, rawLatitude);
        var isCoordinateEnrichedFromDetail = detailHasCompleteCoordinate
            && (!catalogHasCompleteCoordinate || CoordinatesDiffer(candidate.RawLongitude, candidate.RawLatitude, detailProjection!.Longitude, detailProjection.Latitude));

        return new DevicePlatformRecord
        {
            DeviceCode = candidate.DeviceCode,
            DeviceName = PreferDetailName(candidate.DeviceName, detailProjection?.DeviceName),
            RawLongitude = rawLongitude,
            RawLatitude = rawLatitude,
            RawCoordinateType = rawCoordinateType,
            IsCoordinateEnrichedFromDetail = isCoordinateEnrichedFromDetail,
            IsOnline = detailProjection?.IsOnline ?? candidate.IsOnline
        };
    }

    private static bool NeedsCoordinateDetail(CatalogCandidate candidate)
    {
        return !candidate.RawLongitude.HasValue
            || !candidate.RawLatitude.HasValue
            || !candidate.IsCoordinateTypeExplicit
            || !IsKnownCoordinateType(candidate.RawCoordinateType);
    }

    private static bool NeedsMetadataDetail(CatalogCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.DeviceName)
            || !candidate.IsOnline.HasValue;
    }

    private static int GetCoordinateDetailPriority(CatalogCandidate candidate)
    {
        if (!candidate.RawLongitude.HasValue && !candidate.RawLatitude.HasValue)
        {
            return 0;
        }

        if (!candidate.RawLongitude.HasValue || !candidate.RawLatitude.HasValue)
        {
            return 1;
        }

        if (!candidate.IsCoordinateTypeExplicit || !IsKnownCoordinateType(candidate.RawCoordinateType))
        {
            return 2;
        }

        if (string.IsNullOrWhiteSpace(candidate.DeviceName))
        {
            return 3;
        }

        return !candidate.IsOnline.HasValue ? 4 : 5;
    }

    private bool TryGetCachedDetail(string deviceCode, out DeviceDetailResult detail)
    {
        detail = default!;
        if (!detailCache.TryGetValue(deviceCode, out var cacheEntry))
        {
            return false;
        }

        if (cacheEntry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            detailCache.TryRemove(deviceCode, out _);
            return false;
        }

        detail = cacheEntry.Detail;
        return true;
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

    private static IReadOnlyDictionary<string, string> BuildPreviewProxyResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
        {
            if (IgnoredPreviewResponseHeaders.Contains(header.Key))
            {
                continue;
            }

            headers[header.Key] = string.Join("; ", header.Value);
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                if (IgnoredPreviewResponseHeaders.Contains(header.Key))
                {
                    continue;
                }

                headers[header.Key] = string.Join("; ", header.Value);
            }
        }

        return headers;
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
            if (!TryFindPropertyValue(element, propertyName, out var value))
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
            if (!TryFindPropertyValue(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numberValue))
            {
                return numberValue;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var stringValue))
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
            if (!TryFindPropertyValue(element, propertyName, out var value))
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
        return TryResolveCoordinateType(element, out var coordinateType)
            ? coordinateType
            : UnknownCoordinateType;
    }

    private static string ResolveCoordinateType(string? raw)
    {
        return TryResolveCoordinateType(raw, out var coordinateType)
            ? coordinateType
            : UnknownCoordinateType;
    }

    private static bool TryResolveCoordinateType(JsonElement element, out string coordinateType)
    {
        var coordinateTypeValue = ReadString(
            element,
            "coordinateType",
            "coordinateSystem",
            "coordType",
            "coordSystem",
            "mapType",
            "sourceCoordType",
            "coordProvider",
            "coordinateMode",
            "coordinateFlag",
            "coordSource",
            "locationCoordType");

        if (TryResolveCoordinateType(coordinateTypeValue, out coordinateType))
        {
            return true;
        }

        return TryResolveCoordinateTypeFromText(element.GetRawText(), out coordinateType);
    }

    private static bool TryResolveCoordinateType(string? raw, out string coordinateType)
    {
        if (TryParseJson(raw ?? string.Empty, out var root) && TryResolveCoordinateType(root, out coordinateType))
        {
            return true;
        }

        return TryResolveCoordinateTypeFromText(raw, out coordinateType);
    }

    private static bool TryResolveCoordinateTypeFromText(string? raw, out string coordinateType)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            coordinateType = UnknownCoordinateType;
            return false;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Contains("baidu", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bd09", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("百度", StringComparison.OrdinalIgnoreCase))
        {
            coordinateType = "bd09";
            return true;
        }

        if (normalized.Contains("gcj02", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gaode", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("amap", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("高德", StringComparison.OrdinalIgnoreCase))
        {
            coordinateType = "gcj02";
            return true;
        }

        if (normalized.Contains("wgs84", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gps", StringComparison.OrdinalIgnoreCase))
        {
            coordinateType = "wgs84";
            return true;
        }

        if (normalized.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            coordinateType = UnknownCoordinateType;
            return true;
        }

        coordinateType = UnknownCoordinateType;
        return false;
    }

    private static bool IsKnownCoordinateType(string? coordinateType)
    {
        return !string.IsNullOrWhiteSpace(coordinateType)
            && !string.Equals(coordinateType, UnknownCoordinateType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindPropertyValue(JsonElement element, string propertyName, out JsonElement value, int depth = 0)
    {
        value = default;
        if (depth > 8)
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindPropertyValue(property.Value, propertyName, out value, depth + 1))
                    {
                        return true;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindPropertyValue(item, propertyName, out value, depth + 1))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private static PayloadProjection ExtractProjection(
        JsonElement element,
        string? fallbackDeviceCode = null,
        string? fallbackDeviceName = null)
    {
        var hasCoordinateTypeSignal = TryResolveCoordinateType(element, out var coordinateType);

        return new PayloadProjection(
            NormalizeText(ReadString(element, "deviceCode", "id", "gbId")) ?? NormalizeText(fallbackDeviceCode) ?? string.Empty,
            NormalizeText(ReadString(element, "deviceName", "name", "pointName", "cameraName")) ?? NormalizeText(fallbackDeviceName) ?? string.Empty,
            ReadDouble(element, "longitude", "lng", "lon", "mapLongitude", "deviceLongitude"),
            ReadDouble(element, "latitude", "lat", "mapLatitude", "deviceLatitude"),
            coordinateType,
            hasCoordinateTypeSignal,
            ReadBool(element, "onlineStatus", "online", "isOnline", "onlineFlag"),
            NormalizeText(ReadString(element, "lastSyncTime", "reportTime", "importTime", "updateTime")));
    }

    private static PayloadProjection BuildDetailProjection(DeviceDetailResult detail)
    {
        var projection = new PayloadProjection(
            NormalizeText(detail.DeviceCode) ?? string.Empty,
            NormalizeText(detail.DeviceName) ?? string.Empty,
            detail.Longitude is decimal detailLongitude ? (double)detailLongitude : null,
            detail.Latitude is decimal detailLatitude ? (double)detailLatitude : null,
            UnknownCoordinateType,
            false,
            detail.IsOnline,
            NormalizeText(detail.LastSyncTime));

        if (TryParseJson(detail.RawResponse, out var rawRoot))
        {
            projection = MergeProjection(projection, ExtractProjection(rawRoot, detail.DeviceCode, detail.DeviceName));
        }

        foreach (var snapshot in detail.Snapshots.Where(snapshot => snapshot.IsSuccess))
        {
            if (TryParseJson(snapshot.RawResponse, out var snapshotRoot))
            {
                projection = MergeProjection(
                    projection,
                    ExtractProjection(snapshotRoot, snapshot.DeviceCode ?? detail.DeviceCode, snapshot.DeviceName ?? detail.DeviceName));
                continue;
            }

            projection = MergeProjection(
                projection,
                new PayloadProjection(
                    NormalizeText(snapshot.DeviceCode) ?? NormalizeText(detail.DeviceCode) ?? string.Empty,
                    NormalizeText(snapshot.DeviceName) ?? NormalizeText(detail.DeviceName) ?? string.Empty,
                    snapshot.Longitude is decimal snapshotLongitude ? (double)snapshotLongitude : null,
                    snapshot.Latitude is decimal snapshotLatitude ? (double)snapshotLatitude : null,
                    UnknownCoordinateType,
                    false,
                    snapshot.IsOnline,
                    NormalizeText(snapshot.LastSyncTime)));
        }

        return projection;
    }

    private static PayloadProjection MergeProjection(PayloadProjection current, PayloadProjection candidate)
    {
        var longitude = SelectCoordinateValue(current.Longitude, current.Latitude, candidate.Longitude, candidate.Latitude, isLongitude: true);
        var latitude = SelectCoordinateValue(current.Longitude, current.Latitude, candidate.Longitude, candidate.Latitude, isLongitude: false);

        var coordinateType = current.RawCoordinateType;
        var isCoordinateTypeExplicit = current.IsCoordinateTypeExplicit;
        if (candidate.IsCoordinateTypeExplicit && (!isCoordinateTypeExplicit || !IsKnownCoordinateType(coordinateType)))
        {
            coordinateType = candidate.RawCoordinateType;
            isCoordinateTypeExplicit = true;
        }
        else if (IsKnownCoordinateType(candidate.RawCoordinateType) && !IsKnownCoordinateType(coordinateType))
        {
            coordinateType = candidate.RawCoordinateType;
            isCoordinateTypeExplicit = candidate.IsCoordinateTypeExplicit;
        }

        return new PayloadProjection(
            NormalizeText(current.DeviceCode) ?? NormalizeText(candidate.DeviceCode) ?? string.Empty,
            PreferDetailName(current.DeviceName, candidate.DeviceName),
            longitude,
            latitude,
            coordinateType,
            isCoordinateTypeExplicit,
            current.IsOnline ?? candidate.IsOnline,
            NormalizeText(current.LastSyncTime) ?? NormalizeText(candidate.LastSyncTime));
    }

    private static double? SelectCoordinateValue(
        double? currentLongitude,
        double? currentLatitude,
        double? candidateLongitude,
        double? candidateLatitude,
        bool isLongitude)
    {
        var currentHasPair = HasCompleteCoordinate(currentLongitude, currentLatitude);
        var candidateHasPair = HasCompleteCoordinate(candidateLongitude, candidateLatitude);

        if (candidateHasPair)
        {
            return isLongitude ? candidateLongitude : candidateLatitude;
        }

        if (currentHasPair)
        {
            return isLongitude ? currentLongitude : currentLatitude;
        }

        return isLongitude
            ? candidateLongitude ?? currentLongitude
            : candidateLatitude ?? currentLatitude;
    }

    private static string ResolveFinalCoordinateType(
        CatalogCandidate candidate,
        PayloadProjection? detailProjection,
        double? rawLongitude,
        double? rawLatitude)
    {
        if (detailProjection is not null && IsKnownCoordinateType(detailProjection.RawCoordinateType))
        {
            return detailProjection.RawCoordinateType;
        }

        if (IsKnownCoordinateType(candidate.RawCoordinateType))
        {
            return candidate.RawCoordinateType;
        }

        return HasCompleteCoordinate(rawLongitude, rawLatitude)
            ? DefaultPlatformCoordinateType
            : UnknownCoordinateType;
    }

    private static bool HasCompleteCoordinate(double? longitude, double? latitude)
    {
        return longitude.HasValue && latitude.HasValue;
    }

    private static bool CoordinatesDiffer(double? firstLongitude, double? firstLatitude, double? secondLongitude, double? secondLatitude)
    {
        if (!HasCompleteCoordinate(firstLongitude, firstLatitude) || !HasCompleteCoordinate(secondLongitude, secondLatitude))
        {
            return false;
        }

        return Math.Abs(firstLongitude!.Value - secondLongitude!.Value) > 0.000001d
            || Math.Abs(firstLatitude!.Value - secondLatitude!.Value) > 0.000001d;
    }

    private static string PreferDetailName(string current, string? candidate)
    {
        var normalizedCurrent = NormalizeText(current);
        var normalizedCandidate = NormalizeText(candidate);

        if (string.IsNullOrWhiteSpace(normalizedCurrent))
        {
            return normalizedCandidate ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return normalizedCurrent;
        }

        return normalizedCandidate.Length > normalizedCurrent.Length
            ? normalizedCandidate
            : normalizedCurrent;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ToProtocolKey(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => "webrtc",
            SitePreviewProtocol.Flv => "flv",
            SitePreviewProtocol.Hls => "hls",
            SitePreviewProtocol.H5 => "h5",
            _ => "unknown"
        };
    }

    private static SitePreviewProtocol ToPreviewProtocol(string? protocol)
    {
        return protocol?.Trim().ToLowerInvariant() switch
        {
            "webrtc" => SitePreviewProtocol.WebRtc,
            "flv" => SitePreviewProtocol.Flv,
            "hls" => SitePreviewProtocol.Hls,
            "h5" => SitePreviewProtocol.H5,
            _ => SitePreviewProtocol.Unknown
        };
    }

    private sealed record CatalogCandidate(
        string DeviceCode,
        string DeviceName,
        double? RawLongitude,
        double? RawLatitude,
        string RawCoordinateType,
        bool IsCoordinateTypeExplicit,
        bool? IsOnline);

    private sealed class DevicePlatformRecord
    {
        public required string DeviceCode { get; init; }

        public required string DeviceName { get; init; }

        public double? RawLongitude { get; init; }

        public double? RawLatitude { get; init; }

        public required string RawCoordinateType { get; init; }

        public required bool IsCoordinateEnrichedFromDetail { get; init; }

        public bool? IsOnline { get; init; }
    }

    private sealed record PayloadProjection(
        string DeviceCode,
        string DeviceName,
        double? Longitude,
        double? Latitude,
        string RawCoordinateType,
        bool IsCoordinateTypeExplicit,
        bool? IsOnline,
        string? LastSyncTime);

    private sealed record PlatformCacheEntry(
        IReadOnlyList<PlatformSiteSnapshot> Snapshots,
        DateTimeOffset ExpiresAt);

    private sealed record DetailCacheEntry(
        DeviceDetailResult Detail,
        DateTimeOffset ExpiresAt);

    private sealed record CatalogLoadResult(
        IReadOnlyList<DeviceCatalogItem> Items,
        int PageCount,
        int TotalHint);

    private sealed record DetailLoadResult(
        IReadOnlyDictionary<string, DeviceDetailResult> Details,
        int SelectedCount);

    private sealed record PlatformLoadResult(
        IReadOnlyList<PlatformSiteSnapshot> Snapshots,
        int CatalogCount,
        int CatalogPageCount,
        int DetailCount,
        int FinalSelectedCount);
}
