using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.UI.Models;
using Tysl.Ai.UI.ViewModels;

namespace Tysl.Ai.UI.Views.Controls;

public partial class PreviewHostControl : UserControl, IDisposable
{
    private const string HostName = "preview.tysl.local";
    private const string HostUrl = $"https://{HostName}/index.html";
    private const string HostPageModeDefault = "default";
    private const string HostPageModeFallbackOnly = "fallback-only";

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
    private string? pendingHostPageMode;
    private string? pendingHostPageProtocol;
    private string? pendingSessionJson;
    private CoreWebView2DevToolsProtocolEventReceiver? networkRequestWillBeSentReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? networkResponseReceivedReceiver;
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
        WriteMediaResourceDiagnostic(
            "preview-host-network-request",
            e.Request.Uri,
            e.Request.Method,
            e.ResourceContext.ToString());
    }

    private void HandleWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        WriteMediaResourceDiagnostic(
            "preview-host-network-response",
            e.Request.Uri,
            e.Request.Method,
            "response",
            e.Response.StatusCode,
            e.Response.ReasonPhrase);
    }

    private async Task EnsureDevToolsNetworkDiagnosticsAsync(CoreWebView2 webView)
    {
        if (networkRequestWillBeSentReceiver is not null
            || networkResponseReceivedReceiver is not null
            || networkLoadingFailedReceiver is not null)
        {
            return;
        }

        networkRequestWillBeSentReceiver = webView.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        networkResponseReceivedReceiver = webView.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        networkLoadingFailedReceiver = webView.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        networkRequestWillBeSentReceiver.DevToolsProtocolEventReceived += HandleNetworkRequestWillBeSent;
        networkResponseReceivedReceiver.DevToolsProtocolEventReceived += HandleNetworkResponseReceived;
        networkLoadingFailedReceiver.DevToolsProtocolEventReceived += HandleNetworkLoadingFailed;
        await webView.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
    }

    private void HandleNetworkRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-request", e.ParameterObjectAsJson);
    }

    private void HandleNetworkResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-response", e.ParameterObjectAsJson);
    }

    private void HandleNetworkLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        WriteDevToolsMediaDiagnostic("preview-host-network-failed", e.ParameterObjectAsJson);
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

        if (networkResponseReceivedReceiver is not null)
        {
            networkResponseReceivedReceiver.DevToolsProtocolEventReceived -= HandleNetworkResponseReceived;
            networkResponseReceivedReceiver = null;
        }

        if (networkLoadingFailedReceiver is not null)
        {
            networkLoadingFailedReceiver.DevToolsProtocolEventReceived -= HandleNetworkLoadingFailed;
            networkLoadingFailedReceiver = null;
        }

        ClearMediaRequestTraces();
    }

    private void ClearMediaRequestTraces()
    {
        mediaRequestTraces.Clear();
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
        bool? canceled = null)
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
            var statusCode = GetNullableIntValue(payload, "response", "status");
            var reasonPhrase = GetNestedValue(payload, "response", "statusText") ?? GetValue(payload, "errorText");
            var mimeType = GetNestedValue(payload, "response", "mimeType");
            var canceled = GetNullableBoolValue(payload, "canceled");

            if (!string.IsNullOrWhiteSpace(requestId)
                && mediaRequestTraces.TryGetValue(requestId, out var requestTrace))
            {
                requestUri ??= requestTrace.RequestUri;
                method ??= requestTrace.Method;
                resourceContext ??= requestTrace.ResourceContext;
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
                canceled);

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                if (string.Equals(eventName, "preview-host-network-failed", StringComparison.OrdinalIgnoreCase))
                {
                    mediaRequestTraces.Remove(requestId);
                }
                else
                {
                    mediaRequestTraces[requestId] = new MediaRequestTrace(requestUri, method, resourceContext);
                }
            }
        }
        catch
        {
            // Ignore diagnostic parsing failures.
        }
    }

    private static bool ShouldTraceMediaResource(
        Uri sourceUri,
        Uri requestedUri,
        string? resourceContext,
        string? mimeType)
    {
        if (string.Equals(requestedUri.Host, HostName, StringComparison.OrdinalIgnoreCase))
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
        AppendValue(parts, payload, "requestKind");
        AppendValue(parts, payload, "configuredSeconds");
        AppendValue(parts, payload, "readyTimeoutSeconds");
        AppendValue(parts, payload, "decodedFrames");
        AppendValue(parts, payload, "droppedFrames");
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

    private sealed class MediaRequestTrace(string requestUri, string? method, string? resourceContext)
    {
        public string RequestUri { get; } = requestUri;

        public string? Method { get; } = method;

        public string? ResourceContext { get; } = resourceContext;
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
