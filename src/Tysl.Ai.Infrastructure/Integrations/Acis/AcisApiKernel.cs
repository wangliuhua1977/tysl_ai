
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TianyiVision.Acis.Reusable;

/// <summary>
/// 单文件复用内核：封装 CTYun 令牌缓存、签名/加解密、平台接口、地图坐标转换、直播 URL 获取、预览宿主页生成、配置文件与本地日志。
/// 设计目标：给重构版 ACIS 直接复用，不依赖当前项目的 UI/ViewModel/DI 容器。
/// </summary>
public sealed class AcisApiKernel : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan DeviceDetailCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeviceDetailPartialCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DeviceDetailFailureCacheLifetime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DeviceDetailEndpointCooldownLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DeviceAlertCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DeviceAlertFailureCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PreviewSuccessCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PreviewFailureCacheLifetime = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _tokenSync = new(1, 1);
    private readonly IAcisKernelLogger _logger;
    private readonly ConcurrentDictionary<string, CacheItem<DeviceDetailResult>> _deviceDetailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CacheItem<DeviceAlertBatchResult>> _deviceAlertCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CacheItem<PreviewResolution>> _previewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _endpointCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private TokenCacheEntry? _token;

    public AcisApiKernel(AcisKernelOptions options, HttpClient? httpClient = null, IAcisKernelLogger? logger = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Directory.CreateDirectory(Options.WorkDirectory);
        _logger = logger ?? new FileAcisKernelLogger(Path.Combine(Options.WorkDirectory, "acis-kernel.log"));
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(10, Options.Http.TimeoutSeconds))
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public AcisKernelOptions Options { get; }

    public static AcisKernelOptions LoadOptions(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("配置文件路径不能为空。", nameof(path));
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        var options = JsonSerializer.Deserialize<AcisKernelOptions>(json, JsonOptions)
                      ?? throw new InvalidOperationException("配置文件反序列化失败。");
        options.Ctyun ??= new CtyunOptions();
        options.Amap ??= new AmapOptions();
        options.Preview ??= new PreviewOptions();
        options.Http ??= new HttpOptions();

        if (string.IsNullOrWhiteSpace(options.WorkDirectory))
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? AppContext.BaseDirectory;
            options.WorkDirectory = Path.Combine(baseDir, ".acis-kernel");
        }

        if (string.IsNullOrWhiteSpace(options.Amap.CoordinateConvertUrl))
        {
            options.Amap.CoordinateConvertUrl = "https://restapi.amap.com/v3/assistant/coordinate/convert";
        }

        return options;
    }

    public static void SaveOptions(string path, AcisKernelOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("配置文件路径不能为空。", nameof(path));
        }

        var json = JsonSerializer.Serialize(options, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? AppContext.BaseDirectory);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public async Task<TokenCacheEntry> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_token is null)
            {
                _token = await LoadTokenCacheAsync(cancellationToken).ConfigureAwait(false);
            }

            if (CanReuse(_token, now, Options.Ctyun.TokenReuseBeforeExpirySeconds))
            {
                _logger.Info("Token", $"复用本地 accessToken，expiresAt={_token!.AccessTokenExpiresAtUtc:yyyy-MM-dd HH:mm:ss}.");
                return _token!;
            }

            if (_token is not null && !string.IsNullOrWhiteSpace(_token.RefreshToken) && _token.RefreshTokenExpiresAtUtc > now)
            {
                try
                {
                    _logger.Info("Token", "accessToken 接近过期，尝试 refreshToken 刷新。");
                    var refreshed = await RequestTokenAsync("refresh_token", _token.RefreshToken, cancellationToken).ConfigureAwait(false);
                    _token = refreshed;
                    await SaveTokenCacheAsync(refreshed, cancellationToken).ConfigureAwait(false);
                    return refreshed;
                }
                catch (Exception ex) when (_token.AccessTokenExpiresAtUtc > now)
                {
                    _logger.Warn("Token", $"refreshToken 刷新失败，但旧 token 仍可用，将继续复用。reason={ex.Message}");
                    return _token;
                }
            }

            var acquired = await RequestTokenAsync(Options.Ctyun.GrantType, null, cancellationToken).ConfigureAwait(false);
            _token = acquired;
            await SaveTokenCacheAsync(acquired, cancellationToken).ConfigureAwait(false);
            return acquired;
        }
        finally
        {
            _tokenSync.Release();
        }
    }

    public async Task<ProtectedApiResult<JsonElement>> PostProtectedJsonAsync(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>> privateParameters,
        bool decryptResponseData = false,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var parameters = privateParameters?.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value is not null).ToList()
                        ?? new List<KeyValuePair<string, string>>();

        var url = BuildUrl(Options.Ctyun.BaseUrl, endpoint);
        var privatePlain = CtyunSecurity.BuildPrivateParameterString(parameters);
        var encryptedParams = CtyunSecurity.EncryptParams(parameters, Options.Ctyun.AppSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var signature = CtyunSecurity.BuildSignature(
            Options.Ctyun.AppId,
            Options.Ctyun.ClientType,
            encryptedParams,
            timestamp,
            Options.Ctyun.Version,
            Options.Ctyun.AppSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("appId", Options.Ctyun.AppId),
                new KeyValuePair<string, string>("params", encryptedParams),
                new KeyValuePair<string, string>("signature", signature),
                new KeyValuePair<string, string>("version", Options.Ctyun.Version),
                new KeyValuePair<string, string>("clientType", Options.Ctyun.ClientType),
                new KeyValuePair<string, string>("timestamp", timestamp)
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("apiVersion", Options.Ctyun.ApiVersion);

        _logger.Info("CTYunHttp", $"Calling protected API: path={endpoint}, paramsSummary={MaskSensitive(privatePlain)}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("CTYunHttp", $"Protected API responded: path={endpoint}, status={(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"CTYun API HTTP 失败：{endpoint} -> {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        var code = ReadInt(root, "code") ?? -1;
        var message = ReadString(root, "msg") ?? ReadString(root, "message") ?? string.Empty;

        if (code != 0)
        {
            _logger.Warn("CTYunHttp", $"Protected API returned business failure: path={endpoint}, code={code}, message={message}");
            return new ProtectedApiResult<JsonElement>(
                false,
                code,
                message,
                root,
                body,
                response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                endpoint,
                MaskSensitive(privatePlain));
        }

        var data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;
        if (decryptResponseData && data.ValueKind == JsonValueKind.String)
        {
            var decrypted = CtyunPayloadParser.TryResolvePayload(data.GetString() ?? string.Empty, Options.Ctyun.RsaPrivateKeyPem, Options.Ctyun.AppSecret);
            return new ProtectedApiResult<JsonElement>(
                true,
                0,
                message,
                decrypted.ResolvedJson ?? root,
                body,
                response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                endpoint,
                MaskSensitive(privatePlain),
                decrypted);
        }

        return new ProtectedApiResult<JsonElement>(
            true,
            0,
            message,
            data.ValueKind == JsonValueKind.Undefined ? root : data,
            body,
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            endpoint,
            MaskSensitive(privatePlain));
    }

    public async Task<DeviceCatalogPageResult> GetDeviceCatalogPageAsync(long lastId, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var result = await PostProtectedJsonAsync(
            Options.Ctyun.Endpoints.GetAllDeviceListNew,
            new[]
            {
                new KeyValuePair<string, string>("accessToken", token.AccessToken),
                new KeyValuePair<string, string>("enterpriseUser", Options.Ctyun.EnterpriseUser),
                new KeyValuePair<string, string>("lastId", lastId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("hasChildDevices", "0")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var items = new List<DeviceCatalogItem>();
        long nextLastId = -1;
        int total = 0;

        if (result.Data.ValueKind == JsonValueKind.Object)
        {
            if (result.Data.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    items.Add(new DeviceCatalogItem
                    {
                        DeviceCode = ReadString(item, "deviceCode") ?? ReadString(item, "id") ?? string.Empty,
                        DeviceName = ReadString(item, "deviceName") ?? ReadString(item, "name") ?? string.Empty,
                        Raw = item.GetRawText()
                    });
                }
            }

            nextLastId = ReadLong(result.Data, "lastId") ?? ReadLong(result.Data, "nextLastId") ?? -1;
            total = ReadInt(result.Data, "total") ?? 0;
        }

        return new DeviceCatalogPageResult(result.IsSuccess, result.ResponseCode, result.ResponseMessage, items, nextLastId, total, result.RawResponse);
    }

    public async Task<DeviceDetailResult> GetDeviceDetailAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new ArgumentException("deviceCode 不能为空。", nameof(deviceCode));
        }

        var cacheKey = deviceCode.Trim();
        if (TryGetCache(_deviceDetailCache, cacheKey, out var cached))
        {
            return cached;
        }

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var candidateEndpoints = BuildDeviceDetailEndpoints().ToArray();

        var snapshots = new List<DeviceDetailSnapshot>();
        DeviceDetailSnapshot? merged = null;

        foreach (var endpoint in candidateEndpoints)
        {
            if (TryGetEndpointCooldown(endpoint, out var cooldownUntil))
            {
                snapshots.Add(new DeviceDetailSnapshot(
                    endpoint,
                    false,
                    30041,
                    "详情接口冷却中",
                    null,
                    null,
                    null,
                    null,
                    string.Empty));
                _logger.Warn(
                    "PointDetail",
                    $"Skipping endpoint during cooldown: deviceCode={cacheKey}, endpoint={endpoint}, cooldownUntil={cooldownUntil:yyyy-MM-dd HH:mm:ss}");
                continue;
            }

            var response = await PostProtectedJsonAsync(
                endpoint,
                new[]
                {
                    new KeyValuePair<string, string>("accessToken", token.AccessToken),
                    new KeyValuePair<string, string>("enterpriseUser", Options.Ctyun.EnterpriseUser),
                    new KeyValuePair<string, string>("deviceCode", cacheKey)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                if (ShouldCooldownEndpoint(endpoint, response.ResponseCode))
                {
                    PutEndpointCooldown(endpoint, DeviceDetailEndpointCooldownLifetime);
                }

                snapshots.Add(new DeviceDetailSnapshot(endpoint, false, response.ResponseCode, response.ResponseMessage, null, null, null, null, response.RawResponse));
                continue;
            }

            var payload = response.Data;
            var snapshot = new DeviceDetailSnapshot(
                endpoint,
                true,
                response.ResponseCode,
                response.ResponseMessage,
                ReadString(payload, "deviceCode") ?? cacheKey,
                ReadString(payload, "deviceName") ?? ReadString(payload, "name"),
                ReadDecimal(payload, "longitude") ?? ReadDecimal(payload, "lng"),
                ReadDecimal(payload, "latitude") ?? ReadDecimal(payload, "lat"),
                response.RawResponse,
                ReadBoolFlexible(payload, "onlineStatus") ?? ReadBoolFlexible(payload, "online") ?? ReadBoolFlexible(payload, "isOnline"),
                ReadString(payload, "lastSyncTime") ?? ReadString(payload, "reportTime") ?? ReadString(payload, "importTime"));

            snapshots.Add(snapshot);

            merged = merged is null ? snapshot : MergeSnapshot(merged, snapshot);

            if (HasUsableDetail(merged))
            {
                break;
            }
        }

        var final = new DeviceDetailResult
        {
            DeviceCode = merged?.DeviceCode ?? cacheKey,
            DeviceName = merged?.DeviceName,
            Longitude = merged?.Longitude,
            Latitude = merged?.Latitude,
            IsOnline = merged?.IsOnline,
            LastSyncTime = merged?.LastSyncTime,
            Snapshots = snapshots,
            RawResponse = merged?.RawResponse ?? snapshots.LastOrDefault()?.RawResponse ?? string.Empty
        };

        var ttl = final.HasCoordinate ? DeviceDetailCacheLifetime
            : snapshots.Any(x => x.IsSuccess) ? DeviceDetailPartialCacheLifetime
            : DeviceDetailFailureCacheLifetime;

        PutCache(_deviceDetailCache, cacheKey, final, ttl);
        _logger.Info("PointDetail", $"Device detail resolved: deviceCode={cacheKey}, hasCoordinate={final.HasCoordinate}, online={final.IsOnline?.ToString() ?? "unknown"}, snapshotCount={snapshots.Count}");
        return final;
    }

    public async Task<DeviceAlertBatchResult> GetDeviceAlertsAsync(
        string deviceCode,
        int pageNo = 1,
        int pageSize = 20,
        string alertSource = "1",
        string alertTypeList = "1,2,10,11",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new ArgumentException("deviceCode 不能为空。", nameof(deviceCode));
        }

        var cacheKey = $"{deviceCode.Trim()}|{pageNo}|{pageSize}|{alertSource}|{alertTypeList}";
        if (TryGetCache(_deviceAlertCache, cacheKey, out var cached))
        {
            return cached;
        }

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var response = await PostProtectedJsonAsync(
            Options.Ctyun.Endpoints.GetDeviceAlarmMessage,
            new[]
            {
                new KeyValuePair<string, string>("accessToken", token.AccessToken),
                new KeyValuePair<string, string>("enterpriseUser", Options.Ctyun.EnterpriseUser),
                new KeyValuePair<string, string>("deviceCode", deviceCode.Trim()),
                new KeyValuePair<string, string>("pageNo", pageNo.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("alertSource", alertSource),
                new KeyValuePair<string, string>("alertTypeList", alertTypeList)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var items = new List<DeviceAlertItem>();
        if (response.IsSuccess && response.Data.ValueKind == JsonValueKind.Object)
        {
            var list = response.Data.TryGetProperty("list", out var listElement) && listElement.ValueKind == JsonValueKind.Array
                ? listElement
                : default;

            if (list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    items.Add(new DeviceAlertItem
                    {
                        AlertId = ReadString(item, "id") ?? ReadString(item, "alertId") ?? string.Empty,
                        AlertName = ReadString(item, "alertName") ?? ReadString(item, "name") ?? string.Empty,
                        AlertType = ReadString(item, "alertType") ?? string.Empty,
                        AlertTime = ReadString(item, "alertTime") ?? string.Empty,
                        Raw = item.GetRawText()
                    });
                }
            }
        }

        var final = new DeviceAlertBatchResult(response.IsSuccess, response.ResponseCode, response.ResponseMessage, items, response.RawResponse);
        PutCache(_deviceAlertCache, cacheKey, final, final.IsSuccess ? DeviceAlertCacheLifetime : DeviceAlertFailureCacheLifetime);
        return final;
    }

    public async Task<AiAlertBatchResult> GetAiAlertsAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var response = await PostProtectedJsonAsync(
            Options.Ctyun.Endpoints.GetAiAlertInfoList,
            new[]
            {
                new KeyValuePair<string, string>("accessToken", token.AccessToken),
                new KeyValuePair<string, string>("enterpriseUser", Options.Ctyun.EnterpriseUser)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new AiAlertBatchResult(response.IsSuccess, response.ResponseCode, response.ResponseMessage, response.Data.ValueKind == JsonValueKind.Undefined ? string.Empty : response.Data.GetRawText(), response.RawResponse);
    }

    public async Task<PreviewResolution> ResolvePreviewAsync(
        string deviceCode,
        AcisPreviewIntent intent = AcisPreviewIntent.ClickPreview,
        IReadOnlyList<string>? protocolOrderOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new ArgumentException("deviceCode 不能为空。", nameof(deviceCode));
        }

        var normalized = deviceCode.Trim();
        string[] protocols = protocolOrderOverride is { Count: > 0 }
            ? protocolOrderOverride
                .Select(protocol => protocol?.Trim().ToLowerInvariant())
                .Where(protocol => !string.IsNullOrWhiteSpace(protocol))
                .Select(protocol => protocol!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : intent == AcisPreviewIntent.ClickPreview
                ? (Options.Preview.ClickProtocolOrder?.Length > 0 ? Options.Preview.ClickProtocolOrder : new[] { "webrtc", "flv", "hls" })
                : (Options.Preview.InspectionProtocolOrder?.Length > 0 ? Options.Preview.InspectionProtocolOrder : new[] { "flv", "hls" });

        PreviewResolution? preferredFailure = null;
        var attempted = new List<string>();

        foreach (var protocol in protocols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(protocol))
            {
                continue;
            }

            var cacheKey = $"{normalized}|{protocol}";
            if (TryGetCache(_previewCache, cacheKey, out var cached))
            {
                attempted.Add(protocol);
                if (cached.IsSuccess)
                {
                    _logger.Info("InspectionPreviewStream", $"缓存命中：deviceCode={normalized}, selectedProtocol={protocol}, previewUrl={cached.PreviewUrl}");
                    return cached with { AttemptedProtocols = attempted.ToArray() };
                }

                preferredFailure = ChoosePreferred(preferredFailure, cached with { AttemptedProtocols = attempted.ToArray() });
                continue;
            }

            attempted.Add(protocol);
            PreviewResolution current;
            try
            {
                current = await TryResolveSingleProtocolAsync(normalized, protocol, attempted, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Info(
                    "CTYunPreview",
                    $"deviceCode={normalized}, selectedProtocol={protocol}, attemptedProtocols={string.Join(">", attempted)}, requestException={ex.GetType().Name}, requestExceptionMessage={ex.Message}");
                current = PreviewResolution.Failure(
                    deviceCode: normalized,
                    selectedProtocol: protocol,
                    attemptedProtocols: attempted.ToArray(),
                    mediaApiPath: ResolveProtocolPlan(protocol).Endpoint,
                    responseCode: -1,
                    responseMessage: ex.Message,
                    streamAcquireResult: "preview api request failed",
                    failureCategory: PreviewFailureCategory.PlayerProtocolNotSupported,
                    failureReason: ex.Message);
            }
            PutCache(_previewCache, cacheKey, current, current.IsSuccess ? PreviewSuccessCacheLifetime : PreviewFailureCacheLifetime);

            if (current.IsSuccess)
            {
                _logger.Info(
                    "CTYunPreview",
                    $"deviceCode={normalized}, attemptedProtocols={string.Join(">", attempted)}, finalProtocol={current.ParsedProtocolType ?? current.SelectedProtocol}, finalPreviewUrl={current.PreviewUrl}");
                return current;
            }

            preferredFailure = ChoosePreferred(preferredFailure, current);
        }

        var failure = preferredFailure ?? PreviewResolution.Failure(
            deviceCode: normalized,
            selectedProtocol: string.Empty,
            attemptedProtocols: attempted.ToArray(),
            mediaApiPath: string.Empty,
            responseCode: -1,
            responseMessage: "所有协议均未获取到可承载的预览地址。",
            streamAcquireResult: "无流地址",
            failureCategory: PreviewFailureCategory.NoStreamAddress,
            failureReason: "所有协议回退后仍失败。");
        _logger.Info(
            "CTYunPreview",
            $"deviceCode={normalized}, attemptedProtocols={string.Join(">", attempted)}, finalResult=failure, failureReason={failure.FailureReason}");
        return failure;
    }

    public async Task<CoordinateConvertResult> ConvertCoordinatesAsync(
        IReadOnlyList<GeoPoint> points,
        string coordinateSystem = "baidu",
        CancellationToken cancellationToken = default)
    {
        if (points is null || points.Count == 0)
        {
            return new CoordinateConvertResult
            {
                IsSuccess = true,
                Converted = Array.Empty<GeoPoint>(),
                RawResponse = string.Empty
            };
        }

        if (string.IsNullOrWhiteSpace(Options.Amap.WebServiceKey))
        {
            throw new InvalidOperationException("未配置高德 WebServiceKey，无法调用坐标转换接口。");
        }

        var chunk = string.Join("|", points.Select(x => $"{x.Longitude.ToString("0.######", CultureInfo.InvariantCulture)},{x.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}"));
        var url = $"{Options.Amap.CoordinateConvertUrl}?key={Uri.EscapeDataString(Options.Amap.WebServiceKey)}&locations={Uri.EscapeDataString(chunk)}&coordsys={Uri.EscapeDataString(coordinateSystem)}&output=JSON";
        _logger.Info("Amap", $"Calling coordinate convert API: pointCount={points.Count}, coordsys={coordinateSystem}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var status = ReadString(root, "status");
        var info = ReadString(root, "info") ?? string.Empty;
        var locationText = ReadString(root, "locations") ?? string.Empty;

        if (!string.Equals(status, "1", StringComparison.Ordinal))
        {
            return new CoordinateConvertResult
            {
                IsSuccess = false,
                Info = info,
                RawResponse = body,
                Converted = Array.Empty<GeoPoint>()
            };
        }

        var converted = locationText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text =>
            {
                var parts = text.Split(',', StringSplitOptions.TrimEntries);
                return parts.Length == 2
                    && decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lng)
                    && decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)
                    ? new GeoPoint(lng, lat)
                    : null;
            })
            .OfType<GeoPoint>()
            .ToArray();

        return new CoordinateConvertResult
        {
            IsSuccess = true,
            Info = info,
            RawResponse = body,
            Converted = converted
        };
    }

    public async Task<PreviewHostResult> BuildPreviewHostAsync(
        PreviewHostRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.SourceUrl)) throw new ArgumentException("SourceUrl 不能为空。", nameof(request));
        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"无效的预览地址：{request.SourceUrl}");
        }

        var protocol = (request.Protocol ?? DetectProtocolFromUrl(request.SourceUrl)).Trim().ToLowerInvariant();
        var html = PreviewHostHtmlBuilder.Build(request.DeviceCode, request.SourceUrl, protocol, request.Title);
        var folder = Path.Combine(Options.WorkDirectory, "preview-hosts");
        Directory.CreateDirectory(folder);
        var fileName = $"{SanitizeFileName(request.DeviceCode)}-{protocol}-{Guid.NewGuid():N}.html";
        var path = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        return new PreviewHostResult
        {
            Protocol = protocol,
            HtmlPath = path,
            HtmlUri = new Uri(path),
            SourceUrl = request.SourceUrl
        };
    }

    public Task<WebRtcPlayNegotiationResult> NegotiateWebRtcPlayAsync(
        string playbackUrl,
        string offerSdp,
        CancellationToken cancellationToken = default)
    {
        return NegotiateWebRtcPlayAsync(null, playbackUrl, offerSdp, cancellationToken);
    }

    public async Task<WebRtcPlayNegotiationResult> NegotiateWebRtcPlayAsync(
        string? apiUrl,
        string playbackUrl,
        string offerSdp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            throw new ArgumentException("playbackUrl is required.", nameof(playbackUrl));
        }

        if (string.IsNullOrWhiteSpace(offerSdp))
        {
            throw new ArgumentException("offerSdp is required.", nameof(offerSdp));
        }

        var resolvedApiUrl = string.IsNullOrWhiteSpace(apiUrl)
            ? BuildWebRtcPlayApiUrl(playbackUrl)
            : apiUrl;
        var payload = JsonSerializer.Serialize(new
        {
            api = resolvedApiUrl,
            streamurl = playbackUrl,
            clientip = (string?)null,
            sdp = offerSdp
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, resolvedApiUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _logger.Info("CTYunWebRtc", $"Negotiating WebRTC play: apiUrl={resolvedApiUrl}, streamUrl={playbackUrl}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _logger.Info("CTYunWebRtc", $"WebRTC play API responded: apiUrl={resolvedApiUrl}, status={(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            return WebRtcPlayNegotiationResult.Failure(
                resolvedApiUrl,
                $"WebRTC play API returned {(int)response.StatusCode} {response.StatusCode}.",
                body);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var responseCode = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var codeValue)
            ? codeValue
            : -1;
        var answerSdp = ReadString(root, "sdp");
        var sessionId = ReadString(root, "sessionid");
        var server = ReadString(root, "server");
        var failureReason = ReadString(root, "msg")
            ?? ReadString(root, "message")
            ?? "WebRTC answer unavailable.";

        return responseCode == 0 && !string.IsNullOrWhiteSpace(answerSdp)
            ? WebRtcPlayNegotiationResult.Success(resolvedApiUrl, answerSdp!, sessionId, server, body)
            : WebRtcPlayNegotiationResult.Failure(resolvedApiUrl, failureReason, body, responseCode, sessionId, server);
    }

    public static string BuildWebRtcPlayApiUrl(string playbackUrl)
    {
        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            throw new ArgumentException("playbackUrl is required.", nameof(playbackUrl));
        }

        if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid WebRTC playback URL: {playbackUrl}");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var apiPath = segments.Length > 0 && segments[0].Equals("webrtc-gateway", StringComparison.OrdinalIgnoreCase)
            ? "/webrtc-gateway/rtc/v1/play/"
            : "/rtc/v1/play/";

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = apiPath,
            Query = uri.Query.TrimStart('?')
        };

        return builder.Uri.ToString();
    }

    public async Task WriteDiagnosticAsync(string category, string message, CancellationToken cancellationToken = default)
    {
        await _logger.WriteAsync(category, message, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _tokenSync.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<TokenCacheEntry> RequestTokenAsync(string grantType, string? refreshToken, CancellationToken cancellationToken)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("grantType", grantType)
        };

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            parameters.Add(new("refreshToken", refreshToken));
        }

        var url = BuildUrl(Options.Ctyun.BaseUrl, Options.Ctyun.Endpoints.GetAccessToken);
        var encryptedParams = CtyunSecurity.EncryptParams(parameters, Options.Ctyun.AppSecret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var signature = CtyunSecurity.BuildSignature(
            Options.Ctyun.AppId,
            Options.Ctyun.ClientType,
            encryptedParams,
            timestamp,
            Options.Ctyun.Version,
            Options.Ctyun.AppSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("appId", Options.Ctyun.AppId),
                new KeyValuePair<string, string>("params", encryptedParams),
                new KeyValuePair<string, string>("signature", signature),
                new KeyValuePair<string, string>("version", Options.Ctyun.Version),
                new KeyValuePair<string, string>("clientType", Options.Ctyun.ClientType),
                new KeyValuePair<string, string>("timestamp", timestamp)
            })
        };
        request.Headers.TryAddWithoutValidation("apiVersion", Options.Ctyun.ApiVersion);

        _logger.Info("CTYunToken", $"Calling access token API: path={Options.Ctyun.Endpoints.GetAccessToken}, grantType={grantType}, enterpriseUser={MaskValue(Options.Ctyun.EnterpriseUser)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("CTYunToken", $"Access token API responded: status={(int)response.StatusCode} {response.StatusCode}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var code = ReadInt(root, "code");
        var msg = ReadString(root, "msg") ?? string.Empty;

        if (code != 0)
        {
            throw new InvalidOperationException($"获取 accessToken 失败：code={code}, msg={msg}");
        }

        if (!root.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException("accessToken 响应缺少 data 字段。");
        }

        var dto = JsonSerializer.Deserialize<TokenEnvelope>(data.GetRawText(), JsonOptions)
                  ?? throw new InvalidOperationException("token data 反序列化失败。");

        var now = DateTimeOffset.UtcNow;
        var token = new TokenCacheEntry
        {
            AppId = Options.Ctyun.AppId,
            AccessToken = dto.AccessToken ?? string.Empty,
            RefreshToken = dto.RefreshToken ?? string.Empty,
            ExpiresIn = dto.ExpiresIn,
            RefreshExpiresIn = dto.RefreshExpiresIn,
            AcquiredAtUtc = now,
            AccessTokenExpiresAtUtc = now.AddSeconds(dto.ExpiresIn),
            RefreshTokenExpiresAtUtc = now.AddSeconds(dto.RefreshExpiresIn)
        };

        _logger.Info("CTYunToken", $"Access token acquired successfully: expiresAt={token.AccessTokenExpiresAtUtc:yyyy-MM-dd HH:mm:ss}, refreshExpiresAt={token.RefreshTokenExpiresAtUtc:yyyy-MM-dd HH:mm:ss}");
        return token;
    }

    private async Task<TokenCacheEntry?> LoadTokenCacheAsync(CancellationToken cancellationToken)
    {
        var path = GetTokenCachePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var token = JsonSerializer.Deserialize<TokenCacheEntry>(json, JsonOptions);
            if (token is not null
                && !string.Equals(token.AppId, Options.Ctyun.AppId, StringComparison.Ordinal))
            {
                _logger.Warn(
                    "Token",
                    $"token cache appId mismatch or missing. cachedAppId={token.AppId ?? "missing"}, currentAppId={Options.Ctyun.AppId}. token cache will be ignored.");
                return null;
            }

            return token;
        }
        catch (Exception ex)
        {
            _logger.Warn("Token", $"读取 token 缓存失败，将忽略本地缓存。reason={ex.Message}");
            return null;
        }
    }

    private async Task SaveTokenCacheAsync(TokenCacheEntry token, CancellationToken cancellationToken)
    {
        var path = GetTokenCachePath();
        var json = JsonSerializer.Serialize(token, JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private string GetTokenCachePath() => Path.Combine(Options.WorkDirectory, "token-cache.json");

    private async Task<PreviewResolution> TryResolveSingleProtocolAsync(
        string deviceCode,
        string protocol,
        IReadOnlyList<string> attemptedProtocols,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var protocolPlan = ResolveProtocolPlan(protocol);
        var response = await PostProtectedJsonAsync(
            protocolPlan.Endpoint,
            protocolPlan.BuildParameters(token.AccessToken, Options.Ctyun.EnterpriseUser, deviceCode),
            decryptResponseData: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var parser = response.PayloadParserResult ?? CtyunPayloadParser.TryResolvePayload(
            response.Data.ValueKind == JsonValueKind.Undefined ? string.Empty : response.Data.ToString(),
            Options.Ctyun.RsaPrivateKeyPem,
            Options.Ctyun.AppSecret);

        var previewUrl = parser.MatchedUrl;
        var parsedProtocol = parser.ParsedProtocolType ?? protocolPlan.Protocol;
        var streamResult = string.IsNullOrWhiteSpace(previewUrl)
            ? "接口成功但未返回可解析的预览地址"
            : $"已获取{parsedProtocol.ToUpperInvariant()}流地址";

        var logMessage =
            $"deviceCode={deviceCode}, selectedProtocol={protocolPlan.Protocol}, attemptedProtocols={string.Join(">", attemptedProtocols)}, mediaApiPath={protocolPlan.Endpoint}, " +
            $"responseCode={response.ResponseCode}, responseMessage={response.ResponseMessage}, matchedFieldPath={parser.MatchedFieldPath ?? "none"}, decryptMode={parser.DecryptMode ?? "none"}, " +
            $"parsedProtocolType={parsedProtocol ?? "unknown"}, parsedPreviewUrl={previewUrl ?? "null"}, streamAcquireResult={streamResult}";
        _logger.Info("CTYunPreview", logMessage);

        if (string.IsNullOrWhiteSpace(previewUrl))
        {
            return PreviewResolution.Failure(
                deviceCode,
                protocolPlan.Protocol,
                attemptedProtocols.ToArray(),
                protocolPlan.Endpoint,
                response.ResponseCode,
                response.ResponseMessage,
                "在线但无流地址",
                PreviewFailureCategory.NoStreamAddress,
                string.IsNullOrWhiteSpace(parser.FailureReason) ? "预览地址接口未返回可解析的数据。" : parser.FailureReason,
                parser,
                response.RawResponse);
        }

        var hostSupport = EvaluateHostSupport(parsedProtocol);
        if (!hostSupport.IsSupported)
        {
            return PreviewResolution.Failure(
                deviceCode,
                protocolPlan.Protocol,
                attemptedProtocols.ToArray(),
                protocolPlan.Endpoint,
                response.ResponseCode,
                response.ResponseMessage,
                $"已获取{parsedProtocol?.ToUpperInvariant()}流地址但当前宿主不支持",
                PreviewFailureCategory.PlayerProtocolNotSupported,
                hostSupport.Reason,
                parser,
                response.RawResponse,
                previewUrl,
                parsedProtocol);
        }

        return PreviewResolution.Success(
            deviceCode,
            protocolPlan.Protocol,
            attemptedProtocols.ToArray(),
            protocolPlan.Endpoint,
            response.ResponseCode,
            response.ResponseMessage,
            previewUrl,
            parsedProtocol ?? protocolPlan.Protocol,
            parser,
            response.RawResponse);
    }

    private PreviewProtocolPlan ResolveProtocolPlan(string protocol)
    {
        return protocol.Trim().ToLowerInvariant() switch
        {
            "flv" => PreviewProtocolPlan.Flv(Options.Ctyun.Endpoints.GetDeviceMediaUrlFlv),
            "hls" => PreviewProtocolPlan.Hls(Options.Ctyun.Endpoints.GetDeviceMediaUrlHls),
            "webrtc" => PreviewProtocolPlan.WebRtc(Options.Ctyun.Endpoints.GetDeviceMediaWebrtcUrl),
            "h5" => PreviewProtocolPlan.H5(Options.Ctyun.Endpoints.GetH5StreamUrl),
            _ => throw new NotSupportedException($"不支持的协议：{protocol}")
        };
    }

    private static HostSupportResult EvaluateHostSupport(string? protocol)
    {
        return protocol?.Trim().ToLowerInvariant() switch
        {
            "flv" => HostSupportResult.Supported(),
            "hls" => HostSupportResult.Supported(),
            "webrtc" => HostSupportResult.Supported(),
            _ => HostSupportResult.Supported()
        };
    }

    private static bool CanReuse(TokenCacheEntry? token, DateTimeOffset now, int reuseBeforeExpirySeconds)
    {
        return token is not null
               && !string.IsNullOrWhiteSpace(token.AccessToken)
               && token.AccessTokenExpiresAtUtc > now.AddSeconds(Math.Max(0, reuseBeforeExpirySeconds));
    }

    private IEnumerable<string> BuildDeviceDetailEndpoints()
    {
        var deduplicated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new[]
        {
            Options.Ctyun.Endpoints.GetCusDeviceByDeviceCode,
            Options.Ctyun.Endpoints.ShowDevice,
            Options.Ctyun.Endpoints.GetDeviceInfoByDeviceCode
        };

        foreach (var endpoint in endpoints)
        {
            var normalized = endpoint?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !deduplicated.Add(normalized))
            {
                continue;
            }

            yield return normalized;
        }
    }

    private static DeviceDetailSnapshot MergeSnapshot(DeviceDetailSnapshot current, DeviceDetailSnapshot candidate)
    {
        var currentHasCoordinate = current.Longitude.HasValue && current.Latitude.HasValue;
        var candidateHasCoordinate = candidate.Longitude.HasValue && candidate.Latitude.HasValue;

        return new DeviceDetailSnapshot(
            candidate.Endpoint,
            current.IsSuccess || candidate.IsSuccess,
            candidate.IsSuccess ? candidate.ResponseCode : current.ResponseCode,
            candidate.IsSuccess ? candidate.ResponseMessage : current.ResponseMessage,
            PreferNonEmpty(current.DeviceCode, candidate.DeviceCode),
            PreferDetailName(current.DeviceName, candidate.DeviceName),
            SelectCoordinate(current.Longitude, current.Latitude, candidate.Longitude, candidate.Latitude, isLongitude: true),
            SelectCoordinate(current.Longitude, current.Latitude, candidate.Longitude, candidate.Latitude, isLongitude: false),
            SelectRawResponse(current.RawResponse, candidate.RawResponse, currentHasCoordinate, candidateHasCoordinate),
            current.IsOnline ?? candidate.IsOnline,
            PreferNonEmpty(current.LastSyncTime, candidate.LastSyncTime));
    }

    private static bool HasUsableDetail(DeviceDetailSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.DeviceName)
            && snapshot.Longitude.HasValue
            && snapshot.Latitude.HasValue;
    }

    private static decimal? SelectCoordinate(
        decimal? currentLongitude,
        decimal? currentLatitude,
        decimal? candidateLongitude,
        decimal? candidateLatitude,
        bool isLongitude)
    {
        var currentHasPair = currentLongitude.HasValue && currentLatitude.HasValue;
        var candidateHasPair = candidateLongitude.HasValue && candidateLatitude.HasValue;

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

    private static string PreferDetailName(string? current, string? candidate)
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

    private static string PreferNonEmpty(string? current, string? candidate)
    {
        return NormalizeText(current) ?? NormalizeText(candidate) ?? string.Empty;
    }

    private static string SelectRawResponse(
        string current,
        string candidate,
        bool currentHasCoordinate,
        bool candidateHasCoordinate)
    {
        if (candidateHasCoordinate && !currentHasCoordinate)
        {
            return candidate;
        }

        return string.IsNullOrWhiteSpace(current) ? candidate : current;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private bool TryGetEndpointCooldown(string endpoint, out DateTimeOffset cooldownUntil)
    {
        cooldownUntil = default;
        if (!_endpointCooldowns.TryGetValue(endpoint, out var expiresAt))
        {
            return false;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _endpointCooldowns.TryRemove(endpoint, out _);
            return false;
        }

        cooldownUntil = expiresAt;
        return true;
    }

    private void PutEndpointCooldown(string endpoint, TimeSpan lifetime)
    {
        _endpointCooldowns[endpoint] = DateTimeOffset.UtcNow.Add(lifetime);
    }

    private static bool ShouldCooldownEndpoint(string endpoint, int responseCode)
    {
        return responseCode == 30041
            && endpoint.Contains("getDeviceInfoByDeviceCode", StringComparison.OrdinalIgnoreCase);
    }

    private static PreviewResolution ChoosePreferred(PreviewResolution? current, PreviewResolution candidate)
    {
        if (candidate.IsSuccess) return candidate;
        if (current is null) return candidate;

        return candidate.FailureCategoryPriority >= current.FailureCategoryPriority ? candidate : current;
    }

    private static string DetectProtocolFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "unknown";
        }

        var normalized = url.Trim().ToLowerInvariant();
        if (normalized.Contains(".flv", StringComparison.Ordinal)) return "flv";
        if (normalized.Contains(".m3u8", StringComparison.Ordinal)) return "hls";
        if (normalized.StartsWith("webrtc://", StringComparison.Ordinal)) return "webrtc";
        return "unknown";
    }

    private static string BuildUrl(string baseUrl, string endpoint)
    {
        return $"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";
    }

    private static bool TryGetCache<T>(ConcurrentDictionary<string, CacheItem<T>> store, string key, out T value)
    {
        if (store.TryGetValue(key, out var item) && item.ExpiresAt > DateTimeOffset.UtcNow)
        {
            value = item.Value;
            return true;
        }

        store.TryRemove(key, out _);
        value = default!;
        return false;
    }

    private static void PutCache<T>(ConcurrentDictionary<string, CacheItem<T>> store, string key, T value, TimeSpan ttl)
    {
        store[key] = new CacheItem<T>(value, DateTimeOffset.UtcNow.Add(ttl));
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var result) => result,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) => result,
            _ => null
        };
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var result) => result,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) => result,
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var result) => result,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) => result,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool? ReadBoolFlexible(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number switch
            {
                1 => true,
                0 => false,
                _ => null
            },
            JsonValueKind.String => NormalizeBool(value.GetString()),
            _ => null
        };

        static bool? NormalizeBool(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Trim() switch
            {
                "1" or "true" or "True" or "在线" => true,
                "0" or "false" or "False" or "离线" => false,
                _ => null
            };
        }
    }

    private static string MaskSensitive(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
        {
            return string.Empty;
        }

        var pairs = plain.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var index = part.IndexOf('=');
                if (index < 0) return part;
                var key = part[..index];
                var value = part[(index + 1)..];
                if (key.Equals("accessToken", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("refreshToken", StringComparison.OrdinalIgnoreCase))
                {
                    value = "redacted";
                }
                else if (key.Equals("enterpriseUser", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("parentUser", StringComparison.OrdinalIgnoreCase))
                {
                    value = MaskValue(value);
                }

                return $"{key}={value}";
            });

        return string.Join("&", pairs);
    }

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 6) return value;
        return string.Concat(value.AsSpan(0, 3), "****", value.AsSpan(value.Length - 3));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private sealed record CacheItem<T>(T Value, DateTimeOffset ExpiresAt);

    private sealed class TokenEnvelope
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public int RefreshExpiresIn { get; set; }
    }
}

public sealed class AcisKernelOptions
{
    public string WorkDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, ".acis-kernel");
    public CtyunOptions Ctyun { get; set; } = new();
    public AmapOptions Amap { get; set; } = new();
    public PreviewOptions Preview { get; set; } = new();
    public HttpOptions Http { get; set; } = new();
}

public sealed class HttpOptions
{
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class PreviewOptions
{
    public string[] ClickProtocolOrder { get; set; } = ["webrtc", "flv", "hls"];
    public string[] InspectionProtocolOrder { get; set; } = ["flv", "hls"];
}

public sealed class CtyunOptions
{
    public string BaseUrl { get; set; } = "https://api.ctyun.example";
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string EnterpriseUser { get; set; } = string.Empty;
    public string ClientType { get; set; } = "1";
    public string Version { get; set; } = "1.1";
    public string ApiVersion { get; set; } = "1.0";
    public string GrantType { get; set; } = "vcp_189";
    public int TokenReuseBeforeExpirySeconds { get; set; } = 60;
    public string RsaPrivateKeyPem { get; set; } = string.Empty;
    public CtyunEndpointOptions Endpoints { get; set; } = new();
}

public sealed class CtyunEndpointOptions
{
    public string GetAccessToken { get; set; } = "/open/oauth/getAccessToken";
    public string GetAllDeviceListNew { get; set; } = "/open/token/device/getAllDeviceListNew";
    public string GetCusDeviceByDeviceCode { get; set; } = "/open/token/device/getCusDeviceByDeviceCode";
    public string ShowDevice { get; set; } = "/open/token/device/showDevice";
    public string GetDeviceInfoByDeviceCode { get; set; } = "/open/token/device/getDeviceInfoByDeviceCode";
    public string GetDeviceAlarmMessage { get; set; } = "/open/token/device/getDeviceAlarmMessage";
    public string GetAiAlertInfoList { get; set; } = "/open/token/AIAlarm/getAlertInfoList";
    public string GetDeviceMediaUrlFlv { get; set; } = "/open/token/cloud/getDeviceMediaUrlFlv";
    public string GetDeviceMediaUrlHls { get; set; } = "/open/token/cloud/getDeviceMediaUrlHls";
    public string GetDeviceMediaWebrtcUrl { get; set; } = "/open/token/vpaas/getDeviceMediaWebrtcUrl";
    public string GetH5StreamUrl { get; set; } = "/open/token/vpaas/getH5StreamUrl";
}

public sealed class AmapOptions
{
    public string WebServiceKey { get; set; } = string.Empty;
    public string CoordinateConvertUrl { get; set; } = "https://restapi.amap.com/v3/assistant/coordinate/convert";
}

public sealed class TokenCacheEntry
{
    public string AppId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public int RefreshExpiresIn { get; set; }
    public DateTimeOffset AcquiredAtUtc { get; set; }
    public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; set; }
}


public sealed class ProtectedApiResult<T>
{
    public ProtectedApiResult(
        bool isSuccess,
        int responseCode,
        string responseMessage,
        T data,
        string rawResponse,
        string responseContentType,
        string path,
        string requestPayloadSummary,
        CtyunPayloadParseResult? payloadParserResult = null)
    {
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
        Data = data;
        RawResponse = rawResponse;
        ResponseContentType = responseContentType;
        Path = path;
        RequestPayloadSummary = requestPayloadSummary;
        PayloadParserResult = payloadParserResult;
    }

    public bool IsSuccess { get; }
    public int ResponseCode { get; }
    public string ResponseMessage { get; }
    public T Data { get; }
    public string RawResponse { get; }
    public string ResponseContentType { get; }
    public string Path { get; }
    public string RequestPayloadSummary { get; }
    public CtyunPayloadParseResult? PayloadParserResult { get; }
}

public sealed class DeviceCatalogPageResult
{
    public DeviceCatalogPageResult(bool isSuccess, int responseCode, string responseMessage, IReadOnlyList<DeviceCatalogItem> items, long nextLastId, int total, string rawResponse)
    {
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
        Items = items;
        NextLastId = nextLastId;
        Total = total;
        RawResponse = rawResponse;
    }

    public bool IsSuccess { get; }
    public int ResponseCode { get; }
    public string ResponseMessage { get; }
    public IReadOnlyList<DeviceCatalogItem> Items { get; }
    public long NextLastId { get; }
    public int Total { get; }
    public string RawResponse { get; }
}

public sealed class DeviceCatalogItem
{
    public string DeviceCode { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Raw { get; set; } = string.Empty;
}

public sealed class DeviceDetailResult
{
    public string DeviceCode { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Latitude { get; set; }
    public bool? IsOnline { get; set; }
    public string? LastSyncTime { get; set; }
    public List<DeviceDetailSnapshot> Snapshots { get; set; } = new();
    public string RawResponse { get; set; } = string.Empty;

    public bool HasCoordinate => Longitude.HasValue && Latitude.HasValue;
}

public sealed record DeviceDetailSnapshot(
    string Endpoint,
    bool IsSuccess,
    int ResponseCode,
    string ResponseMessage,
    string? DeviceCode,
    string? DeviceName,
    decimal? Longitude,
    decimal? Latitude,
    string RawResponse,
    bool? IsOnline = null,
    string? LastSyncTime = null);

public sealed class DeviceAlertBatchResult
{
    public DeviceAlertBatchResult(bool isSuccess, int responseCode, string responseMessage, IReadOnlyList<DeviceAlertItem> items, string rawResponse)
    {
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
        Items = items;
        RawResponse = rawResponse;
    }

    public bool IsSuccess { get; }
    public int ResponseCode { get; }
    public string ResponseMessage { get; }
    public IReadOnlyList<DeviceAlertItem> Items { get; }
    public string RawResponse { get; }
}

public sealed class DeviceAlertItem
{
    public string AlertId { get; set; } = string.Empty;
    public string AlertName { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string AlertTime { get; set; } = string.Empty;
    public string Raw { get; set; } = string.Empty;
}

public sealed class AiAlertBatchResult
{
    public AiAlertBatchResult(bool isSuccess, int responseCode, string responseMessage, string dataJson, string rawResponse)
    {
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
        DataJson = dataJson;
        RawResponse = rawResponse;
    }

    public bool IsSuccess { get; }
    public int ResponseCode { get; }
    public string ResponseMessage { get; }
    public string DataJson { get; }
    public string RawResponse { get; }
}

public sealed record GeoPoint(decimal Longitude, decimal Latitude);

public sealed class CoordinateConvertResult
{
    public bool IsSuccess { get; set; }
    public string Info { get; set; } = string.Empty;
    public IReadOnlyList<GeoPoint> Converted { get; set; } = Array.Empty<GeoPoint>();
    public string RawResponse { get; set; } = string.Empty;
}

public enum AcisPreviewIntent
{
    ClickPreview = 1,
    Inspection = 2
}

public enum PreviewFailureCategory
{
    NoStreamAddress = 1,
    ProtocolFallbackStillFailed = 2,
    StreamUrlParseFailed = 3,
    StreamUrlResolvedPlaybackFailed = 4,
    PlayerProtocolNotSupported = 5
}

public sealed record PreviewResolution(
    bool IsSuccess,
    string DeviceCode,
    string SelectedProtocol,
    string[] AttemptedProtocols,
    string MediaApiPath,
    int ResponseCode,
    string ResponseMessage,
    string? PreviewUrl,
    string? ParsedProtocolType,
    string StreamAcquireResult,
    PreviewFailureCategory FailureCategory,
    string FailureReason,
    int FailureCategoryPriority,
    CtyunPayloadParseResult? PayloadParser,
    string RawResponse)
{
    public static PreviewResolution Success(
        string deviceCode,
        string selectedProtocol,
        string[] attemptedProtocols,
        string mediaApiPath,
        int responseCode,
        string responseMessage,
        string previewUrl,
        string parsedProtocolType,
        CtyunPayloadParseResult? parser,
        string rawResponse)
        => new(
            true,
            deviceCode,
            selectedProtocol,
            attemptedProtocols,
            mediaApiPath,
            responseCode,
            responseMessage,
            previewUrl,
            parsedProtocolType,
            $"已获取{parsedProtocolType.ToUpperInvariant()}流地址",
            PreviewFailureCategory.NoStreamAddress,
            "none",
            0,
            parser,
            rawResponse);

    public static PreviewResolution Failure(
        string deviceCode,
        string selectedProtocol,
        string[] attemptedProtocols,
        string mediaApiPath,
        int responseCode,
        string responseMessage,
        string streamAcquireResult,
        PreviewFailureCategory failureCategory,
        string failureReason,
        CtyunPayloadParseResult? parser = null,
        string rawResponse = "",
        string? previewUrl = null,
        string? parsedProtocolType = null)
        => new(
            false,
            deviceCode,
            selectedProtocol,
            attemptedProtocols,
            mediaApiPath,
            responseCode,
            responseMessage,
            previewUrl,
            parsedProtocolType,
            streamAcquireResult,
            failureCategory,
            failureReason,
            (int)failureCategory,
            parser,
            rawResponse);
}

public sealed class PreviewHostRequest
{
    public string DeviceCode { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public sealed class PreviewHostResult
{
    public string Protocol { get; set; } = string.Empty;
    public string HtmlPath { get; set; } = string.Empty;
    public Uri HtmlUri { get; set; } = null!;
    public string SourceUrl { get; set; } = string.Empty;
}

public sealed class WebRtcPlayNegotiationResult
{
    public bool IsSuccess { get; private init; }
    public string ApiUrl { get; private init; } = string.Empty;
    public string? AnswerSdp { get; private init; }
    public int ResponseCode { get; private init; }
    public string? SessionId { get; private init; }
    public string? Server { get; private init; }
    public string FailureReason { get; private init; } = string.Empty;
    public string ResponseBody { get; private init; } = string.Empty;

    public static WebRtcPlayNegotiationResult Success(
        string apiUrl,
        string answerSdp,
        string? sessionId,
        string? server,
        string responseBody)
        => new()
        {
            IsSuccess = true,
            ApiUrl = apiUrl,
            AnswerSdp = answerSdp,
            ResponseCode = 0,
            SessionId = sessionId,
            Server = server,
            FailureReason = string.Empty,
            ResponseBody = responseBody
        };

    public static WebRtcPlayNegotiationResult Failure(
        string apiUrl,
        string failureReason,
        string responseBody,
        int responseCode = -1,
        string? sessionId = null,
        string? server = null)
        => new()
        {
            IsSuccess = false,
            ApiUrl = apiUrl,
            AnswerSdp = null,
            ResponseCode = responseCode,
            SessionId = sessionId,
            Server = server,
            FailureReason = failureReason,
            ResponseBody = responseBody
        };
}

public sealed class HostSupportResult
{
    public bool IsSupported { get; private init; }
    public string Reason { get; private init; } = string.Empty;

    public static HostSupportResult Supported() => new() { IsSupported = true, Reason = string.Empty };
    public static HostSupportResult Unsupported(string reason) => new() { IsSupported = false, Reason = reason };
}

public sealed class PreviewProtocolPlan
{
    public string Protocol { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public Func<string, string, string, IEnumerable<KeyValuePair<string, string>>> BuildParameters { get; init; } = null!;

    public static PreviewProtocolPlan Flv(string endpoint) => new()
    {
        Protocol = "flv",
        Endpoint = endpoint,
        BuildParameters = (accessToken, enterpriseUser, deviceCode) => new[]
        {
            new KeyValuePair<string, string>("accessToken", accessToken),
            new KeyValuePair<string, string>("enterpriseUser", enterpriseUser),
            new KeyValuePair<string, string>("deviceCode", deviceCode),
            new KeyValuePair<string, string>("mediaType", "0"),
            new KeyValuePair<string, string>("supportDomain", "1"),
            new KeyValuePair<string, string>("mute", "0"),
            new KeyValuePair<string, string>("netType", "0"),
            new KeyValuePair<string, string>("expire", "300")
        }
    };

    public static PreviewProtocolPlan Hls(string endpoint) => new()
    {
        Protocol = "hls",
        Endpoint = endpoint,
        BuildParameters = (accessToken, enterpriseUser, deviceCode) => new[]
        {
            new KeyValuePair<string, string>("accessToken", accessToken),
            new KeyValuePair<string, string>("enterpriseUser", enterpriseUser),
            new KeyValuePair<string, string>("deviceCode", deviceCode),
            new KeyValuePair<string, string>("mediaType", "0"),
            new KeyValuePair<string, string>("supportDomain", "1"),
            new KeyValuePair<string, string>("mute", "0"),
            new KeyValuePair<string, string>("netType", "0"),
            new KeyValuePair<string, string>("expire", "300")
        }
    };

    public static PreviewProtocolPlan WebRtc(string endpoint) => new()
    {
        Protocol = "webrtc",
        Endpoint = endpoint,
        BuildParameters = (accessToken, enterpriseUser, deviceCode) => new[]
        {
            new KeyValuePair<string, string>("accessToken", accessToken),
            new KeyValuePair<string, string>("enterpriseUser", enterpriseUser),
            new KeyValuePair<string, string>("deviceCode", deviceCode),
            new KeyValuePair<string, string>("mediaType", "0"),
            new KeyValuePair<string, string>("netType", "0"),
            new KeyValuePair<string, string>("channel", "0"),
            new KeyValuePair<string, string>("mute", "0")
        }
    };

    public static PreviewProtocolPlan H5(string endpoint) => new()
    {
        Protocol = "h5",
        Endpoint = endpoint,
        BuildParameters = (accessToken, enterpriseUser, deviceCode) => new[]
        {
            new KeyValuePair<string, string>("accessToken", accessToken),
            new KeyValuePair<string, string>("enterpriseUser", enterpriseUser),
            new KeyValuePair<string, string>("deviceCode", deviceCode),
            new KeyValuePair<string, string>("mediaType", "0"),
            new KeyValuePair<string, string>("mute", "0"),
            new KeyValuePair<string, string>("playerType", "0"),
            new KeyValuePair<string, string>("wasm", "1"),
            new KeyValuePair<string, string>("allLiveUrl", "1")
        }
    };
}

public sealed class CtyunPayloadParseResult
{
    public JsonElement? ResolvedJson { get; set; }
    public string? MatchedFieldPath { get; set; }
    public string? MatchedUrl { get; set; }
    public string? DecryptMode { get; set; }
    public string? ParsedProtocolType { get; set; }
    public string? OriginalDataRaw { get; set; }
    public string? FailureReason { get; set; }
    public string ResponseEnvelopeShape { get; set; } = string.Empty;
    public string ResponseJsonTopLevelKeys { get; set; } = string.Empty;
    public string ResponseCandidateUrlKeys { get; set; } = string.Empty;
    public string ResponseNestedPathTried { get; set; } = string.Empty;
    public string ResponseBodyPreviewFirst300 { get; set; } = string.Empty;
}

internal static class CtyunPayloadParser
{
    private static readonly string[] PreviewUrlPropertyNames =
    [
        "rtcUrl", "webrtcUrl", "webRtcUrl", "url", "streamUrl", "playUrl",
        "mediaUrl", "flvUrl", "hlsUrl", "previewUrl", "h5Url", "liveUrl", "httpUrl", "httpsUrl"
    ];

    public static CtyunPayloadParseResult TryResolvePayload(string rawData, string rsaPrivateKeyPem, string xxteaKey)
    {
        var result = new CtyunPayloadParseResult
        {
            OriginalDataRaw = rawData,
            ResponseBodyPreviewFirst300 = rawData is null ? string.Empty : rawData[..Math.Min(300, rawData.Length)]
        };

        if (string.IsNullOrWhiteSpace(rawData))
        {
            result.FailureReason = "响应 data 为空。";
            return result;
        }

        var candidates = new List<(string mode, string text)>
        {
            ("none", rawData)
        };

        if (TryParseJson(rawData, out _))
        {
            // 已加入 none 候选
        }
        else
        {
            if (LooksLikeHex(rawData))
            {
                try
                {
                    candidates.Add(("rsa-hex-pkcs1", CtyunRsaDecryptor.Decrypt(rawData, rsaPrivateKeyPem)));
                }
                catch
                {
                    // ignore
                }

                try
                {
                    candidates.Add(("xxtea-hex", XXTeaCryptor.DecryptFromHex(rawData, xxteaKey)));
                }
                catch
                {
                    // ignore
                }
            }

            if (LooksLikeBase64(rawData))
            {
                try
                {
                    candidates.Add(("rsa-base64-pkcs1", CtyunRsaDecryptor.Decrypt(rawData, rsaPrivateKeyPem)));
                }
                catch
                {
                    // ignore
                }
            }
        }

        foreach (var candidate in candidates.DistinctBy(x => $"{x.mode}|{x.text}"))
        {
            if (!TryParseJson(candidate.text, out var json))
            {
                continue;
            }

            var root = json!.RootElement.Clone();
            result.ResolvedJson = root;
            result.DecryptMode = candidate.mode;
            result.ResponseJsonTopLevelKeys = DescribeKeys(root);
            result.ResponseEnvelopeShape = DescribeEnvelopeShape(root);
            result.ResponseNestedPathTried = "root";
            var hit = FindFirstUrl(root, "payload");
            if (hit is not null)
            {
                result.MatchedFieldPath = hit.Value.Path;
                result.MatchedUrl = hit.Value.Url;
                result.ParsedProtocolType = DetectProtocol(hit.Value.Url);
                result.ResponseCandidateUrlKeys = hit.Value.Path;
                return result;
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.String)
                {
                    var nestedText = data.GetString() ?? string.Empty;
                    if (TryParseJson(nestedText, out var nestedJson))
                    {
                        var nestedRoot = nestedJson!.RootElement.Clone();
                        result.ResolvedJson = nestedRoot;
                        result.ResponseNestedPathTried = "root -> root.data[plain-json]";
                        var nestedHit = FindFirstUrl(nestedRoot, "payload");
                        if (nestedHit is not null)
                        {
                            result.MatchedFieldPath = nestedHit.Value.Path;
                            result.MatchedUrl = nestedHit.Value.Url;
                            result.ParsedProtocolType = DetectProtocol(nestedHit.Value.Url);
                            result.ResponseCandidateUrlKeys = nestedHit.Value.Path;
                            return result;
                        }
                    }
                }
            }
        }

        result.FailureReason = "预览地址接口未返回可解析的数据。";
        return result;
    }

    private static (string Path, string Url)? FindFirstUrl(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var nextPath = $"{path}.{property.Name}";
                    if (PreviewUrlPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        var url = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return (nextPath, url!);
                        }
                    }

                    var nested = FindFirstUrl(property.Value, nextPath);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstUrl(item, $"{path}[{index}]");
                    if (nested is not null)
                    {
                        return nested;
                    }

                    index++;
                }

                break;
        }

        return null;
    }

    private static bool TryParseJson(string text, out JsonDocument? json)
    {
        try
        {
            json = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            json = null;
            return false;
        }
    }

    private static bool LooksLikeHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length % 2 != 0) return false;
        foreach (var ch in text)
        {
            if (!Uri.IsHexDigit(ch)) return false;
        }

        return true;
    }

    private static bool LooksLikeBase64(string text)
    {
        try
        {
            Convert.FromBase64String(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeKeys(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            ? string.Join(", ", root.EnumerateObject().Select(x => x.Name))
            : root.ValueKind.ToString();
    }

    private static string DescribeEnvelopeShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return $"root:{root.ValueKind}";
        }

        var rootKeys = string.Join(",", root.EnumerateObject().Select(x => x.Name));
        var parts = new List<string> { $"root:Object({rootKeys})" };
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.String)
            {
                parts.Add("root.data[String]");
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                parts.Add($"root.data:Object({string.Join(",", data.EnumerateObject().Select(x => x.Name))})");
            }
        }

        return string.Join(" -> ", parts);
    }

    private static string DetectProtocol(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "unknown";
        }

        var normalized = url.Trim().ToLowerInvariant();
        if (normalized.StartsWith("webrtc://", StringComparison.Ordinal)) return "webrtc";
        if (normalized.Contains(".flv", StringComparison.Ordinal)) return "flv";
        if (normalized.Contains(".m3u8", StringComparison.Ordinal)) return "hls";
        return "unknown";
    }
}

internal static class CtyunSecurity
{
    public static string BuildSignature(
        string appId,
        string clientType,
        string encryptedParams,
        string timestamp,
        string version,
        string appSecret)
    {
        var signatureSource = string.Concat(
            appId?.Trim() ?? string.Empty,
            clientType?.Trim() ?? string.Empty,
            encryptedParams?.Trim() ?? string.Empty,
            timestamp?.Trim() ?? string.Empty,
            version?.Trim() ?? string.Empty);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureSource));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string EncryptParams(IReadOnlyList<KeyValuePair<string, string>> parameters, string appSecret)
    {
        var plain = BuildPrivateParameterString(parameters);
        return XXTeaCryptor.EncryptToHex(Encoding.UTF8.GetBytes(plain), Encoding.UTF8.GetBytes(appSecret));
    }

    public static string BuildPrivateParameterString(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join("&", parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => $"{x.Key}={x.Value}"));
    }
}

internal static class CtyunRsaDecryptor
{
    public static string Decrypt(string cipherText, string privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return string.Empty;
        }

        var cipherBytes = IsHex(cipherText)
            ? Convert.FromHexString(cipherText)
            : Convert.FromBase64String(cipherText);

        using var rsa = RSA.Create();
        ImportPrivateKey(rsa, privateKeyPem);

        var blockSize = rsa.KeySize / 8;
        using var output = new MemoryStream();
        for (var offset = 0; offset < cipherBytes.Length; offset += blockSize)
        {
            var length = Math.Min(blockSize, cipherBytes.Length - offset);
            var segment = new byte[length];
            Buffer.BlockCopy(cipherBytes, offset, segment, 0, length);
            var plain = rsa.Decrypt(segment, RSAEncryptionPadding.Pkcs1);
            output.Write(plain, 0, plain.Length);
        }

        return Encoding.UTF8.GetString(output.ToArray()).TrimEnd('\0');
    }

    private static void ImportPrivateKey(RSA rsa, string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException("未配置 RSA 私钥。");
        }

        var normalized = pem.Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Contains("BEGIN RSA PRIVATE KEY", StringComparison.Ordinal))
        {
            rsa.ImportFromPem(normalized);
            return;
        }

        if (normalized.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal))
        {
            rsa.ImportFromPem(normalized);
            return;
        }

        var bytes = Convert.FromBase64String(normalized);
        try
        {
            rsa.ImportPkcs8PrivateKey(bytes, out _);
        }
        catch
        {
            rsa.ImportRSAPrivateKey(bytes, out _);
        }
    }

    private static bool IsHex(string text)
    {
        return text.Length % 2 == 0 && text.All(Uri.IsHexDigit);
    }
}

internal static class XXTeaCryptor
{
    private const uint Delta = 0x9E3779B9;

    public static string EncryptToHex(byte[] data, byte[] key)
    {
        if (data.Length == 0) return string.Empty;
        var encrypted = Encrypt(data, key);
        return Convert.ToHexString(encrypted);
    }

    public static string DecryptFromHex(string hex, string key)
    {
        var bytes = Convert.FromHexString(hex);
        return Encoding.UTF8.GetString(Decrypt(bytes, Encoding.UTF8.GetBytes(key))).TrimEnd('\0');
    }

    private static byte[] Encrypt(byte[] data, byte[] key)
    {
        var v = ToUInt32Array(data, true);
        var k = ToUInt32Array(FixKey(key), false);
        var n = v.Length - 1;
        uint z = v[n], y = v[0], sum = 0, e, p, q = (uint)(6 + 52 / (n + 1));
        while (q-- > 0)
        {
            sum += Delta;
            e = (sum >> 2) & 3;
            for (p = 0; p < n; p++)
            {
                y = v[p + 1];
                z = v[p] += Mx(sum, y, z, p, e, k);
            }

            y = v[0];
            z = v[n] += Mx(sum, y, z, p, e, k);
        }

        return ToByteArray(v, false);
    }

    private static byte[] Decrypt(byte[] data, byte[] key)
    {
        var v = ToUInt32Array(data, false);
        var k = ToUInt32Array(FixKey(key), false);
        var n = v.Length - 1;
        uint z = v[n], y = v[0], sum, e, p, q = (uint)(6 + 52 / (n + 1));
        sum = q * Delta;
        while (sum != 0)
        {
            e = (sum >> 2) & 3;
            for (p = (uint)n; p > 0; p--)
            {
                z = v[p - 1];
                y = v[p] -= Mx(sum, y, z, p - 1, e, k);
            }

            z = v[n];
            y = v[0] -= Mx(sum, y, z, p, e, k);
            sum -= Delta;
        }

        return ToByteArray(v, true);
    }

    private static uint Mx(uint sum, uint y, uint z, uint p, uint e, uint[] k)
    {
        return ((z >> 5 ^ y << 2) + (y >> 3 ^ z << 4)) ^ ((sum ^ y) + (k[(p & 3) ^ e] ^ z));
    }

    private static byte[] FixKey(byte[] key)
    {
        if (key.Length == 16) return key;
        var fixedKey = new byte[16];
        Buffer.BlockCopy(key, 0, fixedKey, 0, Math.Min(key.Length, fixedKey.Length));
        return fixedKey;
    }

    private static uint[] ToUInt32Array(byte[] data, bool includeLength)
    {
        var length = data.Length;
        var n = ((length & 3) == 0 ? length >> 2 : (length >> 2) + 1);
        var result = includeLength ? new uint[n + 1] : new uint[n];
        if (includeLength) result[n] = (uint)length;
        for (var i = 0; i < length; i++)
        {
            result[i >> 2] |= (uint)data[i] << ((i & 3) << 3);
        }

        return result;
    }

    private static byte[] ToByteArray(uint[] data, bool includeLength)
    {
        var n = data.Length << 2;
        if (includeLength)
        {
            var length = (int)data[^1];
            if (length < n - 7 || length > n - 4)
            {
                throw new InvalidOperationException("XXTea 解密结果长度无效。");
            }

            n = length;
        }

        var result = new byte[n];
        for (var i = 0; i < n; i++)
        {
            result[i] = (byte)(data[i >> 2] >> ((i & 3) << 3));
        }

        return result;
    }
}

public interface IAcisKernelLogger
{
    void Info(string category, string message);
    void Warn(string category, string message);
    Task WriteAsync(string category, string message, CancellationToken cancellationToken = default);
}

public sealed class FileAcisKernelLogger : IAcisKernelLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public FileAcisKernelLogger(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? AppContext.BaseDirectory);
    }

    public void Info(string category, string message) => WriteLine(category, message);
    public void Warn(string category, string message) => WriteLine(category, message);

    public async Task WriteAsync(string category, string message, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(
                _path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    private void WriteLine(string category, string message)
    {
        WriteAsync(category, message).GetAwaiter().GetResult();
    }
}

internal static class LegacyPreviewHostHtmlBuilder
{
    public static string Build(string deviceCode, string sourceUrl, string protocol, string? title)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? deviceCode : title;
        var safeUrl = JsonSerializer.Serialize(sourceUrl);
        var safeProtocol = JsonSerializer.Serialize(protocol);
        var safeDeviceCode = JsonSerializer.Serialize(deviceCode);
        var safeTitleJson = JsonSerializer.Serialize(safeTitle);

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta http-equiv="X-UA-Compatible" content="IE=edge" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>{{safeTitle}}</title>
<style>
html, body { margin:0; padding:0; width:100%; height:100%; background:#000; color:#fff; font-family:Segoe UI, Arial, sans-serif; }
#root { width:100%; height:100%; position:relative; overflow:hidden; background:#000; }
video { width:100%; height:100%; object-fit:contain; background:#000; }
#status { position:absolute; left:8px; top:8px; right:8px; z-index:9; font-size:12px; line-height:1.4; background:rgba(0,0,0,.45); padding:6px 8px; border-radius:6px; }
</style>
<script src="https://cdn.jsdelivr.net/npm/hls.js@1.5.18/dist/hls.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/mpegts.js@1.8.0/dist/mpegts.min.js"></script>
</head>
<body>
<div id="root">
  <div id="status">初始化中...</div>
  <video id="player" controls autoplay muted playsinline></video>
</div>
<script>
const sourceUrl = {{safeUrl}};
const protocol = {{safeProtocol}};
const deviceCode = {{safeDeviceCode}};
const title = {{safeTitleJson}};
const statusEl = document.getElementById("status");
const video = document.getElementById("player");

function send(msg) {
  const payload = typeof msg === "object" ? msg : { type: "text", value: String(msg) };
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(payload);
    } else {
      console.log("preview-host-message", payload);
    }
  } catch (e) {
    console.error(e);
  }
}

function setStatus(text) {
  statusEl.textContent = text;
  send({ type: "probe_status", deviceCode, protocol, text });
}

function fail(category, reason) {
  setStatus("播放失败：" + reason);
  send({ type: "playback_failed", deviceCode, protocol, category, reason, sourceUrl });
}

function ready() {
  setStatus("播放正常");
  send({ type: "playback_ready", deviceCode, protocol, sourceUrl });
}

function stalled() {
  setStatus("播放警告：stalled");
  send({ type: "playback_warning", deviceCode, protocol, warning: "stalled", sourceUrl });
}

video.addEventListener("loadedmetadata", () => send({ type: "loadedmetadata", duration: video.duration }));
video.addEventListener("playing", ready);
video.addEventListener("stalled", stalled);
video.addEventListener("error", () => fail("player_load_failed", "video.error triggered"));

(async function bootstrap() {
  send({ type: "navigation_started", deviceCode, protocol, sourceUrl, title });

  if (protocol === "flv") {
    if (!(window.mpegts && window.mpegts.isSupported())) {
      fail("player_not_supported", "mpegts.js not supported");
      return;
    }
    const player = window.mpegts.createPlayer({ type: "flv", isLive: true, url: sourceUrl });
    player.attachMediaElement(video);
    player.load();
    player.play().catch(err => fail("player_load_failed", String(err)));
    setStatus("FLV 加载中...");
    return;
  }

  if (protocol === "hls") {
    if (video.canPlayType("application/vnd.apple.mpegurl")) {
      video.src = sourceUrl;
      video.play().catch(err => fail("player_load_failed", String(err)));
      setStatus("HLS 直连加载中...");
      return;
    }
    if (window.Hls && window.Hls.isSupported()) {
      const hls = new Hls({ liveDurationInfinity: true, enableWorker: true });
      hls.on(Hls.Events.ERROR, (_, data) => {
        if (data && data.fatal) {
          fail("player_load_failed", data.type + ":" + data.details);
        }
      });
      hls.loadSource(sourceUrl);
      hls.attachMedia(video);
      video.play().catch(err => fail("player_load_failed", String(err)));
      setStatus("HLS.js 加载中...");
      return;
    }
    fail("player_not_supported", "HLS not supported");
    return;
  }

  if (protocol === "webrtc") {
    setStatus("WebRTC 预览宿主已迁移到新版实现");
    return;
  }

  if (protocol === "h5") {
    location.href = sourceUrl;
    return;
  }

  fail("url_unloadable", "unknown protocol");
})();
</script>
</body>
</html>
""";
    }
}

internal static class PreviewHostHtmlBuilder
{
    public static string Build(string deviceCode, string sourceUrl, string protocol, string? title)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? deviceCode : title;
        var safeUrl = JsonSerializer.Serialize(sourceUrl);
        var safeProtocol = JsonSerializer.Serialize(protocol);
        var safeDeviceCode = JsonSerializer.Serialize(deviceCode);
        var safeTitleJson = JsonSerializer.Serialize(safeTitle);
        var safeWebRtcApiUrl = JsonSerializer.Serialize(
            string.Equals(protocol, "webrtc", StringComparison.OrdinalIgnoreCase)
                ? AcisApiKernel.BuildWebRtcPlayApiUrl(sourceUrl)
                : null);
        var readyTimeoutSeconds = string.Equals(protocol, "webrtc", StringComparison.OrdinalIgnoreCase) ? "12" : "10";

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta http-equiv="X-UA-Compatible" content="IE=edge" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>{{safeTitle}}</title>
<style>
html, body { margin:0; padding:0; width:100%; height:100%; background:#04090f; color:#d7e6ff; font-family:"Segoe UI","Microsoft YaHei",sans-serif; }
body { overflow:hidden; }
#root { width:100%; height:100%; position:relative; background:radial-gradient(circle at top left, rgba(39,88,156,.32), transparent 34%), radial-gradient(circle at bottom right, rgba(10,38,78,.45), transparent 40%), #04090f; }
video { width:100%; height:100%; object-fit:cover; background:#000; }
#status { position:absolute; left:12px; right:12px; top:12px; z-index:2; padding:9px 12px; border:1px solid rgba(103,153,238,.22); border-radius:999px; background:rgba(5,10,19,.7); color:#cfe0ff; font-size:12px; line-height:1.4; letter-spacing:.02em; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
</style>
<script src="https://cdn.jsdelivr.net/npm/hls.js@1.5.18/dist/hls.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/mpegts.js@1.8.0/dist/mpegts.min.js"></script>
</head>
<body>
<div id="root">
  <div id="status">等待预览连接。</div>
  <video id="player" controls autoplay muted playsinline></video>
</div>
<script>
const session = {
  deviceCode: {{safeDeviceCode}},
  protocol: {{safeProtocol}},
  sourceUrl: {{safeUrl}},
  title: {{safeTitleJson}},
  webRtcApiUrl: {{safeWebRtcApiUrl}},
  readyTimeoutSeconds: {{readyTimeoutSeconds}}
};
const statusEl = document.getElementById("status");
const video = document.getElementById("player");
const state = { adapter: null, ready: false, readyTimer: null };

function send(message) {
  const payload = typeof message === "object" ? message : { type: "text", value: String(message) };
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(payload);
    } else {
      console.log("preview-host-message", payload);
    }
  } catch (error) {
    console.error(error);
  }
}

function setStatus(text) {
  statusEl.textContent = text;
}

function clearReadyTimer() {
  if (state.readyTimer) {
    window.clearTimeout(state.readyTimer);
    state.readyTimer = null;
  }
}

function resetVideo() {
  try {
    video.pause();
  } catch (_) {
  }

  video.removeAttribute("src");
  video.srcObject = null;
  video.load();
}

function teardownAdapter() {
  clearReadyTimer();

  if (state.adapter && typeof state.adapter.stop === "function") {
    try {
      state.adapter.stop();
    } catch (_) {
    }
  }

  state.adapter = null;
  state.ready = false;
  resetVideo();
}

function fail(category, reason) {
  teardownAdapter();
  setStatus(session.protocol === "webrtc" ? "WebRTC 预览连接失败。" : "备用预览连接失败。");
  send({ type: "playback_failed", deviceCode: session.deviceCode, protocol: session.protocol, category, reason, sourceUrl: session.sourceUrl });
}

function markReady() {
  if (state.ready) {
    return;
  }

  state.ready = true;
  clearReadyTimer();
  setStatus(session.protocol === "webrtc" ? "WebRTC 实时预览已连接。" : "备用预览已连接。");
  send({ type: "playback_ready", deviceCode: session.deviceCode, protocol: session.protocol, sourceUrl: session.sourceUrl });
}

function startReadyTimer(seconds, category) {
  clearReadyTimer();
  state.readyTimer = window.setTimeout(() => {
    if (!state.ready) {
      fail(category || "ready_timeout", "ready timeout");
    }
  }, Math.max(4, seconds || 10) * 1000);
}

function waitForIceGatheringComplete(peer, timeoutMs) {
  if (peer.iceGatheringState === "complete") {
    return Promise.resolve();
  }

  return new Promise(resolve => {
    let settled = false;
    const finish = () => {
      if (settled) {
        return;
      }

      settled = true;
      peer.removeEventListener("icegatheringstatechange", handleStateChange);
      if (timerId) {
        window.clearTimeout(timerId);
      }
      resolve();
    };

    const handleStateChange = () => {
      if (peer.iceGatheringState === "complete") {
        finish();
      }
    };

    const timerId = window.setTimeout(finish, timeoutMs);
    peer.addEventListener("icegatheringstatechange", handleStateChange);
  });
}

function createWebRtcPlaybackHost() {
  let peer = null;
  let mediaStream = null;

  return {
    async start() {
      if (!session.webRtcApiUrl) {
        fail("webrtc_api_missing", "missing webrtc api url");
        return;
      }

      setStatus("WebRTC 预览连接中。");
      send({ type: "host_initialized", deviceCode: session.deviceCode, protocol: "webrtc" });

      peer = new RTCPeerConnection({ bundlePolicy: "max-bundle", rtcpMuxPolicy: "require" });
      mediaStream = new MediaStream();
      video.srcObject = mediaStream;

      peer.ontrack = event => {
        mediaStream.addTrack(event.track);
        video.play().catch(() => {});
      };

      peer.onconnectionstatechange = () => {
        if (peer.connectionState === "connected") {
          video.play().catch(() => {});
          markReady();
          return;
        }

        if (!state.ready && ["failed", "disconnected", "closed"].includes(peer.connectionState)) {
          fail("webrtc_connection_failed", peer.connectionState);
        }
      };

      peer.oniceconnectionstatechange = () => {
        if (!state.ready && ["failed", "disconnected", "closed"].includes(peer.iceConnectionState)) {
          fail("webrtc_ice_failed", peer.iceConnectionState);
        }
      };

      peer.addTransceiver("audio", { direction: "recvonly" });
      peer.addTransceiver("video", { direction: "recvonly" });

      const offer = await peer.createOffer();
      await peer.setLocalDescription(offer);
      await waitForIceGatheringComplete(peer, 1500);

      const payload = JSON.stringify({
        api: session.webRtcApiUrl,
        streamurl: session.sourceUrl,
        clientip: null,
        sdp: peer.localDescription ? peer.localDescription.sdp : null
      });

      if (!peer.localDescription || !peer.localDescription.sdp) {
        fail("webrtc_offer_missing", "missing local description");
        return;
      }

      startReadyTimer(session.readyTimeoutSeconds, "webrtc_ready_timeout");

      const response = await fetch(session.webRtcApiUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: payload
      });
      const body = await response.text();

      if (!response.ok) {
        fail("webrtc_answer_failed", `http_${response.status}`);
        return;
      }

      const result = JSON.parse(body);
      if (!result || result.code !== 0 || !result.sdp) {
        fail("webrtc_answer_failed", result && (result.msg || result.message) ? result.msg || result.message : "missing answer");
        return;
      }

      await peer.setRemoteDescription({ type: "answer", sdp: result.sdp });
    },

    stop() {
      if (peer) {
        try {
          peer.ontrack = null;
          peer.onconnectionstatechange = null;
          peer.oniceconnectionstatechange = null;
          peer.close();
        } catch (_) {
        }
      }

      if (mediaStream) {
        mediaStream.getTracks().forEach(track => {
          try {
            track.stop();
          } catch (_) {
          }
        });
      }

      peer = null;
      mediaStream = null;
    }
  };
}

function createMediaPlaybackHost(kind) {
  let cleanup = null;

  return {
    async start() {
      send({ type: "host_initialized", deviceCode: session.deviceCode, protocol: kind });
      setStatus("正在建立备用预览。");

      if (kind === "flv") {
        if (!(window.mpegts && window.mpegts.isSupported())) {
          fail("flv_not_supported", "mpegts.js unavailable");
          return;
        }

        const player = window.mpegts.createPlayer({ type: "flv", isLive: true, url: session.sourceUrl });
        player.attachMediaElement(video);
        player.load();
        player.play().catch(() => fail("flv_play_failed", "play failed"));
        player.on(window.mpegts.Events.ERROR, () => fail("flv_stream_failed", "stream failed"));
        cleanup = () => {
          try { player.pause(); } catch (_) {}
          try { player.unload(); } catch (_) {}
          try { player.detachMediaElement(); } catch (_) {}
          try { player.destroy(); } catch (_) {}
        };
        startReadyTimer(session.readyTimeoutSeconds, "flv_ready_timeout");
        return;
      }

      if (kind === "hls") {
        if (video.canPlayType("application/vnd.apple.mpegurl")) {
          video.src = session.sourceUrl;
          video.play().catch(() => fail("hls_play_failed", "play failed"));
          cleanup = () => resetVideo();
          startReadyTimer(session.readyTimeoutSeconds, "hls_ready_timeout");
          return;
        }

        if (!(window.Hls && window.Hls.isSupported())) {
          fail("hls_not_supported", "hls.js unavailable");
          return;
        }

        const hls = new Hls({ liveDurationInfinity: true, enableWorker: true });
        hls.on(Hls.Events.ERROR, (_, data) => {
          if (data && data.fatal) {
            fail("hls_stream_failed", data.details || "stream failed");
          }
        });
        hls.loadSource(session.sourceUrl);
        hls.attachMedia(video);
        video.play().catch(() => fail("hls_play_failed", "play failed"));
        cleanup = () => {
          try { hls.destroy(); } catch (_) {}
        };
        startReadyTimer(session.readyTimeoutSeconds, "hls_ready_timeout");
        return;
      }

      fail("unsupported_protocol", kind);
    },

    stop() {
      if (cleanup) {
        try {
          cleanup();
        } catch (_) {
        }
      }

      cleanup = null;
    }
  };
}

video.addEventListener("playing", () => markReady());
video.addEventListener("error", () => {
  if (!state.ready) {
    fail("media_error", "video error");
  }
});

window.addEventListener("beforeunload", () => teardownAdapter());

(async function bootstrap() {
  send({ type: "navigation_started", deviceCode: session.deviceCode, protocol: session.protocol, sourceUrl: session.sourceUrl, title: session.title });

  if (session.protocol === "h5") {
    window.location.href = session.sourceUrl;
    return;
  }

  state.adapter = session.protocol === "webrtc"
    ? createWebRtcPlaybackHost()
    : createMediaPlaybackHost(session.protocol);

  if (!state.adapter) {
    fail("unsupported_protocol", session.protocol);
    return;
  }

  try {
    await state.adapter.start();
  } catch (error) {
    fail("host_start_failed", error && error.message ? error.message : String(error));
  }
})();
</script>
</body>
</html>
""";
    }
}
