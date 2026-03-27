using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tysl.Ai.Core.Models;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views.Controls;

public partial class PreviewHostControl : UserControl, IDisposable
{
    private const string HostName = "preview.tysl.local";
    private const string HostUrl = $"https://{HostName}/index.html";
    private const string ProxyHostName = "preview-proxy.tysl.local";
    private const string HlsProxyPath = "/hls-proxy";
    private const string HostPageModeDefault = "default";
    private const string HostPageModeFallbackOnly = "fallback-only";
    private static readonly Regex HlsManifestUriAttributeRegex = new(@"URI=""(?<uri>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly DependencyProperty SessionJsonProperty =
        DependencyProperty.Register(
            nameof(SessionJson),
            typeof(string),
            typeof(PreviewHostControl),
            new PropertyMetadata(null, HandleSessionJsonChanged));

    private PreviewPlaybackSessionDto? activeSession;
    private readonly Dictionary<string, MediaRequestTrace> mediaRequestTraces = new(StringComparer.OrdinalIgnoreCase);
    private bool browserReady;
    private bool hasShownRuntimeFailure;
    private bool isApplyingSession;
    private bool isDisposed;
    private bool isInitializing;
    private string currentHostPageMode = HostPageModeDefault;
    private CancellationTokenSource? negotiationCts;
    private CoreWebView2DevToolsProtocolEventReceiver? networkLoadingFailedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? networkRequestWillBeSentExtraInfoReceiver;
    private string? pendingHostPageMode;
    private string? pendingHostPageProtocol;
    private string? pendingSessionJson;
    private CoreWebView2DevToolsProtocolEventReceiver? networkRequestWillBeSentReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? networkResponseReceivedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? networkResponseReceivedExtraInfoReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? securityCertificateErrorReceiver;
    private TaskCompletionSource<bool>? stopPlaybackCompletionSource;
    private string? stopPlaybackSessionId;

    public PreviewHostControl()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
    }

    public event EventHandler<PreviewHostInitializedEventArgs>? HostInitialized;

    public event EventHandler<PreviewPlaybackReadyEventArgs>? PlaybackReady;

    public event EventHandler<PreviewPlaybackFailedEventArgs>? PlaybackFailed;

    public ILocalDiagnosticService? DiagnosticService { get; set; }

    public ISitePreviewService? PreviewService { get; set; }

    public string? SessionJson
    {
        get => (string?)GetValue(SessionJsonProperty);
        set => SetValue(SessionJsonProperty, value);
    }

    private static void HandleSessionJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PreviewHostControl control)
        {
            return;
        }

        control.pendingSessionJson = e.NewValue as string;
        _ = control.ApplyPendingSessionAsync();
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureInitializedAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (isDisposed || !IsLoaded || isInitializing)
        {
            return;
        }

        if (Browser.CoreWebView2 is not null)
        {
            return;
        }

        var assetDirectory = ResolveAssetDirectory();
        if (assetDirectory is null)
        {
            ShowFailureState();
            return;
        }

        isInitializing = true;
        try
        {
            await Browser.EnsureCoreWebView2Async();

            if (Browser.CoreWebView2 is null)
            {
                ShowFailureState();
                return;
            }

            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostName,
                assetDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            Browser.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;
            Browser.CoreWebView2.NavigationCompleted += HandleNavigationCompleted;
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.AddWebResourceRequestedFilter(
                $"{Uri.UriSchemeHttps}://{ProxyHostName}{HlsProxyPath}*",
                CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += HandleWebResourceRequested;
            Browser.CoreWebView2.WebResourceResponseReceived += HandleWebResourceResponseReceived;

            try
            {
                await EnsureDevToolsNetworkDiagnosticsAsync(Browser.CoreWebView2);
            }
            catch (Exception ex)
            {
                WriteDiagnostic(
                    "preview-host-network-diagnostic-unavailable",
                    $"reason={ex.Message}");
            }

            NavigateHostPage();
        }
        catch
        {
            ShowFailureState();
        }
        finally
        {
            isInitializing = false;
        }
    }

    private async Task ApplyPendingSessionAsync()
    {
        if (isDisposed || !browserReady || Browser.CoreWebView2 is null || isApplyingSession)
        {
            return;
        }

        isApplyingSession = true;
        try
        {
            while (true)
            {
                var json = pendingSessionJson;
                pendingSessionJson = null;
                var previousSession = activeSession;

                CancelNegotiation();

                if (string.IsNullOrWhiteSpace(json))
                {
                    WriteHostLifecycleDiagnostic(
                        "preview-host-stop-requested",
                        "ApplyPendingSessionAsync",
                        "close_preview",
                        currentSession: previousSession);
                    activeSession = null;
                    await Browser.ExecuteScriptAsync("window.TyslPreviewHost?.stop('close_preview');");
                    ShowOverlay("点击点位后自动预览。");
                }
                else
                {
                    var nextSession = JsonSerializer.Deserialize<PreviewPlaybackSessionDto>(json, JsonOptions);
                    if (nextSession is null)
                    {
                        ShowFailureState();
                        return;
                    }

                    if (ShouldResetHostPage(previousSession, nextSession))
                    {
                        WriteHostLifecycleDiagnostic(
                            "preview-host-switch-requested",
                            "ApplyPendingSessionAsync",
                            "host_page_reset",
                            previousSession: previousSession,
                            nextSession: nextSession,
                            isSessionSwitch: true);
                        PrepareHostPageReset(previousSession, nextSession);
                        pendingSessionJson = json;
                        activeSession = null;
                        await ResetHostPageAsync();
                        return;
                    }

                    activeSession = nextSession;
                    activeSession.ProxySourceUrl = string.Equals(activeSession.Protocol, "hls", StringComparison.OrdinalIgnoreCase)
                        ? BuildHlsProxyUrl(activeSession.SourceUrl)
                        : null;
                    hasShownRuntimeFailure = false;
                    ShowOverlay(null);
                    WriteHostLifecycleDiagnostic(
                        "preview-host-play-requested",
                        "ApplyPendingSessionAsync",
                        "play_new_session",
                        currentSession: nextSession,
                        previousSession: previousSession,
                        nextSession: nextSession,
                        isSessionSwitch: previousSession is not null);
                    var sessionLiteral = JsonSerializer.Serialize(activeSession, JsonOptions);
                    await Browser.ExecuteScriptAsync($"window.TyslPreviewHost?.play({sessionLiteral});");
                }

                if (pendingSessionJson is null)
                {
                    break;
                }
            }
        }
        catch
        {
            ShowFailureState();
        }
        finally
        {
            isApplyingSession = false;
        }
    }

    private bool ShouldResetHostPage(PreviewPlaybackSessionDto? previousSession, PreviewPlaybackSessionDto nextSession)
    {
        if (Browser.CoreWebView2 is null)
        {
            return false;
        }

        if (hasShownRuntimeFailure && IsFallbackProtocol(nextSession.Protocol))
        {
            return true;
        }

        return previousSession is not null
               && (IsFallbackProtocol(previousSession.Protocol) || IsFallbackProtocol(nextSession.Protocol))
               && (!string.Equals(previousSession.DeviceCode, nextSession.DeviceCode, StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(previousSession.PlaybackSessionId, nextSession.PlaybackSessionId, StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(previousSession.Protocol, nextSession.Protocol, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ResetHostPageAsync()
    {
        if (Browser.CoreWebView2 is null || isDisposed)
        {
            return;
        }

        WriteHostLifecycleDiagnostic(
            "preview-host-reset-start",
            "ResetHostPageAsync",
            "host_page_reset",
            currentSession: activeSession);
        browserReady = false;
        Browser.Visibility = Visibility.Collapsed;

        try
        {
            await Browser.ExecuteScriptAsync("window.TyslPreviewHost?.stop('host_page_reset');");
        }
        catch
        {
            // Best effort stop before navigation reset.
        }

        CancelNegotiation();
        ClearMediaRequestTraces();
        NavigateHostPage();
        WriteHostLifecycleDiagnostic(
            "preview-host-reset-end",
            "ResetHostPageAsync",
            "host_page_navigated",
            currentSession: activeSession);
    }

    private void NavigateHostPage()
    {
        var pageMode = string.IsNullOrWhiteSpace(pendingHostPageMode)
            ? HostPageModeDefault
            : pendingHostPageMode!;
        var query = new List<string>
        {
            $"v={Guid.NewGuid():N}",
            $"pageMode={Uri.EscapeDataString(pageMode)}"
        };

        if (IsFallbackProtocol(pendingHostPageProtocol))
        {
            query.Add($"fallbackProtocol={Uri.EscapeDataString(pendingHostPageProtocol!)}");
        }

        currentHostPageMode = pageMode;
        pendingHostPageMode = null;
        pendingHostPageProtocol = null;
        Browser.Source = new Uri($"{HostUrl}?{string.Join("&", query)}", UriKind.Absolute);
    }

    private static bool IsFallbackProtocol(string? protocol)
    {
        return string.Equals(protocol, "flv", StringComparison.OrdinalIgnoreCase)
               || string.Equals(protocol, "hls", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowFailureState();
        }
    }

    private void HandleWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (TryHandleHlsProxyRequest(e))
        {
            return;
        }

        WriteMediaResourceDiagnostic(
            "preview-host-network-request",
            e.Request.Uri,
            e.Request.Method,
            e.ResourceContext.ToString(),
            requestHeaders: SummarizeRequestHeaders(e.Request.Headers));
    }

    private void HandleWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        WriteMediaResourceDiagnostic(
            "preview-host-network-response",
            e.Request.Uri,
            e.Request.Method,
            "response",
            e.Response.StatusCode,
            e.Response.ReasonPhrase,
            responseHeaders: SummarizeResponseHeaders(e.Response.Headers));
    }

    private async Task EnsureDevToolsNetworkDiagnosticsAsync(CoreWebView2 webView)
    {
        if (networkRequestWillBeSentReceiver is not null
            || networkRequestWillBeSentExtraInfoReceiver is not null
            || networkResponseReceivedReceiver is not null
            || networkResponseReceivedExtraInfoReceiver is not null
            || networkLoadingFailedReceiver is not null
            || securityCertificateErrorReceiver is not null)
        {
            return;
        }

        networkRequestWillBeSentReceiver = webView.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        networkRequestWillBeSentExtraInfoReceiver = webView.GetDevToolsProtocolEventReceiver("Network.requestWillBeSentExtraInfo");
        networkResponseReceivedReceiver = webView.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        networkResponseReceivedExtraInfoReceiver = webView.GetDevToolsProtocolEventReceiver("Network.responseReceivedExtraInfo");
        networkLoadingFailedReceiver = webView.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        securityCertificateErrorReceiver = webView.GetDevToolsProtocolEventReceiver("Security.certificateError");
        networkRequestWillBeSentReceiver.DevToolsProtocolEventReceived += HandleNetworkRequestWillBeSent;
        networkRequestWillBeSentExtraInfoReceiver.DevToolsProtocolEventReceived += HandleNetworkRequestWillBeSentExtraInfo;
        networkResponseReceivedReceiver.DevToolsProtocolEventReceived += HandleNetworkResponseReceived;
        networkResponseReceivedExtraInfoReceiver.DevToolsProtocolEventReceived += HandleNetworkResponseReceivedExtraInfo;
        networkLoadingFailedReceiver.DevToolsProtocolEventReceived += HandleNetworkLoadingFailed;
        securityCertificateErrorReceiver.DevToolsProtocolEventReceived += HandleSecurityCertificateError;
        await webView.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        await webView.CallDevToolsProtocolMethodAsync("Security.enable", "{}");
    }

    private void HandleNetworkRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-request", e.ParameterObjectAsJson);
    }

    private void HandleNetworkRequestWillBeSentExtraInfo(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-request-extra", e.ParameterObjectAsJson);
    }

    private void HandleNetworkResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-response", e.ParameterObjectAsJson);
    }

    private void HandleNetworkResponseReceivedExtraInfo(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-response-extra", e.ParameterObjectAsJson);
    }

    private void HandleNetworkLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-failed", e.ParameterObjectAsJson);
    }

    private void HandleSecurityCertificateError(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-certificate-error", e.ParameterObjectAsJson);
    }

    private async void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            root.TryGetProperty("payload", out var payload);

            switch (type)
            {
                case "host_ready":
                    var pageMode = GetValue(payload, "pageMode");
                    if (!string.IsNullOrWhiteSpace(pageMode))
                    {
                        currentHostPageMode = pageMode;
                    }

                    browserReady = true;
                    Browser.Visibility = Visibility.Visible;
                    ShowOverlay(pendingSessionJson is null && activeSession is null ? "点击点位后自动预览。" : null);
                    await ApplyPendingSessionAsync();
                    break;
                case "host_initialized":
                    if (IsActiveSession(payload))
                    {
                        var deviceCode = GetValue(payload, "deviceCode") ?? activeSession?.DeviceCode ?? string.Empty;
                        var playbackSessionId = GetValue(payload, "playbackSessionId") ?? activeSession?.PlaybackSessionId ?? string.Empty;
                        var protocol = GetProtocol(payload);
                        WriteDiagnostic(
                            "preview-host-initialized",
                            $"deviceCode={deviceCode}, sessionId={playbackSessionId}, protocol={protocol}");

                        HostInitialized?.Invoke(this, new PreviewHostInitializedEventArgs(deviceCode, playbackSessionId, protocol));
                    }
                    break;
                case "webrtc_offer":
                    await HandleWebRtcOfferAsync(payload);
                    break;
                case "playback_ready":
                    if (IsActiveSession(payload))
                    {
                        var deviceCode = GetValue(payload, "deviceCode") ?? activeSession?.DeviceCode ?? string.Empty;
                        var playbackSessionId = GetValue(payload, "playbackSessionId") ?? activeSession?.PlaybackSessionId ?? string.Empty;
                        var protocol = GetProtocol(payload);
                        WriteDiagnostic(
                            "preview-host-ready",
                            $"deviceCode={deviceCode}, sessionId={playbackSessionId}, protocol={protocol}");

                        PlaybackReady?.Invoke(this, new PreviewPlaybackReadyEventArgs(deviceCode, playbackSessionId, protocol));
                    }
                    break;
                case "playback_failed":
                    if (IsActiveSession(payload))
                    {
                        var deviceCode = GetValue(payload, "deviceCode") ?? activeSession?.DeviceCode ?? string.Empty;
                        var playbackSessionId = GetValue(payload, "playbackSessionId") ?? activeSession?.PlaybackSessionId ?? string.Empty;
                        var protocol = GetProtocol(payload);
                        var category = GetValue(payload, "category");
                        var reason = GetValue(payload, "reason");
                        WriteDiagnostic(
                            "preview-host-failed",
                            $"deviceCode={deviceCode}, sessionId={playbackSessionId}, protocol={protocol}, category={category ?? "unknown"}, reason={reason ?? "none"}");

                        PlaybackFailed?.Invoke(this, new PreviewPlaybackFailedEventArgs(deviceCode, playbackSessionId, protocol, category, reason));
                    }
                    break;
                case "playback_stopped":
                    var stoppedSessionId = GetValue(payload, "playbackSessionId");
                    var stoppedDeviceCode = GetValue(payload, "deviceCode") ?? activeSession?.DeviceCode ?? "unknown";
                    var stoppedProtocol = GetProtocol(payload);
                    var stoppedSession = new PreviewPlaybackSessionDto
                    {
                        DeviceCode = stoppedDeviceCode,
                        PlaybackSessionId = stoppedSessionId ?? string.Empty,
                        Protocol = stoppedProtocol
                    };
                    WriteDiagnostic(
                        "preview-resources-released",
                        $"{BuildHostLifecycleDiagnosticState("HandleWebMessageReceived.playback_stopped", GetValue(payload, "reason") ?? "none", currentSession: stoppedSession)}, peerClosed={GetBoolValue(payload, "peerClosed")}, mediaTracksStopped={GetIntValue(payload, "mediaTracksStopped")}, flvPlayersDisposed={GetIntValue(payload, "flvPlayersDisposed")}, hlsPlayersDisposed={GetIntValue(payload, "hlsPlayersDisposed")}");
                    if (stopPlaybackCompletionSource is not null
                        && (string.IsNullOrWhiteSpace(stopPlaybackSessionId)
                            || string.Equals(stopPlaybackSessionId, stoppedSessionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        stopPlaybackSessionId = null;
                        stopPlaybackCompletionSource.TrySetResult(true);
                        stopPlaybackCompletionSource = null;
                    }
                    break;
                case "playback_diagnostic":
                    if (IsActiveSession(payload))
                    {
                        WriteDiagnostic(
                            "preview-host-event",
                            BuildPlaybackDiagnosticMessage(payload));
                    }
                    break;
                case "playback_idle":
                    if (activeSession is null && string.IsNullOrWhiteSpace(pendingSessionJson))
                    {
                        ShowOverlay("点击点位后自动预览。");
                    }
                    break;
            }
        }
        catch
        {
            ShowFailureState();
        }
    }

    private async Task HandleWebRtcOfferAsync(JsonElement payload)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var requestId = payload.TryGetProperty("requestId", out var requestIdElement)
            ? requestIdElement.GetString()
            : null;
        var apiUrl = payload.TryGetProperty("apiUrl", out var apiUrlElement)
            ? apiUrlElement.GetString()
            : null;
        var streamUrl = payload.TryGetProperty("streamUrl", out var streamUrlElement)
            ? streamUrlElement.GetString()
            : null;
        var offerSdp = payload.TryGetProperty("offerSdp", out var offerSdpElement)
            ? offerSdpElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(requestId)
            || string.IsNullOrWhiteSpace(apiUrl)
            || string.IsNullOrWhiteSpace(streamUrl)
            || string.IsNullOrWhiteSpace(offerSdp)
            || activeSession is null
            || PreviewService is null)
        {
            PostHostMessage(new
            {
                type = "webrtc_answer_failed",
                payload = new
                {
                    requestId,
                    reason = "webrtc negotiation unavailable"
                }
            });
            return;
        }

        CancelNegotiation();
        ClearMediaRequestTraces();
        var cancellationTokenSource = new CancellationTokenSource();
        negotiationCts = cancellationTokenSource;

        try
        {
            var result = await PreviewService.NegotiateWebRtcAsync(
                activeSession.DeviceCode,
                apiUrl,
                streamUrl,
                offerSdp,
                cancellationTokenSource.Token);

            if (!ReferenceEquals(negotiationCts, cancellationTokenSource) || Browser.CoreWebView2 is null)
            {
                return;
            }

            PostHostMessage(result.IsSuccess && !string.IsNullOrWhiteSpace(result.AnswerSdp)
                ? new
                {
                    type = "webrtc_answer",
                    payload = new
                    {
                        requestId,
                        answerSdp = result.AnswerSdp
                    }
                }
                : new
                {
                    type = "webrtc_answer_failed",
                    payload = new
                    {
                        requestId,
                        reason = result.FailureReason ?? "webrtc negotiation failed"
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled negotiation.
        }
        catch
        {
            if (Browser.CoreWebView2 is null)
            {
                return;
            }

            PostHostMessage(new
            {
                type = "webrtc_answer_failed",
                payload = new
                {
                    requestId,
                    reason = "webrtc negotiation failed"
                }
            });
        }
    }

    private bool IsActiveSession(JsonElement payload)
    {
        var deviceCode = payload.TryGetProperty("deviceCode", out var deviceCodeElement)
            ? deviceCodeElement.GetString()
            : null;
        var playbackSessionId = payload.TryGetProperty("playbackSessionId", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        return activeSession is not null
               && !string.IsNullOrWhiteSpace(deviceCode)
               && !string.IsNullOrWhiteSpace(playbackSessionId)
               && string.Equals(activeSession.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(activeSession.PlaybackSessionId, playbackSessionId, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProtocol(JsonElement payload)
    {
        return GetValue(payload, "protocol") ?? string.Empty;
    }

    private static string? GetValue(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var element)
            ? GetElementString(element)
            : null;
    }

    private static string GetElementString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static int GetIntValue(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return 0;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numberValue))
        {
            return numberValue;
        }

        return element.ValueKind == JsonValueKind.String
               && int.TryParse(element.GetString(), out var stringValue)
            ? stringValue
            : 0;
    }

    private static bool GetBoolValue(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numberValue)
            ? numberValue != 0
            : element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var boolValue) && boolValue;
    }

    private void PostHostMessage(object message)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private void ShowFailureState(string category = "host_runtime_failed", string? reason = null)
    {
        if (hasShownRuntimeFailure)
        {
            return;
        }

        hasShownRuntimeFailure = true;
        browserReady = false;
        ClearMediaRequestTraces();
        Browser.Visibility = Visibility.Collapsed;
        if (activeSession is not null)
        {
            var protocol = activeSession.Protocol;
            WriteDiagnostic(
                "preview-host-failed",
                $"deviceCode={activeSession.DeviceCode}, sessionId={activeSession.PlaybackSessionId}, protocol={protocol}, category={category}, reason={reason ?? "host runtime unavailable"}");

            PlaybackFailed?.Invoke(this, new PreviewPlaybackFailedEventArgs(activeSession.DeviceCode, activeSession.PlaybackSessionId, protocol, category, reason));
        }
        ShowOverlay("预览暂不可用。");
    }

    private void ShowOverlay(string? text)
    {
        HostStateTextBlock.Text = text ?? string.Empty;
        HostStateOverlay.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string? ResolveAssetDirectory()
    {
        foreach (var root in GetSearchRoots())
        {
            var candidate = Path.Combine(root, "web", "preview");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void CancelNegotiation()
    {
        negotiationCts?.Cancel();
        negotiationCts?.Dispose();
        negotiationCts = null;
    }

    private void WriteDiagnostic(string eventName, string message)
    {
        _ = DiagnosticService?.WriteAsync(eventName, message);
    }

    private string BuildHostLifecycleDiagnosticState(
        string caller,
        string reason,
        PreviewPlaybackSessionDto? currentSession = null,
        PreviewPlaybackSessionDto? previousSession = null,
        PreviewPlaybackSessionDto? nextSession = null,
        bool isSessionSwitch = false)
    {
        var effectiveSession = currentSession ?? activeSession ?? nextSession ?? previousSession;
        var deviceCode = effectiveSession?.DeviceCode ?? "none";
        var sessionId = effectiveSession?.PlaybackSessionId ?? "none";
        var protocol = effectiveSession?.Protocol ?? "unknown";
        var isFallbackSession = IsFallbackProtocol(protocol);
        var viewModelState = (DataContext as ShellViewModel)?.BuildPreviewDiagnosticState(
                                 caller,
                                 reason,
                                 deviceCode,
                                 sessionId,
                                 protocol,
                                 isFallbackSession,
                                 oldSessionId: previousSession?.PlaybackSessionId,
                                 newSessionId: nextSession?.PlaybackSessionId,
                                 isSessionSwitch: isSessionSwitch)
                             ?? string.Join(", ", new[]
                             {
                                 $"caller={caller}",
                                 $"reason={reason}",
                                 $"deviceCode={deviceCode}",
                                 $"sessionId={sessionId}",
                                 $"protocol={protocol}",
                                 "previewRequested=unknown",
                                 "previewPlaybackState=unknown",
                                 "hasPreviewSessionJson=unknown",
                                 "hasPreviewSession=unknown",
                                 "selectedPointDeviceCode=unknown",
                                 "isWindowClosing=unknown",
                                 $"isFallbackSession={isFallbackSession}",
                                 $"isSessionSwitch={isSessionSwitch}",
                                 $"oldSessionId={previousSession?.PlaybackSessionId ?? "none"}",
                                 $"newSessionId={nextSession?.PlaybackSessionId ?? "none"}",
                                 $"stackTrace={Environment.StackTrace.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}"
                             });

        return string.Join(", ", new[]
        {
            viewModelState,
            $"hostBrowserReady={browserReady}",
            $"hostHasPendingSessionJson={!string.IsNullOrWhiteSpace(pendingSessionJson)}",
            $"hostActiveSessionId={activeSession?.PlaybackSessionId ?? "none"}"
        });
    }

    private void WriteHostLifecycleDiagnostic(
        string eventName,
        string caller,
        string reason,
        PreviewPlaybackSessionDto? currentSession = null,
        PreviewPlaybackSessionDto? previousSession = null,
        PreviewPlaybackSessionDto? nextSession = null,
        bool isSessionSwitch = false)
    {
        WriteDiagnostic(
            eventName,
            BuildHostLifecycleDiagnosticState(
                caller,
                reason,
                currentSession,
                previousSession,
                nextSession,
                isSessionSwitch));
    }

    private void PrepareHostPageReset(PreviewPlaybackSessionDto? previousSession, PreviewPlaybackSessionDto nextSession)
    {
        pendingHostPageMode = IsFallbackProtocol(nextSession.Protocol)
            ? HostPageModeFallbackOnly
            : HostPageModeDefault;
        pendingHostPageProtocol = IsFallbackProtocol(nextSession.Protocol)
            ? nextSession.Protocol
            : null;

        WriteDiagnostic(
            "preview-host-page-reset",
            $"deviceCode={nextSession.DeviceCode}, sessionId={nextSession.PlaybackSessionId}, previousProtocol={previousSession?.Protocol ?? "none"}, nextProtocol={nextSession.Protocol}, pageMode={pendingHostPageMode}, reason={GetHostPageResetReason(previousSession, nextSession)}, oldSessionId={previousSession?.PlaybackSessionId ?? "none"}, newSessionId={nextSession.PlaybackSessionId}");
    }

    private static string GetHostPageResetReason(PreviewPlaybackSessionDto? previousSession, PreviewPlaybackSessionDto nextSession)
    {
        return string.Equals(previousSession?.Protocol, "webrtc", StringComparison.OrdinalIgnoreCase)
               && IsFallbackProtocol(nextSession.Protocol)
            ? "webrtc_to_fallback_hard_reset"
            : "fallback_session_reset";
    }

    private void DetachDevToolsNetworkDiagnostics()
    {
        if (networkRequestWillBeSentReceiver is not null)
        {
            networkRequestWillBeSentReceiver.DevToolsProtocolEventReceived -= HandleNetworkRequestWillBeSent;
            networkRequestWillBeSentReceiver = null;
        }

        if (networkRequestWillBeSentExtraInfoReceiver is not null)
        {
            networkRequestWillBeSentExtraInfoReceiver.DevToolsProtocolEventReceived -= HandleNetworkRequestWillBeSentExtraInfo;
            networkRequestWillBeSentExtraInfoReceiver = null;
        }

        if (networkResponseReceivedReceiver is not null)
        {
            networkResponseReceivedReceiver.DevToolsProtocolEventReceived -= HandleNetworkResponseReceived;
            networkResponseReceivedReceiver = null;
        }

        if (networkResponseReceivedExtraInfoReceiver is not null)
        {
            networkResponseReceivedExtraInfoReceiver.DevToolsProtocolEventReceived -= HandleNetworkResponseReceivedExtraInfo;
            networkResponseReceivedExtraInfoReceiver = null;
        }

        if (networkLoadingFailedReceiver is not null)
        {
            networkLoadingFailedReceiver.DevToolsProtocolEventReceived -= HandleNetworkLoadingFailed;
            networkLoadingFailedReceiver = null;
        }

        if (securityCertificateErrorReceiver is not null)
        {
            securityCertificateErrorReceiver.DevToolsProtocolEventReceived -= HandleSecurityCertificateError;
            securityCertificateErrorReceiver = null;
        }

        ClearMediaRequestTraces();
    }

    private void ClearMediaRequestTraces()
    {
        mediaRequestTraces.Clear();
    }

    private bool TryHandleHlsProxyRequest(CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (Browser.CoreWebView2 is null
            || PreviewService is null
            || !TryGetHlsProxyTargetUri(e.Request.Uri, out _))
        {
            return false;
        }

        var deferral = e.GetDeferral();
        _ = HandleHlsProxyRequestAsync(e, deferral);
        return true;
    }

    private async Task HandleHlsProxyRequestAsync(
        CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral)
    {
        try
        {
            if (Browser.CoreWebView2 is null)
            {
                return;
            }

            if (PreviewService is null || !TryGetHlsProxyTargetUri(e.Request.Uri, out var targetUri))
            {
                e.Response = CreatePlainTextResponse(Browser.CoreWebView2, 400, "Bad Request", "Invalid HLS proxy target.");
                WriteDiagnostic(
                    "preview-host-proxy-failed",
                    BuildProxyFailureDiagnostic(
                        targetUrl: null,
                        statusCode: 400,
                        contentType: "text/plain; charset=utf-8",
                        contentLength: 25,
                        redirected: false,
                        redirectCount: 0,
                        reason: "invalid_hls_proxy_target",
                        bodyKind: "error",
                        bodySummary: "Invalid HLS proxy target."));
                return;
            }

            if (string.Equals(e.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                e.Response = CreateOptionsResponse(Browser.CoreWebView2);
                WriteDiagnostic(
                    "preview-host-proxy-response",
                    BuildProxyResponseDiagnostic(
                        targetUri.AbsoluteUri,
                        targetUri.AbsoluteUri,
                        204,
                        "No Content",
                        "text/plain; charset=utf-8",
                        0,
                        false,
                        0,
                        bodyKind: "empty",
                        upstreamBodySummary: null,
                        responseBodySummary: null,
                        bodyContainsExtM3u: false,
                        manifestRewriteSucceeded: false,
                        proxyManifestUrlsRewritten: false,
                        proxySegmentUrlsRewritten: false,
                        proxyKeyUrlsRewritten: false));
                return;
            }

            var forwardedHeaders = CreatePreviewProxyHeaders(e.Request.Headers);
            var isTsProxyRequest = LooksLikeSegmentUri(targetUri.AbsolutePath);
            var proxyRequestDiagnostic = BuildProxyRequestDiagnostic(targetUri.AbsoluteUri, e.Request.Method, forwardedHeaders);
            WriteDiagnostic(
                "preview-host-proxy-request",
                proxyRequestDiagnostic);
            if (isTsProxyRequest)
            {
                WriteDiagnostic("hls_ts_proxy_request", proxyRequestDiagnostic);
            }

            var proxyResult = await PreviewService.FetchPreviewResourceAsync(
                new PreviewProxyRequest
                {
                    RequestUrl = targetUri.AbsoluteUri,
                    Method = e.Request.Method,
                    Headers = forwardedHeaders
                });

            var rawResponseBytes = proxyResult.Content ?? [];
            var contentType = proxyResult.ContentType;
            var rawBodyText = TryDecodeTextBody(rawResponseBytes, contentType);
            var rawBodySummary = SummarizeProxyBody(rawBodyText, rawResponseBytes.LongLength);
            var bodyContainsExtM3u = ContainsExtM3u(rawBodyText);
            var responseBytes = rawResponseBytes;
            var rewriteResult = HlsManifestRewriteResult.None;
            if (proxyResult.StatusCode >= 200
                && proxyResult.StatusCode < 300
                && IsHlsManifestResponse(targetUri, contentType))
            {
                rewriteResult = RewriteHlsManifest(responseBytes, targetUri);
                responseBytes = rewriteResult.Content;
                contentType ??= "application/vnd.apple.mpegurl";
            }

            var responseBodyText = TryDecodeTextBody(responseBytes, contentType);
            var responseBodySummary = SummarizeProxyBody(responseBodyText, responseBytes.LongLength);
            var bodyKind = ClassifyProxyBody(
                proxyResult.StatusCode,
                contentType,
                rawBodyText,
                responseBytes.LongLength,
                proxyResult.Redirected);
            var responseHeaders = BuildProxyResponseHeaders(
                proxyResult.Headers,
                contentType,
                responseBytes.LongLength,
                rewriteResult.Rewritten);
            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(responseBytes, writable: false),
                proxyResult.StatusCode,
                string.IsNullOrWhiteSpace(proxyResult.ReasonPhrase) ? "OK" : proxyResult.ReasonPhrase,
                responseHeaders);

            var proxyResponseDiagnostic = BuildProxyResponseDiagnostic(
                targetUri.AbsoluteUri,
                string.IsNullOrWhiteSpace(proxyResult.ResponseUrl) ? targetUri.AbsoluteUri : proxyResult.ResponseUrl,
                proxyResult.StatusCode,
                proxyResult.ReasonPhrase,
                contentType,
                responseBytes.LongLength,
                proxyResult.Redirected,
                proxyResult.RedirectCount,
                bodyKind,
                rawBodySummary,
                responseBodySummary,
                bodyContainsExtM3u,
                rewriteResult.Rewritten,
                rewriteResult.ManifestUrlsRewritten,
                rewriteResult.SegmentUrlsRewritten,
                rewriteResult.KeyUrlsRewritten);
            WriteDiagnostic(
                "preview-host-proxy-response",
                proxyResponseDiagnostic);
            if (isTsProxyRequest)
            {
                WriteDiagnostic(
                    proxyResult.StatusCode >= 200 && proxyResult.StatusCode < 300
                        ? "hls_ts_proxy_response"
                        : "hls_ts_proxy_failed",
                    proxyResponseDiagnostic);
            }
        }
        catch (Exception ex)
        {
            if (Browser.CoreWebView2 is not null)
            {
                e.Response = CreatePlainTextResponse(Browser.CoreWebView2, 502, "Bad Gateway", ex.Message);
            }

            var targetUrl = TryGetHlsProxyTargetUri(e.Request.Uri, out var targetUri) ? targetUri.AbsoluteUri : e.Request.Uri;
            var proxyFailureDiagnostic = BuildProxyFailureDiagnostic(
                targetUrl: targetUrl,
                statusCode: 502,
                contentType: "text/plain; charset=utf-8",
                contentLength: null,
                redirected: false,
                redirectCount: 0,
                reason: ex.Message,
                bodyKind: "error",
                bodySummary: ex.Message);
            WriteDiagnostic(
                "preview-host-proxy-failed",
                proxyFailureDiagnostic);
            if (targetUri is not null && LooksLikeSegmentUri(targetUri.AbsolutePath))
            {
                WriteDiagnostic("hls_ts_proxy_failed", proxyFailureDiagnostic);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static CoreWebView2WebResourceResponse CreatePlainTextResponse(
        CoreWebView2 webView,
        int statusCode,
        string reasonPhrase,
        string message)
    {
        return webView.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(message ?? string.Empty), writable: false),
            statusCode,
            reasonPhrase,
            "Content-Type: text/plain; charset=utf-8\r\nCache-Control: no-store");
    }

    private static CoreWebView2WebResourceResponse CreateOptionsResponse(CoreWebView2 webView)
    {
        return webView.Environment.CreateWebResourceResponse(
            Stream.Null,
            204,
            "No Content",
            BuildOptionsResponseHeaders());
    }

    private static IReadOnlyDictionary<string, string> CreatePreviewProxyHeaders(CoreWebView2HttpRequestHeaders headers)
    {
        var forwarded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            forwarded[header.Key] = header.Value.Trim();
        }

        return forwarded;
    }

    private static string BuildProxyResponseHeaders(
        IReadOnlyDictionary<string, string> headers,
        string? contentType,
        long contentLength,
        bool manifestRewritten)
    {
        var values = new List<string>();
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values.Add($"{header.Key}: {header.Value}");
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            values.Add($"Content-Type: {contentType}");
        }

        values.Add($"Content-Length: {contentLength}");
        values.Add("Cache-Control: no-store");
        values.Add($"Access-Control-Allow-Origin: https://{HostName}");
        values.Add("Access-Control-Allow-Methods: GET, HEAD, OPTIONS");
        values.Add("Access-Control-Allow-Headers: Accept, Accept-Language, Cache-Control, Content-Type, Origin, Pragma, Range, User-Agent");
        values.Add("Access-Control-Expose-Headers: Content-Length, Content-Range, Content-Type");
        if (manifestRewritten)
        {
            values.Add("X-Tysl-Hls-Proxy: rewritten");
        }

        return string.Join("\r\n", values);
    }

    private static string BuildOptionsResponseHeaders()
    {
        return string.Join(
            "\r\n",
            [
                "Cache-Control: no-store",
                $"Access-Control-Allow-Origin: https://{HostName}",
                "Access-Control-Allow-Methods: GET, HEAD, OPTIONS",
                "Access-Control-Allow-Headers: Accept, Accept-Language, Cache-Control, Content-Type, Origin, Pragma, Range, User-Agent",
                "Access-Control-Expose-Headers: Content-Length, Content-Range, Content-Type",
                "Content-Length: 0"
            ]);
    }

    private static bool TryGetHlsProxyTargetUri(string? requestUri, out Uri targetUri)
    {
        targetUri = null!;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var hostUri)
            || (!string.Equals(hostUri.Host, HostName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(hostUri.Host, ProxyHostName, StringComparison.OrdinalIgnoreCase))
            || !string.Equals(hostUri.AbsolutePath, HlsProxyPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = GetQueryValue(hostUri, "target");
        if (!Uri.TryCreate(target, UriKind.Absolute, out var resolvedTargetUri)
            || resolvedTargetUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        targetUri = resolvedTargetUri;
        return true;
    }

    private static string BuildHlsProxyUrl(string targetUrl)
    {
        return $"{Uri.UriSchemeHttps}://{ProxyHostName}{HlsProxyPath}?target={Uri.EscapeDataString(targetUrl)}";
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return null;
        }

        var segments = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
            if (!string.Equals(WebUtility.UrlDecode(rawKey), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;
            return WebUtility.UrlDecode(rawValue);
        }

        return null;
    }

    private static bool IsHlsManifestResponse(Uri requestUri, string? contentType)
    {
        return requestUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(contentType)
                   && contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase));
    }

    private static HlsManifestRewriteResult RewriteHlsManifest(byte[] content, Uri manifestUri)
    {
        if (content.Length == 0)
        {
            return HlsManifestRewriteResult.None;
        }

        var source = DecodeUtf8(content);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder(source.Length + 256);
        var manifestUrlsRewritten = false;
        var segmentUrlsRewritten = false;
        var keyUrlsRewritten = false;
        var rewritten = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var updatedLine = RewriteHlsManifestLine(
                lines[index],
                manifestUri,
                ref rewritten,
                ref manifestUrlsRewritten,
                ref segmentUrlsRewritten,
                ref keyUrlsRewritten);
            builder.Append(updatedLine);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return new HlsManifestRewriteResult(
            Encoding.UTF8.GetBytes(builder.ToString()),
            rewritten,
            manifestUrlsRewritten,
            segmentUrlsRewritten,
            keyUrlsRewritten);
    }

    private static string RewriteHlsManifestLine(
        string line,
        Uri manifestUri,
        ref bool rewritten,
        ref bool manifestUrlsRewritten,
        ref bool segmentUrlsRewritten,
        ref bool keyUrlsRewritten)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        if (line.StartsWith("#", StringComparison.Ordinal))
        {
            var attributeRewritten = false;
            var attributeManifestUrlsRewritten = false;
            var attributeSegmentUrlsRewritten = false;
            var attributeKeyUrlsRewritten = false;
            var replacedLine = HlsManifestUriAttributeRegex.Replace(line, match =>
            {
                var candidate = match.Groups["uri"].Value;
                if (!TryResolveProxyableUri(manifestUri, candidate, out var resolvedUri))
                {
                    return match.Value;
                }

                attributeRewritten = true;
                if (LooksLikeManifestUri(resolvedUri.AbsolutePath))
                {
                    attributeManifestUrlsRewritten = true;
                }
                else if (LooksLikeKeyUri(resolvedUri.AbsolutePath))
                {
                    attributeKeyUrlsRewritten = true;
                }
                else
                {
                    attributeSegmentUrlsRewritten = true;
                }

                return $"URI=\"{BuildHlsProxyUrl(resolvedUri.AbsoluteUri)}\"";
            });
            if (attributeRewritten)
            {
                rewritten = true;
                manifestUrlsRewritten |= attributeManifestUrlsRewritten;
                segmentUrlsRewritten |= attributeSegmentUrlsRewritten;
                keyUrlsRewritten |= attributeKeyUrlsRewritten;
            }

            return replacedLine;
        }

        if (!TryResolveProxyableUri(manifestUri, line.Trim(), out var targetUri))
        {
            return line;
        }

        rewritten = true;
        if (LooksLikeManifestUri(targetUri.AbsolutePath))
        {
            manifestUrlsRewritten = true;
        }
        else if (LooksLikeKeyUri(targetUri.AbsolutePath))
        {
            keyUrlsRewritten = true;
        }
        else
        {
            segmentUrlsRewritten = true;
        }

        return BuildHlsProxyUrl(targetUri.AbsoluteUri);
    }

    private static bool TryResolveProxyableUri(Uri baseUri, string candidate, out Uri targetUri)
    {
        targetUri = null!;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, candidate, out var resolvedTargetUri))
        {
            return false;
        }

        if (resolvedTargetUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        targetUri = resolvedTargetUri;
        return true;
    }

    private string BuildProxyRequestDiagnostic(
        string targetUrl,
        string? method,
        IReadOnlyDictionary<string, string> requestHeaders)
    {
        var parts = CreateProxyDiagnosticPrefix(targetUrl);
        parts.Add($"method={method ?? "GET"}");
        AppendDiagnosticPart(parts, "requestHeaders", SummarizeHeaders(requestHeaders));
        return string.Join(", ", parts);
    }

    private string BuildProxyResponseDiagnostic(
        string targetUrl,
        string responseUrl,
        int statusCode,
        string? reasonPhrase,
        string? contentType,
        long? contentLength,
        bool redirected,
        int redirectCount,
        string bodyKind,
        string? upstreamBodySummary,
        string? responseBodySummary,
        bool bodyContainsExtM3u,
        bool manifestRewriteSucceeded,
        bool proxyManifestUrlsRewritten,
        bool proxySegmentUrlsRewritten,
        bool proxyKeyUrlsRewritten)
    {
        var parts = CreateProxyDiagnosticPrefix(targetUrl);
        parts.Add($"responseUrl={SanitizeUrlForDiagnostic(responseUrl)}");
        parts.Add($"statusCode={statusCode}");
        parts.Add($"redirected={redirected}");
        parts.Add($"redirectCount={redirectCount}");
        parts.Add($"bodyKind={bodyKind}");
        parts.Add($"bodyEmpty={contentLength.GetValueOrDefault() == 0}");
        parts.Add($"bodyContainsExtM3U={bodyContainsExtM3u}");
        parts.Add($"manifestRewriteSucceeded={manifestRewriteSucceeded}");
        parts.Add($"proxyManifestUrlsRewritten={proxyManifestUrlsRewritten}");
        parts.Add($"proxySegmentUrlsRewritten={proxySegmentUrlsRewritten}");
        parts.Add($"proxyKeyUrlsRewritten={proxyKeyUrlsRewritten}");
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
        {
            parts.Add($"reason={SanitizeDiagnosticFragment(reasonPhrase)}");
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            parts.Add($"contentType={SanitizeDiagnosticFragment(contentType)}");
        }

        if (contentLength.HasValue)
        {
            parts.Add($"contentLength={contentLength.Value}");
        }

        AppendDiagnosticPart(parts, "upstreamBodySummary", upstreamBodySummary);
        AppendDiagnosticPart(parts, "responseBodySummary", responseBodySummary);
        return string.Join(", ", parts);
    }

    private string BuildProxyFailureDiagnostic(
        string? targetUrl,
        int statusCode,
        string? contentType,
        long? contentLength,
        bool redirected,
        int redirectCount,
        string reason,
        string bodyKind,
        string? bodySummary)
    {
        var parts = CreateProxyDiagnosticPrefix(targetUrl);
        parts.Add($"statusCode={statusCode}");
        parts.Add($"redirected={redirected}");
        parts.Add($"redirectCount={redirectCount}");
        parts.Add($"bodyKind={bodyKind}");
        parts.Add($"bodyEmpty={!contentLength.HasValue || contentLength.Value == 0}");
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            parts.Add($"contentType={SanitizeDiagnosticFragment(contentType)}");
        }

        if (contentLength.HasValue)
        {
            parts.Add($"contentLength={contentLength.Value}");
        }

        parts.Add($"reason={SanitizeDiagnosticFragment(reason)}");
        AppendDiagnosticPart(parts, "bodySummary", bodySummary);
        return string.Join(", ", parts);
    }

    private List<string> CreateProxyDiagnosticPrefix(string? targetUrl)
    {
        return
        [
            $"deviceCode={SanitizeDiagnosticFragment(activeSession?.DeviceCode ?? "unknown")}",
            $"sessionId={SanitizeDiagnosticFragment(activeSession?.PlaybackSessionId ?? "unknown")}",
            $"protocol={SanitizeDiagnosticFragment(activeSession?.Protocol ?? "unknown")}",
            $"target={SanitizeUrlForDiagnostic(targetUrl)}"
        ];
    }

    private static string? TryDecodeTextBody(byte[] content, string? contentType)
    {
        if (content.Length == 0)
        {
            return string.Empty;
        }

        if (IsLikelyTextualBody(contentType))
        {
            return DecodeUtf8(content);
        }

        return LooksLikeUtf8Text(content) ? DecodeUtf8(content) : null;
    }

    private static bool IsLikelyTextualBody(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUtf8Text(byte[] content)
    {
        var length = Math.Min(content.Length, 256);
        for (var index = 0; index < length; index++)
        {
            var value = content[index];
            if (value == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeUtf8(byte[] content)
    {
        if (content.Length >= 3
            && content[0] == 0xEF
            && content[1] == 0xBB
            && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        return Encoding.UTF8.GetString(content);
    }

    private static string SummarizeProxyBody(string? body, long contentLength)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return contentLength == 0 ? "empty" : $"binary:{contentLength}";
        }

        var sanitized = SanitizeBodySummary(body);
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }

    private static string SanitizeBodySummary(string value)
    {
        return SanitizeSensitiveText(value)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " | ", StringComparison.Ordinal)
            .Trim();
    }

    private static bool ContainsExtM3u(string? body)
    {
        return !string.IsNullOrWhiteSpace(body)
               && body.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyProxyBody(
        int statusCode,
        string? contentType,
        string? body,
        long contentLength,
        bool redirected)
    {
        if (redirected || statusCode is 301 or 302 or 303 or 307 or 308)
        {
            return "redirect";
        }

        if (contentLength == 0 || string.IsNullOrWhiteSpace(body))
        {
            return "empty";
        }

        if (ContainsExtM3u(body))
        {
            return "m3u8";
        }

        if ((!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            || body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase))
        {
            return "html";
        }

        return statusCode >= 400 ? "error" : "text";
    }

    private static string SanitizeUrlForDiagnostic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return SanitizeSensitiveText(value);
        }

        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        var pairs = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment =>
            {
                var separatorIndex = segment.IndexOf('=');
                var rawKey = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
                var rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;
                var decodedKey = WebUtility.UrlDecode(rawKey);
                var decodedValue = WebUtility.UrlDecode(rawValue);
                var sanitizedValue = IsSensitiveQueryParameter(decodedKey)
                    ? "redacted"
                    : SanitizeSensitiveText(decodedValue);
                return $"{decodedKey}={sanitizedValue}";
            });

        return $"{uri.GetLeftPart(UriPartial.Path)}?{string.Join("&", pairs)}";
    }

    private static string SanitizeSensitiveText(string value)
    {
        var sanitized = value;
        foreach (var parameterName in new[] { "token", "origin", "reqid", "hls_ctx", "auth_key", "fq" })
        {
            sanitized = Regex.Replace(
                sanitized,
                $@"(?<prefix>(^|[?&\s,;]){parameterName}=)(?<value>[^&\s,;]+)",
                "${prefix}redacted",
                RegexOptions.IgnoreCase);
        }

        return sanitized;
    }

    private static bool IsSensitiveQueryParameter(string? key)
    {
        return key is not null
               && (key.Equals("token", StringComparison.OrdinalIgnoreCase)
                   || key.Equals("origin", StringComparison.OrdinalIgnoreCase)
                   || key.Equals("reqid", StringComparison.OrdinalIgnoreCase)
                   || key.Equals("hls_ctx", StringComparison.OrdinalIgnoreCase)
                   || key.Equals("auth_key", StringComparison.OrdinalIgnoreCase)
                   || key.Equals("fq", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeManifestUri(string? absolutePath)
    {
        return !string.IsNullOrWhiteSpace(absolutePath)
               && absolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeKeyUri(string? absolutePath)
    {
        return !string.IsNullOrWhiteSpace(absolutePath)
               && absolutePath.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSegmentUri(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        var extension = Path.GetExtension(absolutePath);
        return extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".m4s", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct HlsManifestRewriteResult(
        byte[] Content,
        bool Rewritten,
        bool ManifestUrlsRewritten,
        bool SegmentUrlsRewritten,
        bool KeyUrlsRewritten)
    {
        public static HlsManifestRewriteResult None => new([], false, false, false, false);
    }

    private void WriteMediaResourceDiagnostic(
        string eventName,
        string? requestUri,
        string? method,
        string? resourceContext,
        int? statusCode = null,
        string? reasonPhrase = null,
        string diagnosticSource = "webresource",
        string? requestId = null,
        string? mimeType = null,
        bool? canceled = null,
        string? requestHeaders = null,
        string? responseHeaders = null,
        string? initiator = null,
        string? frameId = null,
        string? documentUrl = null,
        string? blockedReason = null,
        string? corsError = null,
        string? mixedContentType = null,
        string? failureText = null,
        string? remoteAddress = null,
        string? securityState = null)
    {
        if (!TryBuildMediaResourceDiagnosticMessage(
                requestUri,
                method,
                resourceContext,
                statusCode,
                reasonPhrase,
                diagnosticSource,
                requestId,
                mimeType,
                canceled,
                requestHeaders,
                responseHeaders,
                initiator,
                frameId,
                documentUrl,
                blockedReason,
                corsError,
                mixedContentType,
                failureText,
                remoteAddress,
                securityState,
                out var message))
        {
            return;
        }

        WriteDiagnostic(eventName, message);
    }

    private bool TryBuildMediaResourceDiagnosticMessage(
        string? requestUri,
        string? method,
        string? resourceContext,
        int? statusCode,
        string? reasonPhrase,
        string diagnosticSource,
        string? requestId,
        string? mimeType,
        bool? canceled,
        string? requestHeaders,
        string? responseHeaders,
        string? initiator,
        string? frameId,
        string? documentUrl,
        string? blockedReason,
        string? corsError,
        string? mixedContentType,
        string? failureText,
        string? remoteAddress,
        string? securityState,
        out string message)
    {
        message = string.Empty;

        if (activeSession is null
            || !IsFallbackProtocol(activeSession.Protocol)
            || !TryCreateMediaUri(activeSession.SourceUrl, out var sourceUri)
            || !TryCreateMediaUri(requestUri, out var requestedUri)
            || !ShouldTraceMediaResource(sourceUri, requestedUri, resourceContext, mimeType))
        {
            return false;
        }

        var parts = new List<string>
        {
            $"deviceCode={activeSession.DeviceCode}",
            $"sessionId={activeSession.PlaybackSessionId}",
            $"protocol={activeSession.Protocol}",
            $"pageMode={currentHostPageMode}",
            $"source={diagnosticSource}",
            $"method={method ?? "unknown"}",
            $"resourceContext={resourceContext ?? "unknown"}",
            $"url={requestedUri.GetLeftPart(UriPartial.Path)}",
            $"sourceHost={sourceUri.Host}"
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            parts.Add($"requestId={requestId}");
        }

        if (statusCode.HasValue)
        {
            parts.Add($"statusCode={statusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            parts.Add($"mimeType={mimeType.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(reasonPhrase))
        {
            parts.Add($"reason={reasonPhrase.Trim()}");
        }

        if (canceled.HasValue)
        {
            parts.Add($"canceled={canceled.Value}");
        }

        AppendDiagnosticPart(parts, "requestHeaders", requestHeaders);
        AppendDiagnosticPart(parts, "responseHeaders", responseHeaders);
        AppendDiagnosticPart(parts, "initiator", initiator);
        AppendDiagnosticPart(parts, "frameId", frameId);
        AppendDiagnosticPart(parts, "documentUrl", documentUrl);
        AppendDiagnosticPart(parts, "blockedReason", blockedReason);
        AppendDiagnosticPart(parts, "corsError", corsError);
        AppendDiagnosticPart(parts, "mixedContentType", mixedContentType);
        AppendDiagnosticPart(parts, "failureText", failureText);
        AppendDiagnosticPart(parts, "remoteAddress", remoteAddress);
        AppendDiagnosticPart(parts, "securityState", securityState);

        message = string.Join(", ", parts);
        return true;
    }

    private void WriteDevToolsMediaDiagnostic(string eventName, string parameterObjectAsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(parameterObjectAsJson);
            var payload = document.RootElement;
            var requestId = GetValue(payload, "requestId");
            var resourceContext = GetValue(payload, "type");
            var requestUri = GetNestedValue(payload, "request", "url");
            var method = GetNestedValue(payload, "request", "method");
            var statusCode = GetNullableIntValue(payload, "response", "status")
                             ?? GetNullableIntValue(payload, "statusCode");
            var reasonPhrase = GetNestedValue(payload, "response", "statusText")
                               ?? GetValue(payload, "errorText")
                               ?? GetValue(payload, "errorType");
            var mimeType = GetNestedValue(payload, "response", "mimeType");
            var canceled = GetNullableBoolValue(payload, "canceled");
            var requestHeaders = SummarizeJsonHeaders(payload, "request", "headers")
                                 ?? SummarizeJsonHeaders(payload, "headers");
            var responseHeaders = SummarizeJsonHeaders(payload, "response", "headers")
                                  ?? SummarizeJsonHeaders(payload, "headers");
            var initiator = BuildInitiatorSummary(payload);
            var frameId = GetValue(payload, "frameId");
            var documentUrl = GetValue(payload, "documentURL");
            var blockedReason = GetValue(payload, "blockedReason");
            var corsError = GetNestedValue(payload, "corsErrorStatus", "corsError");
            var mixedContentType = GetValue(payload, "mixedContentType");
            var failureText = GetValue(payload, "errorText");
            var remoteAddress = BuildRemoteAddress(payload);
            var securityState = GetNestedValue(payload, "response", "securityState");

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                var requestTrace = GetOrCreateMediaRequestTrace(requestId, requestUri, method, resourceContext);
                requestTrace.RequestUri = requestUri ?? requestTrace.RequestUri;
                requestTrace.Method = method ?? requestTrace.Method;
                requestTrace.ResourceContext = resourceContext ?? requestTrace.ResourceContext;
                requestTrace.RequestHeaders = requestHeaders ?? requestTrace.RequestHeaders;
                requestTrace.ResponseHeaders = responseHeaders ?? requestTrace.ResponseHeaders;
                requestTrace.Initiator = initiator ?? requestTrace.Initiator;
                requestTrace.FrameId = frameId ?? requestTrace.FrameId;
                requestTrace.DocumentUrl = documentUrl ?? requestTrace.DocumentUrl;
                requestTrace.BlockedReason = blockedReason ?? requestTrace.BlockedReason;
                requestTrace.CorsError = corsError ?? requestTrace.CorsError;
                requestTrace.MixedContentType = mixedContentType ?? requestTrace.MixedContentType;
                requestTrace.FailureText = failureText ?? requestTrace.FailureText;
                requestTrace.RemoteAddress = remoteAddress ?? requestTrace.RemoteAddress;
                requestTrace.SecurityState = securityState ?? requestTrace.SecurityState;

                requestUri ??= requestTrace.RequestUri;
                method ??= requestTrace.Method;
                resourceContext ??= requestTrace.ResourceContext;
                requestHeaders ??= requestTrace.RequestHeaders;
                responseHeaders ??= requestTrace.ResponseHeaders;
                initiator ??= requestTrace.Initiator;
                frameId ??= requestTrace.FrameId;
                documentUrl ??= requestTrace.DocumentUrl;
                blockedReason ??= requestTrace.BlockedReason;
                corsError ??= requestTrace.CorsError;
                mixedContentType ??= requestTrace.MixedContentType;
                failureText ??= requestTrace.FailureText;
                remoteAddress ??= requestTrace.RemoteAddress;
                securityState ??= requestTrace.SecurityState;
            }

            if (string.IsNullOrWhiteSpace(requestUri))
            {
                requestUri = GetValue(payload, "requestURL");
            }

            if (string.IsNullOrWhiteSpace(requestUri))
            {
                return;
            }

            WriteMediaResourceDiagnostic(
                eventName,
                requestUri,
                method,
                resourceContext,
                statusCode,
                reasonPhrase,
                "cdp",
                requestId,
                mimeType,
                canceled,
                requestHeaders,
                responseHeaders,
                initiator,
                frameId,
                documentUrl,
                blockedReason,
                corsError,
                mixedContentType,
                failureText,
                remoteAddress,
                securityState);

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                if (string.Equals(eventName, "preview-host-network-failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(eventName, "preview-host-network-certificate-error", StringComparison.OrdinalIgnoreCase))
                {
                    mediaRequestTraces.Remove(requestId);
                }
            }
        }
        catch
        {
            // Ignore diagnostic parsing failures.
        }
    }

    private MediaRequestTrace GetOrCreateMediaRequestTrace(
        string requestId,
        string? requestUri,
        string? method,
        string? resourceContext)
    {
        if (mediaRequestTraces.TryGetValue(requestId, out var existing))
        {
            return existing;
        }

        var created = new MediaRequestTrace(requestUri, method, resourceContext);
        mediaRequestTraces[requestId] = created;
        return created;
    }

    private static void AppendDiagnosticPart(List<string> parts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parts.Add($"{key}={SanitizeDiagnosticFragment(value)}");
    }

    private static string SanitizeDiagnosticFragment(string value)
    {
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " | ", StringComparison.Ordinal)
            .Replace(", ", "; ", StringComparison.Ordinal)
            .Trim();
    }

    private static string? SummarizeRequestHeaders(CoreWebView2HttpRequestHeaders headers)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            values[header.Key] = header.Value.Trim();
        }

        return SummarizeHeaders(values);
    }

    private static string? SummarizeResponseHeaders(CoreWebView2HttpResponseHeaders headers)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            values[header.Key] = header.Value.Trim();
        }

        return SummarizeHeaders(values);
    }

    private static string? SummarizeJsonHeaders(JsonElement payload, params string[] propertyPath)
    {
        if (!TryGetNestedElement(payload, out var element, propertyPath)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var value = GetElementString(property.Value);
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            values[property.Name] = value.Trim();
        }

        return SummarizeHeaders(values);
    }

    private static string? SummarizeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return null;
        }

        return string.Join(
            "; ",
            headers
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}:{SanitizeDiagnosticFragment(pair.Value)}"));
    }

    private static string? BuildInitiatorSummary(JsonElement payload)
    {
        if (!TryGetNestedElement(payload, out var initiatorElement, "initiator")
            || initiatorElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = new List<string>();
        AppendElementPart(parts, "type", initiatorElement, "type");
        AppendElementPart(parts, "url", initiatorElement, "url");
        AppendElementPart(parts, "lineNumber", initiatorElement, "lineNumber");
        return parts.Count == 0 ? null : string.Join("|", parts);
    }

    private static string? BuildRemoteAddress(JsonElement payload)
    {
        var address = GetNestedValue(payload, "response", "remoteIPAddress");
        var port = GetNestedValue(payload, "response", "remotePort");
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(port) ? address : $"{address}:{port}";
    }

    private static void AppendElementPart(List<string> parts, string key, JsonElement payload, params string[] propertyPath)
    {
        var value = GetNestedValue(payload, propertyPath);
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{key}={SanitizeDiagnosticFragment(value)}");
        }
    }

    private static bool ShouldTraceMediaResource(
        Uri sourceUri,
        Uri requestedUri,
        string? resourceContext,
        string? mimeType)
    {
        if (string.Equals(requestedUri.Host, HostName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestedUri.Host, ProxyHostName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(requestedUri.Scheme, "blob", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(sourceUri.AbsoluteUri, requestedUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsMediaResourceContext(resourceContext)
               || IsMediaMimeType(mimeType)
               || IsKnownMediaPath(requestedUri.AbsolutePath)
               || string.Equals(sourceUri.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase)
                  && LooksLikeMediaRequestPath(requestedUri.AbsolutePath);
    }

    private static bool TryCreateMediaUri(string? value, out Uri uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri!);
    }

    private static bool IsMediaResourceContext(string? resourceContext)
    {
        return string.Equals(resourceContext, "Media", StringComparison.OrdinalIgnoreCase)
               || string.Equals(resourceContext, "Manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMediaMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return false;
        }

        return mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
               || mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
               || mimeType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
               || mimeType.Contains("mp2t", StringComparison.OrdinalIgnoreCase)
               || mimeType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownMediaPath(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        var extension = Path.GetExtension(absolutePath);
        return extension.Equals(".flv", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".m4s", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMediaRequestPath(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        return absolutePath.Contains("stream", StringComparison.OrdinalIgnoreCase)
               || absolutePath.Contains("playlist", StringComparison.OrdinalIgnoreCase)
               || absolutePath.Contains("segment", StringComparison.OrdinalIgnoreCase)
               || absolutePath.Contains("manifest", StringComparison.OrdinalIgnoreCase)
               || absolutePath.Contains("key", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPlaybackDiagnosticMessage(JsonElement payload)
    {
        var parts = new List<string>
        {
            $"deviceCode={GetValue(payload, "deviceCode") ?? "unknown"}",
            $"sessionId={GetValue(payload, "playbackSessionId") ?? "unknown"}",
            $"protocol={GetProtocol(payload)}",
            $"event={GetValue(payload, "event") ?? "unknown"}"
        };

        AppendValue(parts, payload, "category");
        AppendValue(parts, payload, "reason");
        AppendValue(parts, payload, "detail");
        AppendValue(parts, payload, "connectionState");
        AppendValue(parts, payload, "iceConnectionState");
        AppendValue(parts, payload, "kind");
        AppendValue(parts, payload, "trigger");
        AppendValue(parts, payload, "requestId");
        AppendValue(parts, payload, "requestUrl");
        AppendValue(parts, payload, "originalRequestUrl");
        AppendValue(parts, payload, "proxyUrl");
        AppendValue(parts, payload, "requestKind");
        AppendValue(parts, payload, "configuredSeconds");
        AppendValue(parts, payload, "readyTimeoutSeconds");
        AppendValue(parts, payload, "startPosition");
        AppendValue(parts, payload, "startLoadAttemptCount");
        AppendValue(parts, payload, "decodedFrames");
        AppendValue(parts, payload, "droppedFrames");
        AppendValue(parts, payload, "fragSn");
        AppendValue(parts, payload, "fragLevel");
        AppendValue(parts, payload, "statsLoaded");
        AppendValue(parts, payload, "statsTotal");
        AppendValue(parts, payload, "parent");
        AppendValue(parts, payload, "dataType");
        AppendValue(parts, payload, "contentLength");
        AppendValue(parts, payload, "fatal");
        AppendValue(parts, payload, "fragments");
        AppendValue(parts, payload, "attachCallbackReceived");
        AppendValue(parts, payload, "mediaAssigned");
        AppendValue(parts, payload, "oldSessionId");
        AppendValue(parts, payload, "newSessionId");
        AppendValue(parts, payload, "preferredProtocol");
        AppendValue(parts, payload, "protocolAttemptIndex");
        AppendValue(parts, payload, "totalAttemptIndex");
        AppendValue(parts, payload, "maxTotalAttempts");
        AppendSnapshot(parts, payload);
        return string.Join(", ", parts);
    }

    private static void AppendSnapshot(List<string> parts, JsonElement payload)
    {
        if (!payload.TryGetProperty("snapshot", out var snapshot)
            || snapshot.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AppendSnapshotValue(parts, snapshot, "readyState");
        AppendSnapshotValue(parts, snapshot, "networkState");
        AppendSnapshotValue(parts, snapshot, "currentTime");
        AppendSnapshotValue(parts, snapshot, "paused");
        AppendSnapshotValue(parts, snapshot, "muted");
        AppendSnapshotValue(parts, snapshot, "ended");
        AppendSnapshotValue(parts, snapshot, "seeking");
        AppendSnapshotValue(parts, snapshot, "videoWidth");
        AppendSnapshotValue(parts, snapshot, "videoHeight");
        AppendSnapshotValue(parts, snapshot, "duration");
        AppendSnapshotValue(parts, snapshot, "hasSrc");
        AppendSnapshotValue(parts, snapshot, "hasSrcObject");
        AppendSnapshotValue(parts, snapshot, "videoIsConnected");
        AppendSnapshotValue(parts, snapshot, "videoInDocument");
        AppendSnapshotValue(parts, snapshot, "isActiveVideo");
        AppendSnapshotValue(parts, snapshot, "pageMode");
        AppendSnapshotValue(parts, snapshot, "fallbackProtocolHint");
        AppendSnapshotValue(parts, snapshot, "currentSrc");
        AppendSnapshotValue(parts, snapshot, "src");
        AppendSnapshotValue(parts, snapshot, "srcObjectKind");
        AppendSnapshotValue(parts, snapshot, "crossOrigin");
        AppendSnapshotValue(parts, snapshot, "autoplay");
        AppendSnapshotValue(parts, snapshot, "hlsSupported");
        AppendSnapshotValue(parts, snapshot, "flvjsSupported");
        AppendSnapshotValue(parts, snapshot, "mediaSourceAvailable");
        AppendSnapshotValue(parts, snapshot, "errorCode");
        AppendSnapshotValue(parts, snapshot, "errorMessage");
    }

    private static void AppendSnapshotValue(List<string> parts, JsonElement snapshot, string propertyName)
    {
        if (!snapshot.TryGetProperty(propertyName, out var element))
        {
            return;
        }

        var value = GetElementString(element);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parts.Add($"{propertyName}={value}");
    }

    private static void AppendValue(List<string> parts, JsonElement payload, string propertyName)
    {
        var value = GetValue(payload, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{propertyName}={value}");
        }
    }

    private static string? GetNestedValue(JsonElement payload, params string[] propertyPath)
    {
        return TryGetNestedElement(payload, out var element, propertyPath)
            ? GetElementString(element)
            : null;
    }

    private static int? GetNullableIntValue(JsonElement payload, params string[] propertyPath)
    {
        if (!TryGetNestedElement(payload, out var element, propertyPath))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (element.TryGetDouble(out var doubleValue))
            {
                return (int)Math.Round(doubleValue);
            }
        }

        return element.ValueKind == JsonValueKind.String
               && int.TryParse(element.GetString(), out var stringValue)
            ? stringValue
            : null;
    }

    private static bool? GetNullableBoolValue(JsonElement payload, params string[] propertyPath)
    {
        if (!TryGetNestedElement(payload, out var element, propertyPath))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return element.ValueKind == JsonValueKind.String
               && bool.TryParse(element.GetString(), out var boolValue)
            ? boolValue
            : null;
    }

    private static bool TryGetNestedElement(JsonElement payload, out JsonElement element, params string[] propertyPath)
    {
        element = payload;
        foreach (var propertyName in propertyPath)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out element))
            {
                return false;
            }
        }

        return true;
    }

    public async Task FlushActiveSessionAsync(string reason = "window_closing", TimeSpan? timeout = null)
    {
        if (isDisposed)
        {
            return;
        }

        pendingSessionJson = null;
        ClearMediaRequestTraces();
        var sessionId = activeSession?.PlaybackSessionId;
        WriteHostLifecycleDiagnostic(
            "preview-host-flush-start",
            "FlushActiveSessionAsync",
            reason,
            currentSession: activeSession);
        if (Browser.CoreWebView2 is null || !browserReady || string.IsNullOrWhiteSpace(sessionId))
        {
            activeSession = null;
            WriteHostLifecycleDiagnostic(
                "preview-host-flush-skipped",
                "FlushActiveSessionAsync",
                "no_active_browser_session");
            return;
        }

        stopPlaybackCompletionSource?.TrySetResult(false);
        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        stopPlaybackCompletionSource = completionSource;
        stopPlaybackSessionId = sessionId;

        try
        {
            var reasonLiteral = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(reason) ? "window_closing" : reason.Trim());
            await Browser.ExecuteScriptAsync($"window.TyslPreviewHost?.stop({reasonLiteral});");
        }
        catch
        {
            stopPlaybackSessionId = null;
            completionSource.TrySetResult(false);
        }

        await Task.WhenAny(
            completionSource.Task,
            Task.Delay(timeout ?? TimeSpan.FromSeconds(4)));

        stopPlaybackSessionId = null;
        stopPlaybackCompletionSource = null;
        activeSession = null;
        WriteHostLifecycleDiagnostic(
            "preview-host-flush-end",
            "FlushActiveSessionAsync",
            reason);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        browserReady = false;
        pendingSessionJson = null;
        stopPlaybackSessionId = null;
        stopPlaybackCompletionSource?.TrySetResult(false);
        stopPlaybackCompletionSource = null;
        activeSession = null;
        ClearMediaRequestTraces();
        CancelNegotiation();
        Loaded -= HandleLoaded;
        Browser.Visibility = Visibility.Collapsed;

        if (Browser.CoreWebView2 is not null)
        {
            WriteHostLifecycleDiagnostic(
                "preview-host-stop-requested",
                "Dispose",
                "host_dispose");
            try
            {
                _ = Browser.ExecuteScriptAsync("window.TyslPreviewHost?.stop('host_dispose');");
            }
            catch
            {
                // Best effort shutdown only.
            }

            Browser.CoreWebView2.WebMessageReceived -= HandleWebMessageReceived;
            Browser.CoreWebView2.NavigationCompleted -= HandleNavigationCompleted;
            Browser.CoreWebView2.WebResourceRequested -= HandleWebResourceRequested;
            Browser.CoreWebView2.WebResourceResponseReceived -= HandleWebResourceResponseReceived;
            DetachDevToolsNetworkDiagnostics();

            try
            {
                Browser.CoreWebView2.ClearVirtualHostNameToFolderMapping(HostName);
            }
            catch
            {
                // Best effort shutdown only.
            }
        }

        try
        {
            Browser.Source = new Uri("about:blank");
        }
        catch
        {
            // Best effort shutdown only.
        }

        try
        {
            Browser.Dispose();
        }
        catch
        {
            // Best effort shutdown only.
        }

        GC.SuppressFinalize(this);
    }

    private sealed class MediaRequestTrace(string? requestUri, string? method, string? resourceContext)
    {
        public string? RequestUri { get; set; } = requestUri;

        public string? Method { get; set; } = method;

        public string? ResourceContext { get; set; } = resourceContext;

        public string? RequestHeaders { get; set; }

        public string? ResponseHeaders { get; set; }

        public string? Initiator { get; set; }

        public string? FrameId { get; set; }

        public string? DocumentUrl { get; set; }

        public string? BlockedReason { get; set; }

        public string? CorsError { get; set; }

        public string? MixedContentType { get; set; }

        public string? FailureText { get; set; }

        public string? RemoteAddress { get; set; }

        public string? SecurityState { get; set; }
    }
}

public sealed class PreviewHostInitializedEventArgs(string deviceCode, string playbackSessionId, string protocol) : EventArgs
{
    public string DeviceCode { get; } = deviceCode;

    public string PlaybackSessionId { get; } = playbackSessionId;

    public string Protocol { get; } = protocol;
}

public sealed class PreviewPlaybackReadyEventArgs(string deviceCode, string playbackSessionId, string protocol) : EventArgs
{
    public string DeviceCode { get; } = deviceCode;

    public string PlaybackSessionId { get; } = playbackSessionId;

    public string Protocol { get; } = protocol;
}

public sealed class PreviewPlaybackFailedEventArgs(
    string deviceCode,
    string playbackSessionId,
    string protocol,
    string? category,
    string? reason) : EventArgs
{
    public string DeviceCode { get; } = deviceCode;

    public string PlaybackSessionId { get; } = playbackSessionId;

    public string Protocol { get; } = protocol;

    public string? Category { get; } = category;

    public string? Reason { get; } = reason;
}
