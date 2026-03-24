using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.UI.Models;

namespace Tysl.Ai.UI.Views.Controls;

public partial class PreviewHostControl : UserControl, IDisposable
{
    private const string HostName = "preview.tysl.local";
    private const string HostUrl = $"https://{HostName}/index.html";

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
    private bool browserReady;
    private bool hasShownRuntimeFailure;
    private bool isApplyingSession;
    private bool isDisposed;
    private bool isInitializing;
    private CancellationTokenSource? negotiationCts;
    private string? pendingSessionJson;

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
            Browser.Source = new Uri(HostUrl, UriKind.Absolute);
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

                CancelNegotiation();

                if (string.IsNullOrWhiteSpace(json))
                {
                    activeSession = null;
                    await Browser.ExecuteScriptAsync("window.TyslPreviewHost?.stop('close_preview');");
                    ShowOverlay("点击开启预览。");
                }
                else
                {
                    activeSession = JsonSerializer.Deserialize<PreviewPlaybackSessionDto>(json, JsonOptions);
                    if (activeSession is null)
                    {
                        ShowFailureState();
                        return;
                    }

                    hasShownRuntimeFailure = false;
                    ShowOverlay(null);
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

    private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowFailureState();
        }
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
                    browserReady = true;
                    Browser.Visibility = Visibility.Visible;
                    ShowOverlay(pendingSessionJson is null && activeSession is null ? "点击开启预览。" : null);
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
                    WriteDiagnostic(
                        "preview-resources-released",
                        $"Preview resources released: deviceCode={GetValue(payload, "deviceCode") ?? "unknown"}, sessionId={GetValue(payload, "playbackSessionId") ?? "unknown"}, protocol={GetProtocol(payload)}, reason={GetValue(payload, "reason") ?? "none"}, peerClosed={GetBoolValue(payload, "peerClosed")}, mediaTracksStopped={GetIntValue(payload, "mediaTracksStopped")}, flvPlayersDisposed={GetIntValue(payload, "flvPlayersDisposed")}, hlsPlayersDisposed={GetIntValue(payload, "hlsPlayersDisposed")}");
                    break;
                case "playback_idle":
                    if (activeSession is null && string.IsNullOrWhiteSpace(pendingSessionJson))
                    {
                        ShowOverlay("点击开启预览。");
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
            ? element.GetString()
            : null;
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

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        browserReady = false;
        pendingSessionJson = null;
        activeSession = null;
        CancelNegotiation();
        Loaded -= HandleLoaded;
        Browser.Visibility = Visibility.Collapsed;

        if (Browser.CoreWebView2 is not null)
        {
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
