using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tysl.Ai.UI.Models;

namespace Tysl.Ai.UI.Views.Controls;

public partial class AmapHostControl : UserControl, IDisposable
{
    private const string HostName = "amap.tysl.local";
    private const string HostUrl = $"https://{HostName}/index.html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly DependencyProperty StateJsonProperty =
        DependencyProperty.Register(
            nameof(StateJson),
            typeof(string),
            typeof(AmapHostControl),
            new PropertyMetadata(null, HandleStateJsonChanged));

    public static readonly DependencyProperty SelectedMapStyleKeyProperty =
        DependencyProperty.Register(
            nameof(SelectedMapStyleKey),
            typeof(string),
            typeof(AmapHostControl),
            new PropertyMetadata(null, HandleSelectedMapStyleKeyChanged));

    private bool browserReady;
    private bool hasShownRuntimeFailure;
    private bool isApplyingMapStyle;
    private bool isApplyingState;
    private bool isDisposed;
    private bool isInitializing;
    private string? pendingMapStyleKey;
    private string? pendingStateJson;
    private AmapHostConfiguration configuration = new()
    {
        IsConfigured = false,
        Zoom = 11,
        Center = [120.585316, 30.028105]
    };

    public AmapHostControl()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
        Unloaded += HandleUnloaded;
    }

    public event EventHandler<MapPointSelectedEventArgs>? PointSelected;

    public event EventHandler<MapClickedEventArgs>? MapClicked;

    public event EventHandler<MapRenderedPointsEventArgs>? RenderedPointsUpdated;

    public AmapHostConfiguration Configuration
    {
        get => configuration;
        set
        {
            configuration = value ?? new AmapHostConfiguration
            {
                IsConfigured = false,
                Zoom = 11,
                Center = [120.585316, 30.028105]
            };

            pendingMapStyleKey = SelectedMapStyleKey ?? configuration.MapStyle;
            _ = EnsureInitializedAsync();
        }
    }

    public string? StateJson
    {
        get => (string?)GetValue(StateJsonProperty);
        set => SetValue(StateJsonProperty, value);
    }

    public string? SelectedMapStyleKey
    {
        get => (string?)GetValue(SelectedMapStyleKeyProperty);
        set => SetValue(SelectedMapStyleKeyProperty, value);
    }

    private static void HandleStateJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AmapHostControl control)
        {
            return;
        }

        control.pendingStateJson = e.NewValue as string;
        _ = control.ApplyPendingStateAsync();
    }

    private static void HandleSelectedMapStyleKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AmapHostControl control)
        {
            return;
        }

        control.pendingMapStyleKey = e.NewValue as string;
        _ = control.ApplyPendingMapStyleAsync();
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureInitializedAsync();
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private async Task EnsureInitializedAsync()
    {
        if (isDisposed || !IsLoaded || isInitializing)
        {
            return;
        }

        if (!configuration.IsConfigured)
        {
            Browser.Visibility = Visibility.Collapsed;
            ShowOverlay("地图未配置");
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
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = true;
            Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostName,
                assetDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildBootstrapScript(configuration));
            pendingMapStyleKey = SelectedMapStyleKey ?? configuration.MapStyle;

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

    private async Task ApplyPendingStateAsync()
    {
        if (isDisposed || !browserReady || Browser.CoreWebView2 is null || isApplyingState)
        {
            return;
        }

        isApplyingState = true;
        try
        {
            while (!string.IsNullOrWhiteSpace(pendingStateJson))
            {
                var json = pendingStateJson;
                pendingStateJson = null;

                var jsonLiteral = JsonSerializer.Serialize(json);
                await Browser.ExecuteScriptAsync($"window.TyslAmapHost?.applyStateFromJson({jsonLiteral});");
            }
        }
        catch
        {
            ShowFailureState();
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private async Task ApplyPendingMapStyleAsync()
    {
        if (isDisposed || !browserReady || Browser.CoreWebView2 is null || isApplyingMapStyle)
        {
            return;
        }

        isApplyingMapStyle = true;
        try
        {
            while (!string.IsNullOrWhiteSpace(pendingMapStyleKey))
            {
                var mapStyleKey = pendingMapStyleKey;
                pendingMapStyleKey = null;

                var styleLiteral = JsonSerializer.Serialize(mapStyleKey);
                await Browser.ExecuteScriptAsync($"window.TyslAmapHost?.applyMapStyle({styleLiteral});");
            }
        }
        catch
        {
            ShowFailureState();
        }
        finally
        {
            isApplyingMapStyle = false;
        }
    }

    private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowFailureState();
        }
    }

    private void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                case "host-ready":
                    browserReady = true;
                    Browser.Visibility = Visibility.Visible;
                    ShowOverlay(null);
                    _ = ApplyPendingMapStyleAsync();
                    _ = ApplyPendingStateAsync();
                    break;
                case "marker-click":
                    var deviceCode = payload.TryGetProperty("deviceCode", out var deviceCodeElement)
                        ? deviceCodeElement.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(deviceCode))
                    {
                        PointSelected?.Invoke(this, new MapPointSelectedEventArgs(deviceCode!));
                    }

                    break;
                case "map-click":
                    if (TryReadCoordinate(payload, out var longitude, out var latitude))
                    {
                        MapClicked?.Invoke(this, new MapClickedEventArgs(longitude, latitude));
                    }

                    break;
                case "rendered-points":
                    var points = JsonSerializer.Deserialize<IReadOnlyList<MapHostRenderedPointDto>>(payload.GetRawText(), JsonOptions);
                    if (points is not null)
                    {
                        RenderedPointsUpdated?.Invoke(this, new MapRenderedPointsEventArgs(points));
                    }

                    break;
                case "map-init-failed":
                    ShowFailureState();
                    break;
            }
        }
        catch
        {
            ShowFailureState();
        }
    }

    private void ShowFailureState()
    {
        if (hasShownRuntimeFailure)
        {
            return;
        }

        hasShownRuntimeFailure = true;
        browserReady = false;
        Browser.Visibility = Visibility.Collapsed;
        ShowOverlay("地图暂不可用");
    }

    private void ShowOverlay(string? text)
    {
        HostStateTextBlock.Text = text ?? string.Empty;
        HostStateOverlay.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool TryReadCoordinate(JsonElement payload, out double longitude, out double latitude)
    {
        longitude = 0D;
        latitude = 0D;

        if (!payload.TryGetProperty("longitude", out var longitudeElement)
            || !payload.TryGetProperty("latitude", out var latitudeElement))
        {
            return false;
        }

        return longitudeElement.TryGetDouble(out longitude)
               && latitudeElement.TryGetDouble(out latitude);
    }

    private static string BuildBootstrapScript(AmapHostConfiguration configuration)
    {
        var serializedConfig = JsonSerializer.Serialize(new
        {
            isConfigured = configuration.IsConfigured,
            key = configuration.Key,
            securityJsCode = configuration.SecurityJsCode,
            mapStyle = configuration.MapStyle,
            zoom = configuration.Zoom,
            center = configuration.Center
        });

        return $$"""
            window.__TYSL_AMAP_CONFIG__ = {{serializedConfig}};
            window._AMapSecurityConfig = {
                securityJsCode: window.__TYSL_AMAP_CONFIG__?.securityJsCode || ""
            };
            """;
    }

    private static string? ResolveAssetDirectory()
    {
        foreach (var root in GetSearchRoots())
        {
            var candidate = Path.Combine(root, "web", "amap");
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

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        browserReady = false;
        Loaded -= HandleLoaded;
        Unloaded -= HandleUnloaded;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.WebMessageReceived -= HandleWebMessageReceived;
            Browser.CoreWebView2.NavigationCompleted -= HandleNavigationCompleted;

            try
            {
                Browser.CoreWebView2.Stop();
                Browser.CoreWebView2.ClearVirtualHostNameToFolderMapping(HostName);
            }
            catch
            {
                // Best effort shutdown only.
            }
        }

        try
        {
            Browser.Source = null;
        }
        catch
        {
            // Best effort shutdown only.
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class MapPointSelectedEventArgs(string deviceCode) : EventArgs
{
    public string DeviceCode { get; } = deviceCode;
}

public sealed class MapClickedEventArgs(double longitude, double latitude) : EventArgs
{
    public double Longitude { get; } = longitude;

    public double Latitude { get; } = latitude;
}

public sealed class MapRenderedPointsEventArgs(IReadOnlyList<MapHostRenderedPointDto> points) : EventArgs
{
    public IReadOnlyList<MapHostRenderedPointDto> Points { get; } = points;
}
