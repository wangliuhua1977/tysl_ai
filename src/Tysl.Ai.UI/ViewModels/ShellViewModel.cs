using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;
using Tysl.Ai.UI.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private const int MaxPreviewTotalAttempts = 4;
    private const int MaxWebRtcAttempts = 2;
    private const int MaxFlvAttempts = 2;
    private const int MaxHlsAttempts = 1;
    private const string FinalPreviewFailureSummary = "全协议预览失败";

    private static readonly JsonSerializerOptions MapHostJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<MapStyleOption> BuiltInMapStyleOptions =
    [
        new("amap://styles/grey", "低噪值守风"),
        new("amap://styles/darkblue", "深蓝值守风"),
        new("amap://styles/whitesmoke", "浅灰简洁风"),
        new("default", "原生地图")
    ];

    private readonly SemaphoreSlim dashboardLoadLock = new(1, 1);
    private readonly IDispatchService dispatchService;
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly IMapStylePreferenceStore mapStylePreferenceStore;
    private readonly INotificationTemplateRenderService notificationTemplateRenderService;
    private readonly INotificationTemplateStore notificationTemplateStore;
    private readonly ISitePreviewService sitePreviewService;
    private readonly DispatcherTimer refreshTimer;
    private readonly IWebhookEndpointStore webhookEndpointStore;
    private readonly bool isMapHostConfigured;
    private DemoCoordinate? coordinatePickCandidate;
    private readonly Dictionary<string, DemoCoordinate> renderedPointCoordinates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISiteLocalProfileService siteLocalProfileService;
    private readonly ISiteMapQueryService siteMapQueryService;
    private CloseWorkOrderDialogViewModel? activeCloseWorkOrderDialog;
    private SiteEditorViewModel? activeEditor;
    private ManualDispatchDialogViewModel? activeManualDispatchDialog;
    private int currentMapVisibleCount;
    private int dispatchedCount;
    private string dispatchOverviewDetail = "当前无派单中的点位。";
    private string dispatchOverviewText = "派单待命";
    private int faultCount;
    private bool hasIgnoredPoints;
    private bool hasUnmappedPoints;
    private bool hasVisiblePoints;
    private bool isSecondaryPanelOpen;
    private string inspectionStatusDetail = "当前无纳入静默巡检的点位。";
    private string inspectionStatusText = "巡检待机";
    private bool isCoordinatePickActive;
    private bool isDisposed;
    private bool isFilterPanelExpanded = true;
    private bool isWindowClosing;
    private string mapCoverageDetailText = "地图主视图只保留纳管点位；绿色正常，红色异常。";
    private string mapEmptyStateText;
    private string mapHostStateJson = "{\"points\":[],\"candidateCoordinate\":null,\"coordinatePickActive\":false}";
    private string mapInteractionHint;
    private int mappedPointCount;
    private int monitoredCount;
    private int normalCount;
    private int pendingDispatchCount;
    private int pointCount;
    private string platformStatusDetail = "正在准备平台设备源。";
    private string platformStatusText = "平台准备中";
    private SitePreviewSession? previewSession;
    private CancellationTokenSource? previewResolveCts;
    private PreviewPlaybackState previewPlaybackState = PreviewPlaybackState.Idle;
    private bool isPreviewPlaybackReady;
    private SitePreviewProtocol previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
    private string? previewFallbackFailureReason;
    private SitePreviewProtocol previewPreferredProtocol = SitePreviewProtocol.Unknown;
    private bool acceptanceForceNextWebRtcFailure;
    private string? acceptanceForcedFailureCategory;
    private string? acceptanceForcedFailureReason;
    private string? previewAttemptDeviceCode;
    private int previewFlvAttemptCount;
    private int previewHlsAttemptCount;
    private bool previewRequested;
    private int previewTotalAttemptCount;
    private string previewProtocolText = string.Empty;
    private string? previewSessionJson;
    private string previewStatusText = "选择点位后查看画面";
    private int previewWebRtcAttemptCount;
    private string searchText = string.Empty;
    private SiteDetailViewModel? selectedDetail;
    private SiteMergedView? selectedDetailSource;
    private DashboardFilterOption selectedFilter;
    private string selectedMapStyleKey;
    private SiteMapPointViewModel? selectedPoint;
    private string? selectedPointDeviceCode;
    private SecondaryEntryMode secondaryEntryMode = SecondaryEntryMode.Unmapped;
    private int unmappedPointCount;

    public ShellViewModel(
        ISiteMapQueryService siteMapQueryService,
        ISitePreviewService sitePreviewService,
        ISiteLocalProfileService siteLocalProfileService,
        IDispatchService dispatchService,
        ILocalDiagnosticService diagnosticService,
        IWebhookEndpointStore webhookEndpointStore,
        INotificationTemplateStore notificationTemplateStore,
        INotificationTemplateRenderService notificationTemplateRenderService,
        IMapStylePreferenceStore mapStylePreferenceStore,
        bool isMapHostConfigured,
        string? initialMapStyleKey)
    {
        this.siteMapQueryService = siteMapQueryService;
        this.sitePreviewService = sitePreviewService;
        this.siteLocalProfileService = siteLocalProfileService;
        this.dispatchService = dispatchService;
        this.diagnosticService = diagnosticService;
        this.webhookEndpointStore = webhookEndpointStore;
        this.notificationTemplateStore = notificationTemplateStore;
        this.notificationTemplateRenderService = notificationTemplateRenderService;
        this.mapStylePreferenceStore = mapStylePreferenceStore;
        this.isMapHostConfigured = isMapHostConfigured;
        selectedMapStyleKey = ResolveMapStyleKey(initialMapStyleKey);

        mapEmptyStateText = isMapHostConfigured
            ? "正在等待平台点位。"
            : "地图未配置。请准备 amap-js.json 后重启。";
        mapInteractionHint = ResolveDefaultMapInteractionHint();

        Filters =
        [
            new DashboardFilterOption("全部纳管", SiteDashboardFilter.All),
            new DashboardFilterOption("异常", SiteDashboardFilter.Fault),
            new DashboardFilterOption("正常", SiteDashboardFilter.Normal)
        ];

        selectedFilter = Filters[0];
        MapStyleOptions = BuiltInMapStyleOptions;
        VisiblePoints = [];
        VisibleAlerts = [];
        UnmappedPoints = [];
        IgnoredPoints = [];
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelExpanded = !IsFilterPanelExpanded);
        EditSelectedSiteCommand = new AsyncRelayCommand(OpenEditEditorAsync, () => SelectedDetail is not null);
        OpenPreviewCommand = new AsyncRelayCommand(OpenPreviewAsync, () => SelectedDetail is not null);
        ClosePreviewCommand = new RelayCommand(ClosePreview, () => PreviewSessionJson is not null || previewRequested);
        ManualDispatchCommand = new AsyncRelayCommand(ManualDispatchAsync, () => SelectedDetail?.CanManualDispatch == true);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, () => SelectedDetail is not null);
        ToggleIgnoreCommand = new AsyncRelayCommand(ToggleIgnoreAsync, () => SelectedDetail is not null);
        ConfirmRecoveryCommand = new AsyncRelayCommand(ConfirmRecoveryAsync, () => SelectedDetail?.CanConfirmRecovery == true);
        OpenNotificationSettingsCommand = new AsyncRelayCommand(OpenNotificationSettingsAsync);
        OpenTemplateSettingsCommand = new AsyncRelayCommand(OpenTemplateSettingsAsync);
        SelectPointCommand = new RelayCommand<SiteMapPointViewModel>(SelectPoint);
        SelectAlertCommand = new RelayCommand<SiteAlertDigestViewModel>(SelectAlert);
        SelectUnmappedPointCommand = new RelayCommand<UnmappedPointDigestViewModel>(SelectUnmappedPoint);
        SelectIgnoredPointCommand = new RelayCommand<IgnoredPointDigestViewModel>(SelectIgnoredPoint);
        EditUnmappedPointCommand = new RelayCommand<UnmappedPointDigestViewModel>(EditUnmappedPoint);
        OpenUnmappedSecondaryEntryCommand = new RelayCommand(OpenUnmappedSecondaryEntry);
        OpenIgnoredSecondaryEntryCommand = new RelayCommand(OpenIgnoredSecondaryEntry);
        CloseSecondaryPanelCommand = new RelayCommand(() => IsSecondaryPanelOpen = false);

        refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        refreshTimer.Tick += HandleRefreshTimerTick;
        refreshTimer.Start();

        _ = LoadDashboardAsync();
    }

    public event EventHandler<SiteEditorDialogRequestedEventArgs>? EditorDialogRequested;

    public event EventHandler<ManualDispatchDialogRequestedEventArgs>? ManualDispatchDialogRequested;

    public event EventHandler<CloseWorkOrderDialogRequestedEventArgs>? CloseWorkOrderDialogRequested;

    public event EventHandler<NotificationSettingsDialogRequestedEventArgs>? NotificationSettingsDialogRequested;

    public event EventHandler<NotificationTemplateSettingsDialogRequestedEventArgs>? NotificationTemplateSettingsDialogRequested;

    public event EventHandler<NotificationRequestedEventArgs>? NotificationRequested;

    public int PointCount
    {
        get => pointCount;
        private set => SetProperty(ref pointCount, value);
    }

    public int MonitoredCount
    {
        get => monitoredCount;
        private set => SetProperty(ref monitoredCount, value);
    }

    public int MappedPointCount
    {
        get => mappedPointCount;
        private set => SetProperty(ref mappedPointCount, value);
    }

    public int UnmappedPointCount
    {
        get => unmappedPointCount;
        private set => SetProperty(ref unmappedPointCount, value);
    }

    public int FaultCount
    {
        get => faultCount;
        private set => SetProperty(ref faultCount, value);
    }

    public int NormalCount
    {
        get => normalCount;
        private set => SetProperty(ref normalCount, value);
    }

    public int DispatchedCount
    {
        get => dispatchedCount;
        private set => SetProperty(ref dispatchedCount, value);
    }

    public int PendingDispatchCount
    {
        get => pendingDispatchCount;
        private set => SetProperty(ref pendingDispatchCount, value);
    }

    public int CurrentMapVisibleCount
    {
        get => currentMapVisibleCount;
        private set => SetProperty(ref currentMapVisibleCount, value);
    }

    public string LastRefreshText { get; private set; } = "--:--:--";

    public IReadOnlyList<DashboardFilterOption> Filters { get; }

    public IReadOnlyList<MapStyleOption> MapStyleOptions { get; }

    public ObservableCollection<SiteMapPointViewModel> VisiblePoints { get; }

    public ObservableCollection<SiteAlertDigestViewModel> VisibleAlerts { get; }

    public ObservableCollection<UnmappedPointDigestViewModel> UnmappedPoints { get; }

    public ObservableCollection<IgnoredPointDigestViewModel> IgnoredPoints { get; }

    public RelayCommand ToggleFilterPanelCommand { get; }

    public AsyncRelayCommand EditSelectedSiteCommand { get; }

    public AsyncRelayCommand OpenPreviewCommand { get; }

    public RelayCommand ClosePreviewCommand { get; }

    public AsyncRelayCommand ManualDispatchCommand { get; }

    public AsyncRelayCommand ToggleMonitoringCommand { get; }

    public AsyncRelayCommand ToggleIgnoreCommand { get; }

    public AsyncRelayCommand ConfirmRecoveryCommand { get; }

    public AsyncRelayCommand OpenNotificationSettingsCommand { get; }

    public AsyncRelayCommand OpenTemplateSettingsCommand { get; }

    public RelayCommand<SiteMapPointViewModel> SelectPointCommand { get; }

    public RelayCommand<SiteAlertDigestViewModel> SelectAlertCommand { get; }

    public RelayCommand<UnmappedPointDigestViewModel> SelectUnmappedPointCommand { get; }

    public RelayCommand<IgnoredPointDigestViewModel> SelectIgnoredPointCommand { get; }

    public RelayCommand<UnmappedPointDigestViewModel> EditUnmappedPointCommand { get; }

    public RelayCommand OpenUnmappedSecondaryEntryCommand { get; }

    public RelayCommand OpenIgnoredSecondaryEntryCommand { get; }

    public RelayCommand CloseSecondaryPanelCommand { get; }

    public bool IsFilterPanelExpanded
    {
        get => isFilterPanelExpanded;
        set => SetProperty(ref isFilterPanelExpanded, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                _ = LoadDashboardAsync(selectedPointDeviceCode);
            }
        }
    }

    public DashboardFilterOption SelectedFilter
    {
        get => selectedFilter;
        set
        {
            if (SetProperty(ref selectedFilter, value))
            {
                OnPropertyChanged(nameof(SecondarySectionTitle));
                OnPropertyChanged(nameof(SecondarySectionDetail));
                OnPropertyChanged(nameof(SecondarySectionEmptyText));
                OnPropertyChanged(nameof(HasSecondaryItems));
                _ = LoadDashboardAsync(selectedPointDeviceCode);
            }
        }
    }

    public SiteMapPointViewModel? SelectedPoint
    {
        get => selectedPoint;
        private set
        {
            if (!SetProperty(ref selectedPoint, value))
            {
                return;
            }

            foreach (var point in VisiblePoints)
            {
                point.IsSelected = point == value;
            }

            selectedPointDeviceCode = value?.DeviceCode;
            RefreshMapHostState();
            _ = LoadSelectedSiteDetailAsync(selectedPointDeviceCode);
        }
    }

    public SiteDetailViewModel? SelectedDetail
    {
        get => selectedDetail;
        private set
        {
            if (!SetProperty(ref selectedDetail, value))
            {
                return;
            }

            EditSelectedSiteCommand.NotifyCanExecuteChanged();
            OpenPreviewCommand.NotifyCanExecuteChanged();
            ClosePreviewCommand.NotifyCanExecuteChanged();
            ManualDispatchCommand.NotifyCanExecuteChanged();
            ToggleMonitoringCommand.NotifyCanExecuteChanged();
            ToggleIgnoreCommand.NotifyCanExecuteChanged();
            ConfirmRecoveryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MonitorToggleText));
            OnPropertyChanged(nameof(IgnoreToggleText));
            RaiseMediaStatePropertiesChanged();
        }
    }

    public bool IsCoordinatePickActive
    {
        get => isCoordinatePickActive;
        private set
        {
            if (SetProperty(ref isCoordinatePickActive, value))
            {
                RefreshMapHostState();
            }
        }
    }

    public string MapInteractionHint
    {
        get => mapInteractionHint;
        private set => SetProperty(ref mapInteractionHint, value);
    }

    public string PlatformStatusText
    {
        get => platformStatusText;
        private set => SetProperty(ref platformStatusText, value);
    }

    public string PlatformStatusDetail
    {
        get => platformStatusDetail;
        private set => SetProperty(ref platformStatusDetail, value);
    }

    public string PreviewStatusText
    {
        get => previewStatusText;
        private set => SetProperty(ref previewStatusText, value);
    }

    public string PreviewProtocolText
    {
        get => previewProtocolText;
        private set => SetProperty(ref previewProtocolText, value);
    }

    public string? PreviewSessionJson
    {
        get => previewSessionJson;
        private set
        {
            if (SetProperty(ref previewSessionJson, value))
            {
                RaiseMediaStatePropertiesChanged();
            }
        }
    }

    public string InspectionStatusText
    {
        get => inspectionStatusText;
        private set => SetProperty(ref inspectionStatusText, value);
    }

    public string InspectionStatusDetail
    {
        get => inspectionStatusDetail;
        private set => SetProperty(ref inspectionStatusDetail, value);
    }

    public string DispatchOverviewText
    {
        get => dispatchOverviewText;
        private set => SetProperty(ref dispatchOverviewText, value);
    }

    public string DispatchOverviewDetail
    {
        get => dispatchOverviewDetail;
        private set => SetProperty(ref dispatchOverviewDetail, value);
    }

    public bool HasVisiblePoints
    {
        get => hasVisiblePoints;
        private set => SetProperty(ref hasVisiblePoints, value);
    }

    public bool HasIgnoredPoints
    {
        get => hasIgnoredPoints;
        private set => SetProperty(ref hasIgnoredPoints, value);
    }

    public bool HasUnmappedPoints
    {
        get => hasUnmappedPoints;
        private set => SetProperty(ref hasUnmappedPoints, value);
    }

    public string MapEmptyStateText
    {
        get => mapEmptyStateText;
        private set => SetProperty(ref mapEmptyStateText, value);
    }

    public string MapHostStateJson
    {
        get => mapHostStateJson;
        private set => SetProperty(ref mapHostStateJson, value);
    }

    public string SelectedMapStyleKey
    {
        get => selectedMapStyleKey;
        set
        {
            var resolved = ResolveMapStyleKey(value);
            if (!SetProperty(ref selectedMapStyleKey, resolved))
            {
                return;
            }

            _ = PersistSelectedMapStyleAsync(resolved);
        }
    }

    public string MonitorToggleText => SelectedDetail?.IsMonitored == false ? "纳入巡检" : "暂停巡检";

    public string IgnoreToggleText => SelectedDetail?.IsIgnored == true ? "恢复关注" : "忽略点位";

    public string ManualDispatchText => "手工派单";

    public string DispatchQueueText => $"{DispatchedCount} / {PendingDispatchCount}";

    public string MapCoverageText => $"当前地图显示 {CurrentMapVisibleCount} / 总点位 {PointCount}";

    public string MapCoverageDetailText
    {
        get => mapCoverageDetailText;
        private set => SetProperty(ref mapCoverageDetailText, value);
    }

    public string UnmappedSectionTitle => $"未落图治理 {UnmappedPointCount}";

    public string UnmappedSectionDetail => "点击条目联动详情，可直接进入编辑补充信息。";

    public bool IsSecondaryPanelOpen
    {
        get => isSecondaryPanelOpen;
        private set => SetProperty(ref isSecondaryPanelOpen, value);
    }

    public bool IsIgnoredSecondaryMode => secondaryEntryMode == SecondaryEntryMode.Ignored;

    public string SecondarySectionTitle => IsIgnoredSecondaryMode
        ? $"已忽略点位 {IgnoredPoints.Count}"
        : $"未落图治理 {UnmappedPointCount}";

    public string SecondarySectionDetail => IsIgnoredSecondaryMode
        ? "已忽略点位已退出地图、巡检和派单主线，可在右侧详情中恢复关注。"
        : "点击条目联动详情，可直接进入编辑补充信息。";

    public string SecondarySectionEmptyText => IsIgnoredSecondaryMode
        ? "当前无已忽略点位。"
        : "当前无未落图点位。";

    public bool HasSecondaryItems => IsIgnoredSecondaryMode ? HasIgnoredPoints : HasUnmappedPoints;

    public bool IsPreviewPlaybackReady
    {
        get => isPreviewPlaybackReady;
        private set
        {
            if (SetProperty(ref isPreviewPlaybackReady, value))
            {
                RaiseMediaStatePropertiesChanged();
            }
        }
    }

    public bool HasPreviewSession => !string.IsNullOrWhiteSpace(PreviewSessionJson);

    public bool HasPreviewProtocolBadge => HasPreviewSession && !string.IsNullOrWhiteSpace(PreviewProtocolText);

    public bool HasSnapshotFallback => !IsPreviewPlaybackReady && SelectedDetail?.HasSnapshot == true;

    public bool ShowMediaSurface => IsPreviewPlaybackReady || HasSnapshotFallback;

    public bool ShowMediaEmptyState => !IsPreviewPlaybackReady && !HasSnapshotFallback;

    public string MediaEmptyStateText
    {
        get
        {
            if (HasPreviewSession)
            {
                return "正在连接画面";
            }

            if (SelectedDetail is null)
            {
                return "选择点位后查看画面";
            }

            return "暂无实时视频或截图";
        }
    }

    public void HandleMapClicked(double longitude, double latitude)
    {
        if (!IsCoordinatePickActive || activeEditor is null)
        {
            return;
        }

        var candidate = new DemoCoordinate
        {
            Longitude = Math.Round(longitude, 6),
            Latitude = Math.Round(latitude, 6)
        };
        _ = WriteDiagnosticAsync(
            "map-host-candidate-updated",
            $"deviceCode={activeEditor.DeviceCode}, longitude={candidate.Longitude:F6}, latitude={candidate.Latitude:F6}");

        activeEditor.ApplyPickedCoordinate(candidate);
        coordinatePickCandidate = candidate;
        MapInteractionHint = BuildCoordinatePickHint(coordinatePickCandidate);
        RefreshMapHostState();
    }

    public void HandleMapPointSelected(string deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        _ = HandleMapPointSelectedAsync(deviceCode.Trim());
    }

    public void HandleMapPointsRendered(IReadOnlyList<MapHostRenderedPointDto> points)
    {
        renderedPointCoordinates.Clear();

        foreach (var point in points)
        {
            renderedPointCoordinates[point.DeviceCode] = new DemoCoordinate
            {
                Longitude = point.Longitude,
                Latitude = point.Latitude
            };
        }

        RefreshSelectedDetail();
    }

    public void PrepareAcceptanceForcedWebRtcFailure(
        string category = "acceptance_forced_webrtc_failure",
        string reason = "forced by preview acceptance")
    {
        acceptanceForceNextWebRtcFailure = true;
        acceptanceForcedFailureCategory = string.IsNullOrWhiteSpace(category)
            ? "acceptance_forced_webrtc_failure"
            : category.Trim();
        acceptanceForcedFailureReason = string.IsNullOrWhiteSpace(reason)
            ? "forced by preview acceptance"
            : reason.Trim();
    }

    private void HandlePreviewPlaybackReadyLegacy(string protocol)
    {
        if (previewSession is null)
        {
            return;
        }

        var selectedProtocol = ParsePreviewProtocol(protocol);
        PreviewProtocolText = GetProtocolBadgeText(selectedProtocol);
        PreviewStatusText = selectedProtocol == SitePreviewProtocol.WebRtc
            ? "实时预览中。"
            : $"已切换 {GetProtocolLabel(selectedProtocol)} 预览。";
    }

    private void HandlePreviewPlaybackFailedLegacy(string protocol)
    {
        if (previewSession is null || selectedDetail is null)
        {
            return;
        }

        var failedProtocol = ParsePreviewProtocol(protocol);
        _ = WriteDiagnosticAsync(
            "preview-playback-failed",
            $"deviceCode={selectedDetail.DeviceCode}, protocol={protocol}");

        if (failedProtocol == SitePreviewProtocol.WebRtc)
        {
            PreviewStatusText = "WebRTC 不可用，正在切换备用预览。";
            _ = StartPreviewAsync(selectedDetail.DeviceCode, SitePreviewProtocol.WebRtc);
            return;
        }

        if (failedProtocol == SitePreviewProtocol.Flv)
        {
            PreviewStatusText = "备用预览切换中。";
            _ = StartPreviewAsync(selectedDetail.DeviceCode, SitePreviewProtocol.Flv);
            return;
        }

        PreviewProtocolText = "未开启";
        PreviewStatusText = "预览暂不可用，请稍后重试。";
        PreviewSessionJson = null;
        previewSession = null;
        ClosePreviewCommand.NotifyCanExecuteChanged();
    }

    public void HandlePreviewHostInitialized(string deviceCode, string playbackSessionId, string protocol)
    {
        if (!IsCurrentPreviewSession(deviceCode, playbackSessionId))
        {
            return;
        }

        var initializedProtocol = ParsePreviewProtocol(protocol);
        if (initializedProtocol != SitePreviewProtocol.WebRtc)
        {
            return;
        }

        previewPlaybackState = PreviewPlaybackState.WebRtcHostInitializing;
        IsPreviewPlaybackReady = false;
        PreviewProtocolText = GetProtocolBadgeText(initializedProtocol);
        PreviewStatusText = "正在连接";
    }

    public void HandlePreviewPlaybackReady(string deviceCode, string playbackSessionId, string protocol)
    {
        var currentSession = previewSession;
        if (currentSession is null || !IsCurrentPreviewSession(currentSession, deviceCode, playbackSessionId))
        {
            return;
        }

        var selectedProtocol = ParsePreviewProtocol(protocol);
        previewPlaybackState = selectedProtocol switch
        {
            SitePreviewProtocol.WebRtc => PreviewPlaybackState.WebRtcPlaybackReady,
            SitePreviewProtocol.Flv => PreviewPlaybackState.FlvPlaybackReady,
            SitePreviewProtocol.Hls => PreviewPlaybackState.HlsPlaybackReady,
            _ => previewPlaybackState
        };
        IsPreviewPlaybackReady = true;

        PreviewProtocolText = GetProtocolBadgeText(selectedProtocol);
        PreviewStatusText = selectedProtocol == SitePreviewProtocol.WebRtc
            ? "实时预览"
            : $"{GetProtocolLabel(selectedProtocol)} 备用";
        var preferredProtocol = GetEffectivePreferredProtocol(currentSession);
        var usedFallback = selectedProtocol != SitePreviewProtocol.WebRtc || currentSession.UsedFallback;

        _ = WriteDiagnosticAsync(
            "preview-attempt-end",
            BuildPreviewAttemptDiagnostic(
                "handle-playback-ready",
                currentSession.DeviceCode,
                currentSession.PlaybackSessionId,
                selectedProtocol,
                currentSession.ProtocolAttemptIndex,
                currentSession.TotalAttemptIndex,
                "success",
                PreviewFailureCategory.None,
                retryReason: null,
                nextProtocol: SitePreviewProtocol.Unknown));

        _ = WriteDiagnosticAsync(
            "preview-playback-ready",
            $"deviceCode={currentSession.DeviceCode}, sessionId={currentSession.PlaybackSessionId}, preferredProtocol={ToProtocolKey(preferredProtocol)}, finalProtocol={ToProtocolKey(selectedProtocol)}, fallbackTriggered={usedFallback}, failureProtocol={ToProtocolKey(previewFallbackFailureProtocol)}, failureReason={previewFallbackFailureReason ?? "none"}");

        _ = RecordPreviewPlaybackAsync(
            currentSession.DeviceCode,
            currentSession.PlaybackSessionId,
            preferredProtocol,
            selectedProtocol,
            true,
            usedFallback,
            previewFallbackFailureProtocol,
            previewFallbackFailureReason);

        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
    }

    public void HandlePreviewPlaybackFailed(
        string deviceCode,
        string playbackSessionId,
        string protocol,
        string? category,
        string? reason)
    {
        var currentSession = previewSession;
        if (currentSession is null || !IsCurrentPreviewSession(currentSession, deviceCode, playbackSessionId))
        {
            return;
        }

        var failedProtocol = ParsePreviewProtocol(protocol);
        var failureReason = BuildPreviewFailureReason(category, reason);
        var failureCategory = ClassifyPreviewFailure(failedProtocol, category, reason);
        IsPreviewPlaybackReady = false;
        _ = WriteDiagnosticAsync(
            "preview-playback-failed",
            $"deviceCode={deviceCode}, sessionId={playbackSessionId}, protocol={protocol}, category={category ?? "unknown"}, reason={reason ?? "none"}");

        if (!previewRequested || isDisposed)
        {
            var preferredProtocol = GetEffectivePreferredProtocol(currentSession);
            _ = RecordPreviewPlaybackAsync(
                currentSession.DeviceCode,
                currentSession.PlaybackSessionId,
                preferredProtocol,
                failedProtocol,
                false,
                currentSession.UsedFallback || failedProtocol != SitePreviewProtocol.WebRtc,
                failedProtocol,
                failureReason);
            _ = WriteDiagnosticAsync(
                "preview-chain-aborted",
                BuildPreviewAttemptDiagnostic(
                    "handle-playback-failed-not-requested",
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    failedProtocol,
                    currentSession.ProtocolAttemptIndex,
                    currentSession.TotalAttemptIndex,
                    "aborted",
                    failureCategory,
                    retryReason: "preview_not_requested_or_disposed",
                    nextProtocol: SitePreviewProtocol.Unknown));
            return;
        }

        var decision = DecidePreviewRetry(currentSession, failedProtocol, failureCategory);
        _ = WriteDiagnosticAsync(
            "preview-attempt-end",
            BuildPreviewAttemptDiagnostic(
                "handle-playback-failed",
                currentSession.DeviceCode,
                currentSession.PlaybackSessionId,
                failedProtocol,
                currentSession.ProtocolAttemptIndex,
                currentSession.TotalAttemptIndex,
                "failed",
                failureCategory,
                retryReason: decision.RetryReason,
                nextProtocol: decision.NextProtocol));

        if (decision.ShouldAbortChain)
        {
            _ = WriteDiagnosticAsync(
                "preview-chain-aborted",
                BuildPreviewAttemptDiagnostic(
                    "handle-playback-failed",
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    failedProtocol,
                    currentSession.ProtocolAttemptIndex,
                    currentSession.TotalAttemptIndex,
                    "aborted",
                    failureCategory,
                    retryReason: decision.RetryReason,
                    nextProtocol: SitePreviewProtocol.Unknown));

            var finalPreferredProtocol = GetEffectivePreferredProtocol(currentSession);
            if (failureCategory == PreviewFailureCategory.ProgramLifecycleKill)
            {
                _ = RecordPreviewPlaybackAsync(
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    finalPreferredProtocol,
                    failedProtocol,
                    false,
                    currentSession.UsedFallback || failedProtocol != SitePreviewProtocol.WebRtc,
                    failedProtocol,
                    failureReason);
                ApplyPreviewUnavailableState();
                return;
            }

            FinalizePreviewChainFailure(
                currentSession.DeviceCode,
                currentSession.PlaybackSessionId,
                finalPreferredProtocol,
                failedProtocol,
                failureCategory,
                failureReason,
                "HandlePreviewPlaybackFailed");
            return;
        }

        previewFallbackFailureProtocol = failedProtocol;
        previewFallbackFailureReason = failureReason;

        if (decision.ShouldRetrySameProtocol)
        {
            _ = WriteDiagnosticAsync(
                "preview-attempt-retry-scheduled",
                BuildPreviewAttemptDiagnostic(
                    "handle-playback-failed",
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    failedProtocol,
                    currentSession.ProtocolAttemptIndex,
                    currentSession.TotalAttemptIndex,
                    "retry_same_protocol",
                    failureCategory,
                    retryReason: decision.RetryReason,
                    nextProtocol: decision.NextProtocol));
            PreviewStatusText = failedProtocol == SitePreviewProtocol.WebRtc
                ? "重试 WebRTC"
                : $"重试 {GetProtocolLabel(failedProtocol)}";
            _ = StartPreviewAsync(
                currentSession.DeviceCode,
                failedProtocol,
                caller: "HandlePreviewPlaybackFailed.retry-same-protocol",
                retryReason: decision.RetryReason,
                failureCategory: failureCategory,
                failureProtocol: failedProtocol,
                failureReason: failureReason,
                resetAttemptChain: false);
            return;
        }

        _ = WriteDiagnosticAsync(
            "preview-attempt-retry-skipped",
            BuildPreviewAttemptDiagnostic(
                "handle-playback-failed",
                currentSession.DeviceCode,
                currentSession.PlaybackSessionId,
                failedProtocol,
                currentSession.ProtocolAttemptIndex,
                currentSession.TotalAttemptIndex,
                "skip_same_protocol_retry",
                failureCategory,
                retryReason: decision.RetryReason,
                nextProtocol: decision.NextProtocol));

        PreviewStatusText = decision.NextProtocol switch
        {
            SitePreviewProtocol.Flv => "切换到 FLV",
            SitePreviewProtocol.Hls => "切换到 HLS",
            _ => "暂无实时画面"
        };
        _ = StartPreviewAsync(
            currentSession.DeviceCode,
            decision.NextProtocol,
            caller: "HandlePreviewPlaybackFailed.next-protocol",
            retryReason: decision.RetryReason,
            failureCategory: failureCategory,
            failureProtocol: failedProtocol,
            failureReason: failureReason,
            resetAttemptChain: false);
    }

    private bool IsCurrentPreviewSession(string deviceCode, string playbackSessionId)
    {
        var currentSession = previewSession;
        return currentSession is not null
               && IsCurrentPreviewSession(currentSession, deviceCode, playbackSessionId);
    }

    private static bool IsCurrentPreviewSession(
        SitePreviewSession currentSession,
        string deviceCode,
        string playbackSessionId)
    {
        if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(playbackSessionId))
        {
            return false;
        }

        return string.Equals(currentSession.DeviceCode, deviceCode.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(currentSession.PlaybackSessionId, playbackSessionId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task RecordPreviewPlaybackAsync(
        string deviceCode,
        string playbackSessionId,
        SitePreviewProtocol preferredProtocol,
        SitePreviewProtocol protocol,
        bool isSuccess,
        bool usedFallback,
        SitePreviewProtocol failureProtocol,
        string? failureReason,
        bool isFinalChainFailure = false,
        string? finalFailureSummary = null)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        var normalizedFailureProtocol = isSuccess && !usedFallback
            ? SitePreviewProtocol.Unknown
            : failureProtocol;
        var normalizedFailureReason = string.IsNullOrWhiteSpace(failureReason)
            ? null
            : failureReason.Trim();

        try
        {
            await sitePreviewService.RecordPlaybackAsync(
                new SitePreviewPlaybackRecord
                {
                    DeviceCode = deviceCode.Trim(),
                    PlaybackSessionId = playbackSessionId.Trim(),
                    PreferredProtocol = preferredProtocol,
                    Protocol = protocol,
                    IsSuccess = isSuccess,
                    UsedFallback = usedFallback,
                    FailureProtocol = normalizedFailureProtocol,
                    FailureReason = normalizedFailureReason,
                    IsFinalChainFailure = isFinalChainFailure,
                    FinalFailureSummary = string.IsNullOrWhiteSpace(finalFailureSummary)
                        ? null
                        : finalFailureSummary.Trim(),
                    OccurredAt = DateTimeOffset.UtcNow
                });
        }
        catch (OperationCanceledException)
        {
            // Ignore background cancellation while persisting preview runtime state.
        }
        catch (Exception ex)
        {
            await WriteDiagnosticAsync(
                "preview-playback-record-failed",
                $"deviceCode={deviceCode}, protocol={ToProtocolKey(protocol)}, success={isSuccess}, usedFallback={usedFallback}, type={ex.GetType().FullName}, message={ex.Message}");
        }
    }

    private void FinalizePreviewChainFailure(
        string deviceCode,
        string? playbackSessionId,
        SitePreviewProtocol preferredProtocol,
        SitePreviewProtocol failedProtocol,
        PreviewFailureCategory failureCategory,
        string? failureReason,
        string caller)
    {
        var normalizedDeviceCode = string.IsNullOrWhiteSpace(deviceCode)
            ? string.Empty
            : deviceCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDeviceCode))
        {
            ApplyPreviewUnavailableState();
            return;
        }

        var normalizedSessionId = string.IsNullOrWhiteSpace(playbackSessionId)
            ? $"preview-final-{Guid.NewGuid():N}"
            : playbackSessionId.Trim();
        var usedFallback = preferredProtocol != failedProtocol
                           || previewFallbackFailureProtocol != SitePreviewProtocol.Unknown;
        _ = WriteDiagnosticAsync(
            "preview-chain-final-failed",
            $"caller={caller}, deviceCode={normalizedDeviceCode}, sessionId={normalizedSessionId}, preferredProtocol={ToProtocolKey(preferredProtocol)}, failedProtocol={ToProtocolKey(failedProtocol)}, failureCategory={failureCategory}, reason={failureReason ?? "none"}, summary={FinalPreviewFailureSummary}");
        _ = RecordPreviewPlaybackAsync(
            normalizedDeviceCode,
            normalizedSessionId,
            preferredProtocol,
            failedProtocol,
            false,
            usedFallback,
            failedProtocol,
            FinalPreviewFailureSummary,
            isFinalChainFailure: true,
            finalFailureSummary: FinalPreviewFailureSummary);
        ApplyPreviewUnavailableState();
    }

    private void ApplyPreviewUnavailableState()
    {
        previewPlaybackState = PreviewPlaybackState.Unavailable;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        PreviewProtocolText = string.Empty;
        PreviewStatusText = "暂无实时画面";
        previewSession = null;
        PreviewSessionJson = null;
        ResetPreviewAttemptState();
        ClosePreviewCommand.NotifyCanExecuteChanged();
    }

    private static string? BuildPreviewFailureReason(string? category, string? reason)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? null
            : category.Trim().ToLowerInvariant();
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim();

        var categoryText = normalizedCategory switch
        {
            "host_runtime_failed" => "预览宿主运行失败",
            "ready_timeout" => "预览就绪超时",
            "webrtc_api_missing" => "WebRTC 接口地址缺失",
            "webrtc_connection_failed" => "WebRTC 连接建立失败",
            "webrtc_ice_failed" => "WebRTC ICE 协商失败",
            "webrtc_offer_missing" => "WebRTC Offer 生成失败",
            "webrtc_answer_invalid" => "WebRTC Answer 无效",
            "webrtc_answer_failed" => "WebRTC 应答协商失败",
            "webrtc_ready_timeout" => "WebRTC 预览就绪超时",
            "webrtc_no_first_frame" => "WebRTC 已连接但首帧未到达",
            "webrtc_black_screen" => "WebRTC 已连接但画面仍为黑屏",
            "flv_not_supported" => "当前环境不支持 FLV 预览",
            "flv_play_failed" => "FLV 播放启动失败",
            "flv_stream_failed" => "FLV 流播放失败",
            "flv_ready_timeout" => "FLV 预览就绪超时",
            "hls_not_supported" => "当前环境不支持 HLS 预览",
            "hls_attach_failed" => "HLS 媒体挂载失败",
            "hls_load_source_failed" => "HLS 清单加载启动失败",
            "hls_start_load_failed" => "HLS 拉流启动失败",
            "hls_play_failed" => "HLS 播放启动失败",
            "hls_stream_failed" => "HLS 流播放失败",
            "hls_ready_timeout" => "HLS 预览就绪超时",
            "unsupported_protocol" => "预览协议不受支持",
            _ => null
        };

        if (categoryText is null)
        {
            return normalizedCategory is null
                ? normalizedReason
                : normalizedReason is null
                    ? $"预览播放失败（{normalizedCategory}）"
                    : $"预览播放失败（{normalizedCategory}: {normalizedReason}）";
        }

        if (normalizedReason is null
            || string.Equals(categoryText, normalizedReason, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedCategory, normalizedReason, StringComparison.OrdinalIgnoreCase))
        {
            return categoryText;
        }

        return $"{categoryText}（{normalizedReason}）";
    }

    public void HandleEditorClosed(SiteEditorViewModel editor)
    {
        if (ReferenceEquals(activeEditor, editor))
        {
            ClearCoordinatePick();
            activeEditor = null;
        }

        refreshTimer.Start();

        editor.SaveRequested -= HandleEditorSaveRequested;
        editor.CancelRequested -= HandleEditorCancelRequested;
        editor.CoordinatePickRequested -= HandleEditorCoordinatePickRequested;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= HandleRefreshTimerTick;
        previewResolveCts?.Cancel();
        previewResolveCts?.Dispose();
        previewResolveCts = null;
        ResetAcceptanceForcedPreviewFailure();
        previewPlaybackState = PreviewPlaybackState.Idle;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;
    }

    private async void HandleRefreshTimerTick(object? sender, EventArgs e)
    {
        if (activeEditor is not null)
        {
            return;
        }

        await LoadDashboardAsync(selectedPointDeviceCode);
    }

    private async Task LoadDashboardAsync(string? preferredSelectionDeviceCode = null, bool notifyOnError = false)
    {
        await dashboardLoadLock.WaitAsync();
        try
        {
            var snapshot = await siteMapQueryService.GetDashboardAsync(SelectedFilter.Value, SearchText);

            PlatformStatusText = snapshot.PlatformStatusText;
            PlatformStatusDetail = snapshot.PlatformStatusDetailText ?? "平台状态正常。";
            PointCount = snapshot.CoverageSummary.TotalPointCount;
            MappedPointCount = snapshot.CoverageSummary.MappedPointCount;
            UnmappedPointCount = snapshot.CoverageSummary.UnmappedPointCount;
            MonitoredCount = snapshot.MonitoredCount;
            FaultCount = snapshot.FaultCount;
            NormalCount = Math.Max(0, snapshot.MonitoredCount - snapshot.FaultCount);
            DispatchedCount = snapshot.DispatchedCount;
            PendingDispatchCount = snapshot.PendingDispatchCount;
            CurrentMapVisibleCount = snapshot.CoverageSummary.CurrentVisiblePointCount;
            MapCoverageDetailText = BuildMapCoverageDetailText(snapshot.CoverageSummary, SelectedFilter.Value);
            UpdateOverviewStatus(snapshot);

            LastRefreshText = snapshot.LastRefreshedAt.ToLocalTime().ToString("HH:mm:ss");
            OnPropertyChanged(nameof(LastRefreshText));
            OnPropertyChanged(nameof(MapCoverageText));
            OnPropertyChanged(nameof(DispatchQueueText));
            OnPropertyChanged(nameof(ManualDispatchText));
            OnPropertyChanged(nameof(UnmappedSectionTitle));
            OnPropertyChanged(nameof(SecondarySectionTitle));
            OnPropertyChanged(nameof(SecondarySectionDetail));
            OnPropertyChanged(nameof(SecondarySectionEmptyText));

            renderedPointCoordinates.Clear();
            VisiblePoints.Clear();
            foreach (var point in snapshot.VisiblePoints.Select(point => new SiteMapPointViewModel(point)))
            {
                VisiblePoints.Add(point);
            }

            VisibleAlerts.Clear();
            foreach (var alert in snapshot.VisibleAlerts.Select(alert => new SiteAlertDigestViewModel(alert)))
            {
                VisibleAlerts.Add(alert);
            }

            UnmappedPoints.Clear();
            foreach (var unmappedPoint in snapshot.UnmappedPoints.Select(point => new UnmappedPointDigestViewModel(point)))
            {
                UnmappedPoints.Add(unmappedPoint);
            }

            IgnoredPoints.Clear();
            foreach (var ignoredPoint in snapshot.IgnoredPoints.Select(point => new IgnoredPointDigestViewModel(point)))
            {
                IgnoredPoints.Add(ignoredPoint);
            }

            HasVisiblePoints = VisiblePoints.Count > 0;
            HasUnmappedPoints = UnmappedPoints.Count > 0;
            HasIgnoredPoints = IgnoredPoints.Count > 0;
            OnPropertyChanged(nameof(HasSecondaryItems));
            MapEmptyStateText = ResolveMapEmptyStateText(
                isMapHostConfigured,
                snapshot.IsPlatformConnected,
                snapshot.PlatformStatusText,
                snapshot.CoverageSummary,
                SelectedFilter.Value,
                IgnoredPoints.Count);

            var targetDeviceCode = preferredSelectionDeviceCode ?? selectedPointDeviceCode;
            if (!string.IsNullOrWhiteSpace(targetDeviceCode))
            {
                await SelectDeviceAsync(targetDeviceCode, notifyOnError);
                return;
            }

            var firstVisiblePoint = VisiblePoints.FirstOrDefault();
            if (firstVisiblePoint is not null)
            {
                SelectedPoint = firstVisiblePoint;
                return;
            }

            SelectedPoint = null;
            selectedPointDeviceCode = null;
            selectedDetailSource = null;
            SelectedDetail = null;
            CancelPreviewResolve();
            IsPreviewPlaybackReady = false;
            previewSession = null;
            PreviewSessionJson = null;
            PreviewProtocolText = string.Empty;
            PreviewStatusText = "选择点位后查看画面";
            ResetPreviewAttemptState();
            ClosePreviewCommand.NotifyCanExecuteChanged();
            RefreshMapHostState();
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=load-dashboard, notifyOnError={notifyOnError}, type={ex.GetType().FullName}, message={ex.Message}");

            if (notifyOnError)
            {
                Notify("加载失败", "地图数据暂不可用，请稍后重试。");
            }
        }
        finally
        {
            dashboardLoadLock.Release();
        }
    }

    private async Task LoadSelectedSiteDetailAsync(string? deviceCode, bool notifyOnError = false)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            selectedDetailSource = null;
            SelectedDetail = null;
            await RestartPreviewIfNeededAsync(null);
            return;
        }

        try
        {
            var detail = await siteMapQueryService.GetSiteDetailAsync(deviceCode);
            selectedDetailSource = detail;
            SelectedDetail = detail is null
                ? null
                : SiteDetailViewModel.FromSnapshot(detail, GetRenderedCoordinate(detail.DeviceCode));
            await RestartPreviewIfNeededAsync(detail?.DeviceCode ?? deviceCode);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=load-site-detail, deviceCode={deviceCode}, notifyOnError={notifyOnError}, type={ex.GetType().FullName}, message={ex.Message}");

            if (notifyOnError)
            {
                Notify("详情加载失败", "点位详情暂不可用，请稍后重试。");
            }
        }
    }

    private Task OpenPreviewAsync()
    {
        if (SelectedDetail is null)
        {
            return Task.CompletedTask;
        }

        if (HasActiveOrPendingPreview()
            && previewSession is not null
            && string.Equals(previewSession.DeviceCode, SelectedDetail.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        previewRequested = true;
        return StartPreviewAsync(
            SelectedDetail.DeviceCode,
            SitePreviewProtocol.WebRtc,
            caller: "OpenPreviewAsync",
            resetAttemptChain: true);
    }

    private void ClosePreview()
    {
        var currentSession = previewSession;
        previewRequested = false;
        CancelPreviewResolve();
        ResetAcceptanceForcedPreviewFailure();
        previewPlaybackState = PreviewPlaybackState.Idle;
        IsPreviewPlaybackReady = false;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = string.Empty;
        PreviewStatusText = SelectedDetail is null ? "选择点位后查看画面" : "点击点位后自动预览";
        ResetPreviewAttemptState();
        ClosePreviewCommand.NotifyCanExecuteChanged();
        if (currentSession is not null)
        {
            _ = WriteDiagnosticAsync(
                "preview-close-requested",
                BuildPreviewLifecycleDiagnostic("ClosePreview", currentSession, reason: "user_close"));
            _ = WriteDiagnosticAsync(
                "preview-chain-aborted",
                BuildPreviewAttemptDiagnostic(
                    "ClosePreview",
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    currentSession.SelectedProtocol,
                    currentSession.ProtocolAttemptIndex,
                    currentSession.TotalAttemptIndex,
                    "aborted",
                    PreviewFailureCategory.ProgramLifecycleKill,
                    retryReason: "user_close",
                    nextProtocol: SitePreviewProtocol.Unknown));
        }
    }

    public void PrepareForShutdown(string caller = "PrepareForShutdown")
    {
        var currentSession = previewSession;
        isWindowClosing = true;
        previewRequested = false;
        CancelPreviewResolve();
        previewPlaybackState = PreviewPlaybackState.Idle;
        IsPreviewPlaybackReady = false;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = string.Empty;
        PreviewStatusText = "预览已关闭。";
        ResetPreviewAttemptState();
        ClosePreviewCommand.NotifyCanExecuteChanged();
        if (currentSession is not null)
        {
            _ = WriteDiagnosticAsync(
                "preview-shutdown-requested",
                BuildPreviewLifecycleDiagnostic(caller, currentSession, reason: "prepare_for_shutdown"));
        }
    }

    public void BeginShutdownPreviewRelease(string caller = "BeginShutdownPreviewRelease")
    {
        var currentSession = previewSession;
        isWindowClosing = true;
        previewRequested = false;
        CancelPreviewResolve();
        ResetAcceptanceForcedPreviewFailure();
        IsPreviewPlaybackReady = false;
        if (currentSession is not null)
        {
            _ = WriteDiagnosticAsync(
                "preview-shutdown-requested",
                BuildPreviewLifecycleDiagnostic(caller, currentSession, reason: "begin_shutdown_release"));
            _ = WriteDiagnosticAsync(
                "preview-chain-aborted",
                BuildPreviewAttemptDiagnostic(
                    caller,
                    currentSession.DeviceCode,
                    currentSession.PlaybackSessionId,
                    currentSession.SelectedProtocol,
                    currentSession.ProtocolAttemptIndex,
                    currentSession.TotalAttemptIndex,
                    "aborted",
                    PreviewFailureCategory.ProgramLifecycleKill,
                    retryReason: "window_closing",
                    nextProtocol: SitePreviewProtocol.Unknown));
        }
    }

#if false
    public void CompleteShutdownAfterPreviewRelease()
    {
        previewPlaybackState = PreviewPlaybackState.Idle;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = "寰呭紑鍚?;
        PreviewStatusText = "棰勮宸插叧闂€?;
        ClosePreviewCommand.NotifyCanExecuteChanged();
    }

#endif

    public void CompleteShutdownAfterPreviewRelease(string caller = "CompleteShutdownAfterPreviewRelease")
    {
        var currentSession = previewSession;
        previewPlaybackState = PreviewPlaybackState.Idle;
        IsPreviewPlaybackReady = false;
        previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
        previewFallbackFailureReason = null;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = string.Empty;
        PreviewStatusText = "预览已关闭。";
        ResetPreviewAttemptState();
        ClosePreviewCommand.NotifyCanExecuteChanged();
        if (currentSession is not null)
        {
            _ = WriteDiagnosticAsync(
                "preview-shutdown-release-complete",
                BuildPreviewLifecycleDiagnostic(caller, currentSession, reason: "shutdown_release_complete"));
        }
    }

    private async Task StartPreviewAsyncLegacy(string deviceCode, SitePreviewProtocol? failedProtocol = null)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        CancelPreviewResolve();

        var cancellationTokenSource = new CancellationTokenSource();
        previewResolveCts = cancellationTokenSource;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = "连接中";
        PreviewStatusText = failedProtocol switch
        {
            SitePreviewProtocol.WebRtc => "WebRTC 不可用，正在切换备用预览。",
            SitePreviewProtocol.Flv => "FLV 不可用，正在切换 HLS 预览。",
            _ => "正在建立预览。"
        };
        ClosePreviewCommand.NotifyCanExecuteChanged();

        SitePreviewResolveResult result;
        try
        {
            result = failedProtocol.HasValue
                ? await sitePreviewService.ResolveFallbackPreviewAsync(deviceCode, failedProtocol.Value, cancellationTokenSource.Token)
                : await sitePreviewService.ResolveUserPreviewAsync(deviceCode, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "preview-resolve-exception",
                $"deviceCode={deviceCode}, failedProtocol={failedProtocol?.ToString() ?? "none"}, type={ex.GetType().FullName}, message={ex.Message}");
            PreviewProtocolText = "未开启";
            PreviewStatusText = "预览暂不可用，请稍后重试。";
            ClosePreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!ReferenceEquals(previewResolveCts, cancellationTokenSource) || isDisposed)
        {
            return;
        }

        if (!result.IsSuccess || result.Session is null)
        {
            previewSession = null;
            PreviewSessionJson = null;
            PreviewProtocolText = "未开启";
            PreviewStatusText = failedProtocol.HasValue
                ? "备用预览也不可用，请稍后重试。"
                : "预览暂不可用，请稍后重试。";
            ClosePreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        previewSession = result.Session;
        PreviewSessionJson = JsonSerializer.Serialize(ToPreviewPlaybackSession(result.Session), MapHostJsonOptions);
        PreviewProtocolText = GetProtocolBadgeText(result.Session.SelectedProtocol);
        PreviewStatusText = result.Session.SelectedProtocol == SitePreviewProtocol.WebRtc
            ? "正在连接 WebRTC 预览。"
            : $"已切换 {GetProtocolLabel(result.Session.SelectedProtocol)} 预览。";
        ClosePreviewCommand.NotifyCanExecuteChanged();
    }

    private async Task StartPreviewAsync(
        string deviceCode,
        SitePreviewProtocol requestedProtocol = SitePreviewProtocol.WebRtc,
        string caller = "StartPreviewAsync",
        string? retryReason = null,
        PreviewFailureCategory failureCategory = PreviewFailureCategory.None,
        SitePreviewProtocol failureProtocol = SitePreviewProtocol.Unknown,
        string? failureReason = null,
        bool resetAttemptChain = false)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        var normalizedDeviceCode = deviceCode.Trim();
        if (resetAttemptChain)
        {
            ResetPreviewAttemptState(normalizedDeviceCode);
        }

        if (!TryReservePreviewAttempt(normalizedDeviceCode, requestedProtocol, out var protocolAttemptIndex, out var totalAttemptIndex, out var abortReason))
        {
            _ = WriteDiagnosticAsync(
                "preview-chain-aborted",
                BuildPreviewAttemptDiagnostic(
                    caller,
                    normalizedDeviceCode,
                    previewSession?.PlaybackSessionId,
                    requestedProtocol,
                    GetProtocolAttemptCount(requestedProtocol),
                    previewTotalAttemptCount,
                    "aborted",
                    PreviewFailureCategory.None,
                    retryReason: abortReason,
                    nextProtocol: SitePreviewProtocol.Unknown));
            FinalizePreviewChainFailure(
                normalizedDeviceCode,
                previewSession?.PlaybackSessionId,
                previewPreferredProtocol == SitePreviewProtocol.Unknown ? requestedProtocol : previewPreferredProtocol,
                requestedProtocol,
                PreviewFailureCategory.ResolveUnavailable,
                abortReason,
                caller);
            return;
        }

        CancelPreviewResolve();
        var forceInitialWebRtcFailure = acceptanceForceNextWebRtcFailure;
        var forcedFailureCategory = acceptanceForcedFailureCategory;
        var forcedFailureReason = acceptanceForcedFailureReason;
        ResetAcceptanceForcedPreviewFailure();

        var previousRenderedSession = previewSession;
        var hadPreviewSessionJson = !string.IsNullOrWhiteSpace(PreviewSessionJson);
        var preserveRenderedSession = ShouldPreserveRenderedSessionDuringResolve();
        var cancellationTokenSource = new CancellationTokenSource();
        previewResolveCts = cancellationTokenSource;
        previewSession = null;
        if (preserveRenderedSession)
        {
            _ = WriteDiagnosticAsync(
                "preview-session-switch-pending",
                BuildPreviewLifecycleDiagnostic(
                    caller,
                    previousRenderedSession,
                    reason: "preserve_rendered_session_during_resolve",
                    oldSessionId: previousRenderedSession?.PlaybackSessionId,
                    newSessionId: "pending",
                    isSessionSwitch: true));
        }
        else
        {
            PreviewSessionJson = null;
            if (hadPreviewSessionJson || previousRenderedSession is not null)
            {
                _ = WriteDiagnosticAsync(
                    "preview-session-cleared-before-resolve",
                    BuildPreviewLifecycleDiagnostic(
                        caller,
                        previousRenderedSession,
                        reason: "clear_rendered_session_before_resolve"));
            }
        }

        IsPreviewPlaybackReady = false;
        if (requestedProtocol == SitePreviewProtocol.WebRtc && protocolAttemptIndex == 1)
        {
            previewFallbackFailureProtocol = SitePreviewProtocol.Unknown;
            previewFallbackFailureReason = null;
            previewPreferredProtocol = SitePreviewProtocol.WebRtc;
        }

        previewAttemptDeviceCode = normalizedDeviceCode;
        previewPlaybackState = requestedProtocol switch
        {
            SitePreviewProtocol.WebRtc when protocolAttemptIndex > 1 => PreviewPlaybackState.WebRtcPlaybackFailed,
            SitePreviewProtocol.WebRtc => PreviewPlaybackState.Idle,
            SitePreviewProtocol.Flv => PreviewPlaybackState.FallbackToFlv,
            SitePreviewProtocol.Hls => PreviewPlaybackState.FallbackToHls,
            _ => PreviewPlaybackState.Idle
        };
        PreviewProtocolText = string.Empty;
        PreviewStatusText = requestedProtocol switch
        {
            SitePreviewProtocol.WebRtc when protocolAttemptIndex > 1 => "重试 WebRTC",
            SitePreviewProtocol.WebRtc => "正在连接",
            SitePreviewProtocol.Flv when protocolAttemptIndex > 1 => "重试 FLV",
            SitePreviewProtocol.Flv => "切换到 FLV",
            SitePreviewProtocol.Hls => "切换到 HLS",
            _ => "正在连接"
        };
        _ = WriteDiagnosticAsync(
            "preview-attempt-start",
            BuildPreviewAttemptDiagnostic(
                caller,
                normalizedDeviceCode,
                null,
                requestedProtocol,
                protocolAttemptIndex,
                totalAttemptIndex,
                "started",
                failureCategory,
                retryReason,
                nextProtocol: SitePreviewProtocol.Unknown,
                failureProtocol,
                failureReason));
        ClosePreviewCommand.NotifyCanExecuteChanged();

        SitePreviewResolveResult result;
        try
        {
            result = await sitePreviewService.ResolvePreviewAsync(
                normalizedDeviceCode,
                [requestedProtocol],
                cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "preview-resolve-exception",
                $"deviceCode={normalizedDeviceCode}, requestedProtocol={ToProtocolKey(requestedProtocol)}, type={ex.GetType().FullName}, message={ex.Message}");
            _ = WriteDiagnosticAsync(
                "preview-attempt-end",
                BuildPreviewAttemptDiagnostic(
                    caller,
                    normalizedDeviceCode,
                    null,
                    requestedProtocol,
                    protocolAttemptIndex,
                    totalAttemptIndex,
                    "resolve_exception",
                    failureCategory,
                    retryReason: ex.GetType().Name,
                    nextProtocol: GetNextProtocol(requestedProtocol),
                    failureProtocol,
                    failureReason: ex.Message));
            var nextProtocol = GetNextProtocol(requestedProtocol);
            if (nextProtocol != SitePreviewProtocol.Unknown
                && previewRequested
                && !isDisposed)
            {
                previewFallbackFailureProtocol = requestedProtocol;
                previewFallbackFailureReason = ex.Message;
                await StartPreviewAsync(
                    normalizedDeviceCode,
                    nextProtocol,
                    caller: $"{caller}.resolve-exception-next-protocol",
                    retryReason: ex.GetType().Name,
                    failureCategory: PreviewFailureCategory.ResolveUnavailable,
                    failureProtocol: requestedProtocol,
                    failureReason: ex.Message,
                    resetAttemptChain: false);
            }
            else
            {
                FinalizePreviewChainFailure(
                    normalizedDeviceCode,
                    previewSession?.PlaybackSessionId,
                    previewPreferredProtocol == SitePreviewProtocol.Unknown ? requestedProtocol : previewPreferredProtocol,
                    requestedProtocol,
                    PreviewFailureCategory.ResolveUnavailable,
                    ex.Message,
                    caller);
            }
            return;
        }

        if (!ReferenceEquals(previewResolveCts, cancellationTokenSource) || isDisposed)
        {
            return;
        }

        if (!result.IsSuccess || result.Session is null)
        {
            previewPlaybackState = PreviewPlaybackState.Unavailable;
            previewSession = null;
            PreviewSessionJson = null;
            PreviewProtocolText = string.Empty;
            PreviewStatusText = "暂无实时画面";
            ClosePreviewCommand.NotifyCanExecuteChanged();
            _ = WriteDiagnosticAsync(
                "preview-session-unavailable",
                $"deviceCode={normalizedDeviceCode}, preferredProtocol={ToProtocolKey(previewPreferredProtocol == SitePreviewProtocol.Unknown ? requestedProtocol : previewPreferredProtocol)}, failureReason={result.FailureReason ?? "unknown"}, requestedProtocol={ToProtocolKey(requestedProtocol)}");
            _ = WriteDiagnosticAsync(
                "preview-attempt-end",
                BuildPreviewAttemptDiagnostic(
                    caller,
                    normalizedDeviceCode,
                    null,
                    requestedProtocol,
                    protocolAttemptIndex,
                    totalAttemptIndex,
                    "resolve_failed",
                    failureCategory,
                    retryReason: result.FailureReason,
                    nextProtocol: GetNextProtocol(requestedProtocol),
                    failureProtocol,
                    failureReason: result.FailureReason));

            var nextProtocol = GetNextProtocol(requestedProtocol);
            if (nextProtocol != SitePreviewProtocol.Unknown
                && previewRequested
                && !isDisposed)
            {
                previewFallbackFailureProtocol = requestedProtocol;
                previewFallbackFailureReason = result.FailureReason;
                _ = WriteDiagnosticAsync(
                    "preview-attempt-retry-skipped",
                    BuildPreviewAttemptDiagnostic(
                        caller,
                        normalizedDeviceCode,
                        null,
                        requestedProtocol,
                        protocolAttemptIndex,
                        totalAttemptIndex,
                        "resolve_failed_switch_protocol",
                        failureCategory,
                        retryReason: result.FailureReason,
                        nextProtocol: nextProtocol,
                        failureProtocol,
                        failureReason: result.FailureReason));
                await StartPreviewAsync(
                    normalizedDeviceCode,
                    nextProtocol,
                    caller: $"{caller}.resolve-failed-next-protocol",
                    retryReason: result.FailureReason,
                    failureCategory: PreviewFailureCategory.ResolveUnavailable,
                    failureProtocol: requestedProtocol,
                    failureReason: result.FailureReason,
                    resetAttemptChain: false);
            }
            else
            {
                FinalizePreviewChainFailure(
                    normalizedDeviceCode,
                    previewSession?.PlaybackSessionId,
                    previewPreferredProtocol == SitePreviewProtocol.Unknown ? requestedProtocol : previewPreferredProtocol,
                    requestedProtocol,
                    PreviewFailureCategory.ResolveUnavailable,
                    result.FailureReason,
                    caller);
            }
            return;
        }

        previewSession = result.Session with
        {
            PreferredProtocol = previewPreferredProtocol == SitePreviewProtocol.Unknown
                ? requestedProtocol
                : previewPreferredProtocol,
            ProtocolAttemptIndex = protocolAttemptIndex,
            TotalAttemptIndex = totalAttemptIndex,
            MaxTotalAttempts = MaxPreviewTotalAttempts
        };

        var preferredProtocol = GetEffectivePreferredProtocol(previewSession);
        var playbackSession = ToPreviewPlaybackSession(previewSession);
        if (forceInitialWebRtcFailure && previewSession.SelectedProtocol == SitePreviewProtocol.WebRtc)
        {
            playbackSession.ForceInitialWebRtcFailure = true;
            playbackSession.ForceFailureCategory = string.IsNullOrWhiteSpace(forcedFailureCategory)
                ? "acceptance_forced_webrtc_failure"
                : forcedFailureCategory;
            playbackSession.ForceFailureReason = string.IsNullOrWhiteSpace(forcedFailureReason)
                ? "forced by preview acceptance"
                : forcedFailureReason;
        }

        PreviewSessionJson = JsonSerializer.Serialize(playbackSession, MapHostJsonOptions);
        PreviewProtocolText = GetProtocolBadgeText(result.Session.SelectedProtocol);
        _ = WriteDiagnosticAsync(
            "preview-session-resolved",
            $"deviceCode={previewSession.DeviceCode}, sessionId={previewSession.PlaybackSessionId}, preferredProtocol={ToProtocolKey(preferredProtocol)}, finalProtocol={ToProtocolKey(previewSession.SelectedProtocol)}, fallbackTriggered={previewSession.UsedFallback || previewSession.SelectedProtocol != preferredProtocol}, attempted={string.Join(">", previewSession.AttemptedProtocols.Select(ToProtocolKey))}, totalAttemptIndex={previewSession.TotalAttemptIndex}, protocolAttemptIndex={previewSession.ProtocolAttemptIndex}, maxTotalAttempts={previewSession.MaxTotalAttempts}, sourceUrl={previewSession.SourceUrl}");

        if (previewSession.SelectedProtocol == SitePreviewProtocol.WebRtc)
        {
            previewPlaybackState = PreviewPlaybackState.WebRtcUrlAcquired;
            PreviewStatusText = "正在连接";
            if (previewSession.WebRtcUrlAcquired)
            {
                _ = WriteDiagnosticAsync(
                    "Preview",
                    $"WebRTC URL acquired: deviceCode={previewSession.DeviceCode}, protocol={ToProtocolKey(previewSession.SelectedProtocol)}, sourceUrl={previewSession.SourceUrl}");
            }
        }
        else
        {
            previewPlaybackState = previewSession.SelectedProtocol == SitePreviewProtocol.Flv
                ? PreviewPlaybackState.FallbackToFlv
                : PreviewPlaybackState.FallbackToHls;
            PreviewStatusText = "正在连接";
        }

        ClosePreviewCommand.NotifyCanExecuteChanged();
    }

    private async Task RestartPreviewIfNeededAsync(string? deviceCode)
    {
        if (ShouldPreservePreviewForDevice(deviceCode))
        {
            _ = WriteDiagnosticAsync(
                "preview-refresh-preserved",
                BuildPreviewDiagnosticState(
                    "RestartPreviewIfNeededAsync",
                    reason: "same_device_refresh_preserved"));
            ClosePreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        CancelPreviewResolve();
        IsPreviewPlaybackReady = false;
        previewSession = null;
        PreviewSessionJson = null;
        PreviewProtocolText = string.Empty;
        previewPreferredProtocol = SitePreviewProtocol.Unknown;

        if (!previewRequested || string.IsNullOrWhiteSpace(deviceCode))
        {
            ResetPreviewAttemptState();
            PreviewStatusText = string.IsNullOrWhiteSpace(deviceCode)
                ? "选择点位后查看画面"
                : "点击点位后自动预览";
            ClosePreviewCommand.NotifyCanExecuteChanged();
            return;
        }

        PreviewStatusText = "正在连接";
        await StartPreviewAsync(
            deviceCode,
            SitePreviewProtocol.WebRtc,
            caller: "RestartPreviewIfNeededAsync",
            resetAttemptChain: true);
    }

    private void CancelPreviewResolve()
    {
        previewResolveCts?.Cancel();
        previewResolveCts?.Dispose();
        previewResolveCts = null;
    }

    private Task OpenEditEditorAsync()
    {
        if (SelectedDetail is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _ = WriteDiagnosticAsync(
                "edit-dialog-open-start",
                $"deviceCode={SelectedDetail.DeviceCode}");
            _ = WriteDiagnosticAsync(
                "load-local-profile-start",
                $"deviceCode={SelectedDetail.DeviceCode}");

            var editor = SelectedDetail.CreateEditorViewModel();

            _ = WriteDiagnosticAsync(
                "load-local-profile-end",
                $"deviceCode={SelectedDetail.DeviceCode}, hasLocalProfile={editor.HasLocalProfile}");

            OpenEditor(editor);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=open-editor, deviceCode={SelectedDetail.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("打开失败", "编辑补充信息窗口暂不可用，请稍后重试。");
        }

        return Task.CompletedTask;
    }

    private async Task OpenNotificationSettingsAsync()
    {
        try
        {
            var viewModel = new NotificationSettingsViewModel(webhookEndpointStore, diagnosticService);
            await viewModel.LoadAsync();
            NotificationSettingsDialogRequested?.Invoke(this, new NotificationSettingsDialogRequestedEventArgs(viewModel));
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=open-notification-settings, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("通知设置打开失败", "通知设置窗口打开失败，请稍后重试。");
        }
    }

    private async Task OpenTemplateSettingsAsync()
    {
        try
        {
            var viewModel = new NotificationTemplateSettingsViewModel(
                notificationTemplateStore,
                notificationTemplateRenderService,
                diagnosticService);
            await viewModel.LoadAsync();
            NotificationTemplateSettingsDialogRequested?.Invoke(this, new NotificationTemplateSettingsDialogRequestedEventArgs(viewModel));
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=open-template-settings, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("模板设置打开失败", "模板设置窗口打开失败，请稍后重试。");
        }
    }

    private async Task ToggleMonitoringAsync()
    {
        if (SelectedDetail is null)
        {
            return;
        }

        try
        {
            var input = SelectedDetail.CreateLocalProfileInput(!SelectedDetail.IsMonitored);
            await siteLocalProfileService.UpsertAsync(input);
            await LoadDashboardAsync(SelectedDetail.DeviceCode, true);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=toggle-monitoring, deviceCode={SelectedDetail.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("操作失败", "巡检状态更新失败，请稍后重试。");
        }
    }

    private async Task ManualDispatchAsync()
    {
        if (SelectedDetail is null)
        {
            return;
        }

        var deviceCode = SelectedDetail.DeviceCode;
        _ = WriteDiagnosticAsync(
            "manual-dispatch-open-start",
            $"deviceCode={deviceCode}");

        try
        {
            var dialogViewModel = new ManualDispatchDialogViewModel(
                await Task.Run(() => dispatchService.PrepareManualDispatchAsync(deviceCode)));
            OpenManualDispatchDialog(dialogViewModel);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "manual-dispatch-exception-caught",
                $"deviceCode={deviceCode}, stage=open, type={ex.GetType().FullName}, message={ex.Message}");
            _ = WriteDiagnosticAsync(
                "manual-dispatch-open-ui-failed",
                $"deviceCode={deviceCode}, stage=open, message={ex.Message}");
            Notify(
                "派单失败",
                ResolveDispatchFailureStage(ex) == "validation"
                    ? ex.Message
                    : "手工派单窗口打开失败，请稍后重试。");
        }
    }

    public void HandleManualDispatchDialogClosed(ManualDispatchDialogViewModel viewModel)
    {
        if (ReferenceEquals(activeManualDispatchDialog, viewModel))
        {
            activeManualDispatchDialog.ExecuteConfirmed -= HandleManualDispatchExecuteConfirmed;
            activeManualDispatchDialog.CancelRequested -= HandleManualDispatchCancelRequested;
            activeManualDispatchDialog = null;
            if (activeEditor is null && activeCloseWorkOrderDialog is null)
            {
                refreshTimer.Start();
            }
        }
    }

    private async Task ToggleIgnoreAsync()
    {
        if (SelectedDetail is null)
        {
            return;
        }

        try
        {
            var deviceCode = SelectedDetail.DeviceCode;
            var isIgnored = SelectedDetail.IsIgnored;

            if (isIgnored)
            {
                await siteLocalProfileService.RestoreAsync(deviceCode);
            }
            else
            {
                await siteLocalProfileService.IgnoreAsync(deviceCode);
            }

            var preferredSelectionDeviceCode = isIgnored
                ? SelectedFilter.Value == SiteDashboardFilter.Ignored ? null : deviceCode
                : SelectedFilter.Value == SiteDashboardFilter.Ignored ? deviceCode : null;

            await LoadDashboardAsync(preferredSelectionDeviceCode, true);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=toggle-ignore, deviceCode={SelectedDetail.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("操作失败", "点位关注范围更新失败，请稍后重试。");
        }
    }

    private async Task ConfirmRecoveryAsync()
    {
        if (SelectedDetail?.DispatchRecordId is not long dispatchRecordId)
        {
            return;
        }

        try
        {
            var dialogViewModel = new CloseWorkOrderDialogViewModel(
                await dispatchService.PrepareCloseWorkOrderAsync(dispatchRecordId));
            OpenCloseWorkOrderDialog(dialogViewModel);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=confirm-recovery, dispatchRecordId={dispatchRecordId}, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("恢复确认失败", "恢复状态更新失败，请稍后重试。");
        }
    }

    private void OpenEditor(SiteEditorViewModel editor)
    {
        if (activeEditor is not null)
        {
            if (IsCoordinatePickActive || coordinatePickCandidate is not null)
            {
                ClearCoordinatePick();
            }

            activeEditor.RequestClose();
        }

        activeEditor = editor;
        refreshTimer.Stop();
        editor.SaveRequested += HandleEditorSaveRequested;
        editor.CancelRequested += HandleEditorCancelRequested;
        editor.CoordinatePickRequested += HandleEditorCoordinatePickRequested;

        try
        {
            EditorDialogRequested?.Invoke(this, new SiteEditorDialogRequestedEventArgs(editor));
        }
        catch
        {
            editor.SaveRequested -= HandleEditorSaveRequested;
            editor.CancelRequested -= HandleEditorCancelRequested;
            editor.CoordinatePickRequested -= HandleEditorCoordinatePickRequested;
            activeEditor = null;
            refreshTimer.Start();
            throw;
        }
    }

    private void OpenManualDispatchDialog(ManualDispatchDialogViewModel dialogViewModel)
    {
        if (activeManualDispatchDialog is not null)
        {
            activeManualDispatchDialog.RequestClose();
        }

        activeManualDispatchDialog = dialogViewModel;
        refreshTimer.Stop();
        dialogViewModel.ExecuteConfirmed += HandleManualDispatchExecuteConfirmed;
        dialogViewModel.CancelRequested += HandleManualDispatchCancelRequested;

        try
        {
            ManualDispatchDialogRequested?.Invoke(this, new ManualDispatchDialogRequestedEventArgs(dialogViewModel));
        }
        catch
        {
            dialogViewModel.ExecuteConfirmed -= HandleManualDispatchExecuteConfirmed;
            dialogViewModel.CancelRequested -= HandleManualDispatchCancelRequested;
            activeManualDispatchDialog = null;
            refreshTimer.Start();
            throw;
        }
    }

    public void HandleCloseWorkOrderDialogClosed(CloseWorkOrderDialogViewModel viewModel)
    {
        if (ReferenceEquals(activeCloseWorkOrderDialog, viewModel))
        {
            activeCloseWorkOrderDialog.ExecuteConfirmed -= HandleCloseWorkOrderExecuteConfirmed;
            activeCloseWorkOrderDialog.CancelRequested -= HandleCloseWorkOrderCancelRequested;
            activeCloseWorkOrderDialog = null;
            if (activeEditor is null && activeManualDispatchDialog is null)
            {
                refreshTimer.Start();
            }
        }
    }

    private void OpenCloseWorkOrderDialog(CloseWorkOrderDialogViewModel dialogViewModel)
    {
        if (activeCloseWorkOrderDialog is not null)
        {
            activeCloseWorkOrderDialog.RequestClose();
        }

        activeCloseWorkOrderDialog = dialogViewModel;
        refreshTimer.Stop();
        dialogViewModel.ExecuteConfirmed += HandleCloseWorkOrderExecuteConfirmed;
        dialogViewModel.CancelRequested += HandleCloseWorkOrderCancelRequested;

        try
        {
            CloseWorkOrderDialogRequested?.Invoke(this, new CloseWorkOrderDialogRequestedEventArgs(dialogViewModel));
        }
        catch
        {
            dialogViewModel.ExecuteConfirmed -= HandleCloseWorkOrderExecuteConfirmed;
            dialogViewModel.CancelRequested -= HandleCloseWorkOrderCancelRequested;
            activeCloseWorkOrderDialog = null;
            refreshTimer.Start();
            throw;
        }
    }

    private void SelectPoint(SiteMapPointViewModel? point)
    {
        if (point is not null)
        {
            SelectedPoint = point;
        }
    }

    private async void SelectAlert(SiteAlertDigestViewModel? alert)
    {
        if (alert is null)
        {
            return;
        }

        await SelectDeviceAsync(alert.PointId);
    }

    private async void SelectUnmappedPoint(UnmappedPointDigestViewModel? point)
    {
        if (point is null)
        {
            return;
        }

        OpenUnmappedSecondaryEntry();
        await SelectDeviceAsync(point.DeviceCode);
    }

    private async void SelectIgnoredPoint(IgnoredPointDigestViewModel? point)
    {
        if (point is null)
        {
            return;
        }

        OpenIgnoredSecondaryEntry();
        await SelectDeviceAsync(point.DeviceCode);
    }

    private async void EditUnmappedPoint(UnmappedPointDigestViewModel? point)
    {
        if (point is null)
        {
            return;
        }

        await SelectDeviceAsync(point.DeviceCode, true);
        await OpenEditEditorAsync();
    }

    private void OpenUnmappedSecondaryEntry()
    {
        secondaryEntryMode = SecondaryEntryMode.Unmapped;
        IsSecondaryPanelOpen = true;
        OnPropertyChanged(nameof(IsIgnoredSecondaryMode));
        OnPropertyChanged(nameof(SecondarySectionTitle));
        OnPropertyChanged(nameof(SecondarySectionDetail));
        OnPropertyChanged(nameof(SecondarySectionEmptyText));
        OnPropertyChanged(nameof(HasSecondaryItems));
    }

    private void OpenIgnoredSecondaryEntry()
    {
        secondaryEntryMode = SecondaryEntryMode.Ignored;
        IsSecondaryPanelOpen = true;
        OnPropertyChanged(nameof(IsIgnoredSecondaryMode));
        OnPropertyChanged(nameof(SecondarySectionTitle));
        OnPropertyChanged(nameof(SecondarySectionDetail));
        OnPropertyChanged(nameof(SecondarySectionEmptyText));
        OnPropertyChanged(nameof(HasSecondaryItems));
    }

    private async Task SelectDeviceAsync(string deviceCode, bool notifyOnError = false)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        selectedPointDeviceCode = deviceCode.Trim();
        var point = VisiblePoints.FirstOrDefault(item => item.DeviceCode.Equals(selectedPointDeviceCode, StringComparison.OrdinalIgnoreCase));
        if (point is not null)
        {
            SelectedPoint = point;
            return;
        }

        SelectedPoint = null;
        selectedPointDeviceCode = deviceCode.Trim();
        RefreshMapHostState();
        await LoadSelectedSiteDetailAsync(selectedPointDeviceCode, notifyOnError);
    }

    private async Task HandleMapPointSelectedAsync(string deviceCode)
    {
        var isSameSelection = string.Equals(selectedPointDeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase);
        previewRequested = true;

        if (isSameSelection)
        {
            if (!HasActiveOrPendingPreview())
            {
                await StartPreviewAsync(
                    deviceCode,
                    SitePreviewProtocol.WebRtc,
                    caller: "HandleMapPointSelectedAsync.same-selection",
                    resetAttemptChain: true);
            }

            return;
        }

        await SelectDeviceAsync(deviceCode);
    }

    private async void HandleEditorSaveRequested(object? sender, EventArgs e)
    {
        if (sender is not SiteEditorViewModel editor)
        {
            return;
        }

        if (!editor.TryBuildInput(out var input, out var errorMessage))
        {
            Notify("保存失败", errorMessage ?? "输入不完整。");
            return;
        }

        try
        {
            _ = WriteDiagnosticAsync(
                "save-start",
                $"deviceCode={input!.DeviceCode}");

            await siteLocalProfileService.UpsertAsync(input!);
            editor.RequestClose();
            await LoadDashboardAsync(input.DeviceCode, true);

            _ = WriteDiagnosticAsync(
                "save-end",
                $"deviceCode={input.DeviceCode}, result=success");
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=save-editor, deviceCode={editor.DeviceCode}, type={ex.GetType().FullName}, message={ex.Message}");
            _ = WriteDiagnosticAsync(
                "save-end",
                $"deviceCode={editor.DeviceCode}, result=failure");
            Notify("保存失败", "本地补充信息保存失败，请稍后重试。");
        }
    }

    private void HandleEditorCancelRequested(object? sender, EventArgs e)
    {
        if (sender is not SiteEditorViewModel editor)
        {
            return;
        }

        ClearCoordinatePick();
        editor.RequestClose();
    }

    private async void HandleManualDispatchExecuteConfirmed(object? sender, EventArgs e)
    {
        if (sender is not ManualDispatchDialogViewModel viewModel)
        {
            return;
        }

        _ = WriteDiagnosticAsync(
            "manual-dispatch-confirm-accepted",
            $"deviceCode={viewModel.DeviceCode}");
        viewModel.IsSubmitting = true;

        try
        {
            await Task.Run(() => dispatchService.ManualDispatchAsync(new ManualDispatchRequest
            {
                DeviceCode = viewModel.DeviceCode
            }));

            viewModel.RequestClose();
            await LoadDashboardAsync(viewModel.DeviceCode, true);
            _ = WriteDiagnosticAsync(
                "manual-dispatch-execute-ui-succeeded",
                $"deviceCode={viewModel.DeviceCode}");
        }
        catch (Exception ex)
        {
            var failureStage = ResolveDispatchFailureStage(ex);
            _ = WriteDiagnosticAsync(
                "manual-dispatch-exception-caught",
                $"deviceCode={viewModel.DeviceCode}, stage=execute, type={ex.GetType().FullName}, message={ex.Message}");
            _ = WriteDiagnosticAsync(
                "manual-dispatch-execute-ui-failed",
                $"deviceCode={viewModel.DeviceCode}, stage=execute, message={ex.Message}");
            Notify(
                failureStage == "send" ? "发送失败" : "派单失败",
                failureStage switch
                {
                    "validation" => ex.Message,
                    "send" => "通知发送失败，请检查配置或稍后重试。",
                    _ => "手工派单处理失败，请稍后重试。"
                });
        }
        finally
        {
            viewModel.IsSubmitting = false;
        }
    }

    private void HandleManualDispatchCancelRequested(object? sender, EventArgs e)
    {
        if (sender is ManualDispatchDialogViewModel viewModel)
        {
            viewModel.RequestClose();
        }
    }

    private async void HandleCloseWorkOrderExecuteConfirmed(object? sender, EventArgs e)
    {
        if (sender is not CloseWorkOrderDialogViewModel viewModel)
        {
            return;
        }

        viewModel.IsSubmitting = true;

        try
        {
            await dispatchService.CloseWorkOrderAsync(new CloseWorkOrderRequest
            {
                WorkOrderId = viewModel.WorkOrderId,
                ClosingRemark = viewModel.ClosingRemark
            });

            viewModel.RequestClose();
            await LoadDashboardAsync(viewModel.DeviceCode, true);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=close-work-order-confirmed, workOrderId={viewModel.WorkOrderId}, type={ex.GetType().FullName}, message={ex.Message}");
            Notify("归档失败", ex.Message);
        }
        finally
        {
            viewModel.IsSubmitting = false;
        }
    }

    private void HandleCloseWorkOrderCancelRequested(object? sender, EventArgs e)
    {
        if (sender is CloseWorkOrderDialogViewModel viewModel)
        {
            viewModel.RequestClose();
        }
    }

    private void HandleEditorCoordinatePickRequested(object? sender, EventArgs e)
    {
        if (sender is not SiteEditorViewModel editor)
        {
            return;
        }

        if (!isMapHostConfigured)
        {
            Notify("地图未配置", "请先准备 amap-js.json，再使用地图补录坐标。");
            return;
        }

        var previousEditor = activeEditor;
        if (previousEditor is not null
            && !ReferenceEquals(previousEditor, editor)
            && (IsCoordinatePickActive || coordinatePickCandidate is not null))
        {
            ClearCoordinatePick();
        }

        activeEditor = editor;
        if (!ReferenceEquals(previousEditor, editor) || !IsCoordinatePickActive)
        {
            coordinatePickCandidate = null;
        }

        IsCoordinatePickActive = true;
        MapInteractionHint = BuildCoordinatePickHint(coordinatePickCandidate);
        _ = WriteDiagnosticAsync(
            "map-host-interaction-start",
            $"deviceCode={editor.DeviceCode}, action=coordinate-pick");
        editor.MarkCoordinatePickPending(coordinatePickCandidate);
    }

    private void ClearCoordinatePick()
    {
        if (IsCoordinatePickActive && activeEditor is not null)
        {
            _ = WriteDiagnosticAsync(
                "map-host-interaction-end",
                $"deviceCode={activeEditor.DeviceCode}, action=coordinate-pick-cleared");
        }

        var stateChanged = IsCoordinatePickActive;
        coordinatePickCandidate = null;
        IsCoordinatePickActive = false;
        if (!stateChanged)
        {
            RefreshMapHostState();
        }

        MapInteractionHint = ResolveDefaultMapInteractionHint();
    }

    private DemoCoordinate? GetRenderedCoordinate(string deviceCode)
    {
        return renderedPointCoordinates.TryGetValue(deviceCode, out var coordinate)
            ? coordinate
            : null;
    }

    private void RefreshMapHostState()
    {
        var state = new MapHostStateDto
        {
            Points = VisiblePoints.Select(point => point.ToMapHostPoint()).ToList(),
            SelectedDeviceCode = SelectedPoint?.DeviceCode,
            CandidateCoordinate = IsCoordinatePickActive && coordinatePickCandidate is not null
                ? new MapHostCandidateCoordinateDto
                {
                    Longitude = coordinatePickCandidate.Longitude,
                    Latitude = coordinatePickCandidate.Latitude
                }
                : null,
            CoordinatePickActive = IsCoordinatePickActive
        };

        MapHostStateJson = JsonSerializer.Serialize(state, MapHostJsonOptions);
    }

    private void RaiseMediaStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasPreviewSession));
        OnPropertyChanged(nameof(HasPreviewProtocolBadge));
        OnPropertyChanged(nameof(HasSnapshotFallback));
        OnPropertyChanged(nameof(ShowMediaSurface));
        OnPropertyChanged(nameof(ShowMediaEmptyState));
        OnPropertyChanged(nameof(MediaEmptyStateText));
    }

    private void RefreshSelectedDetail()
    {
        if (selectedDetailSource is null)
        {
            return;
        }

        SelectedDetail = SiteDetailViewModel.FromSnapshot(
            selectedDetailSource,
            GetRenderedCoordinate(selectedDetailSource.DeviceCode));
    }

    private void UpdateOverviewStatus(SiteDashboardSnapshot snapshot)
    {
        if (!snapshot.IsPlatformConnected)
        {
            InspectionStatusText = "巡检待机";
            InspectionStatusDetail = "平台未连通，静默巡检按降级模式运行。";
        }
        else if (snapshot.MonitoredCount <= 0)
        {
            InspectionStatusText = "巡检待机";
            InspectionStatusDetail = "当前无纳入静默巡检的点位。";
        }
        else if (snapshot.FaultCount > 0)
        {
            InspectionStatusText = "异常值守";
            InspectionStatusDetail = $"已纳管 {snapshot.MonitoredCount} 个点位，其中 {snapshot.FaultCount} 个需要关注。";
        }
        else
        {
            InspectionStatusText = "值守运行";
            InspectionStatusDetail = $"已纳管 {snapshot.MonitoredCount} 个点位，当前画面正常。";
        }

        if (snapshot.DispatchedCount > 0)
        {
            DispatchOverviewText = "派单跟进";
            DispatchOverviewDetail = $"{snapshot.DispatchedCount} 个点位处于派单或恢复链路。";
        }
        else if (snapshot.FaultCount > 0)
        {
            DispatchOverviewText = "待处置";
            DispatchOverviewDetail = $"{snapshot.FaultCount} 个点位需要关注。";
        }
        else
        {
            DispatchOverviewText = "派单待命";
            DispatchOverviewDetail = "当前无派单中的点位。";
        }
    }

    private static string BuildMapCoverageDetailText(
        MapCoverageSummary coverageSummary,
        SiteDashboardFilter filter)
    {
        if (filter == SiteDashboardFilter.Ignored)
        {
            return "已忽略点位仅在次级列表中查看，不参与地图主视图。";
        }

        if (coverageSummary.FilteredPointCount <= 0)
        {
            return "当前筛选下暂无落图点位。";
        }

        return "地图主视图聚焦纳管点位状态，绿色正常，红色异常。";
    }

    private static string ResolveMapEmptyStateText(
        bool isMapHostConfigured,
        bool isPlatformConnected,
        string platformStatusText,
        MapCoverageSummary coverageSummary,
        SiteDashboardFilter filter,
        int ignoredPointCount)
    {
        if (!isMapHostConfigured)
        {
            return "地图未配置。请准备 amap-js.json 后重启。";
        }

        if (!isPlatformConnected)
        {
            return $"{platformStatusText}。请补充 ACIS 配置后重试。";
        }

        if (filter == SiteDashboardFilter.Ignored)
        {
            return ignoredPointCount > 0
                ? "已忽略点位保留在次级列表中，可在右侧详情恢复关注。"
                : "当前无已忽略点位。";
        }

        if (coverageSummary.FilteredPointCount <= 0)
        {
            return "当前筛选条件下暂无点位。";
        }

        return "当前筛选条件下暂无可展示点位。";
    }

    private string ResolveDefaultMapInteractionHint()
    {
        return isMapHostConfigured
            ? "单击地图点位可联动右侧详情。"
            : "地图未配置，暂不支持坐标补录。";
    }

    private static string BuildCoordinatePickHint(DemoCoordinate? coordinate)
    {
        return coordinate is null
            ? "请在地图上连续点击选择位置，确认后再保存。"
            : $"当前候选手工坐标：{coordinate.Longitude:F6}, {coordinate.Latitude:F6}。确认后点击“保存补充信息”。";
    }

    private async Task PersistSelectedMapStyleAsync(string mapStyleKey)
    {
        try
        {
            await mapStylePreferenceStore.SaveMapStyleAsync(mapStyleKey);
        }
        catch (Exception ex)
        {
            _ = WriteDiagnosticAsync(
                "exception-caught",
                $"source=save-map-style, mapStyle={mapStyleKey}, type={ex.GetType().FullName}, message={ex.Message}");
        }
    }

    private static string ResolveMapStyleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "amap://styles/grey";
        }

        var normalized = value.Trim();
        return string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "native", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase)
            ? "amap://styles/grey"
            : normalized;
    }

    private static PreviewPlaybackSessionDto ToPreviewPlaybackSession(SitePreviewSession session)
    {
        return new PreviewPlaybackSessionDto
        {
            PlaybackSessionId = session.PlaybackSessionId,
            DeviceCode = session.DeviceCode,
            Protocol = ToProtocolKey(session.SelectedProtocol),
            SourceUrl = session.SourceUrl,
            WebRtcApiUrl = session.WebRtcApiUrl,
            WebRtcUrlAcquired = session.WebRtcUrlAcquired,
            ReadyTimeoutSeconds = session.ReadyTimeoutSeconds,
            PreferredProtocol = ToProtocolKey(session.PreferredProtocol),
            ProtocolAttemptIndex = session.ProtocolAttemptIndex,
            TotalAttemptIndex = session.TotalAttemptIndex,
            MaxTotalAttempts = session.MaxTotalAttempts
        };
    }

    private static SitePreviewProtocol ParsePreviewProtocol(string? protocol)
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

    private static string GetProtocolLabel(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => "WebRTC",
            SitePreviewProtocol.Flv => "FLV",
            SitePreviewProtocol.Hls => "HLS",
            SitePreviewProtocol.H5 => "H5",
            _ => "未知"
        };
    }

    private static string GetProtocolBadgeText(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => "WebRTC",
            SitePreviewProtocol.Flv => "FLV 备用",
            SitePreviewProtocol.Hls => "HLS 备用",
            SitePreviewProtocol.H5 => "H5 兜底",
            _ => "未开启"
        };
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

    private SitePreviewProtocol GetEffectivePreferredProtocol(SitePreviewSession session)
    {
        if (previewPreferredProtocol != SitePreviewProtocol.Unknown)
        {
            return previewPreferredProtocol;
        }

        return session.AttemptedProtocols.Count > 0
            ? session.AttemptedProtocols[0]
            : session.SelectedProtocol;
    }

    private static SitePreviewProtocol GetRequestedPreviewProtocol(SitePreviewProtocol? failedProtocol)
    {
        return failedProtocol switch
        {
            SitePreviewProtocol.WebRtc => SitePreviewProtocol.Flv,
            SitePreviewProtocol.Flv => SitePreviewProtocol.Hls,
            _ => SitePreviewProtocol.WebRtc
        };
    }

    public string BuildPreviewDiagnosticState(
        string caller,
        string? reason = null,
        string? deviceCodeOverride = null,
        string? sessionIdOverride = null,
        string? protocolOverride = null,
        bool? isFallbackSessionOverride = null,
        string? oldSessionId = null,
        string? newSessionId = null,
        bool isSessionSwitch = false)
    {
        return BuildPreviewLifecycleDiagnostic(
            caller,
            previewSession,
            reason: reason,
            deviceCodeOverride: deviceCodeOverride,
            sessionIdOverride: sessionIdOverride,
            protocolOverride: protocolOverride,
            isFallbackSessionOverride: isFallbackSessionOverride,
            oldSessionId: oldSessionId,
            newSessionId: newSessionId,
            isSessionSwitch: isSessionSwitch);
    }

    private string BuildPreviewLifecycleDiagnostic(
        string caller,
        SitePreviewSession? session,
        string? reason = null,
        string? deviceCodeOverride = null,
        string? sessionIdOverride = null,
        string? protocolOverride = null,
        bool? isFallbackSessionOverride = null,
        string? oldSessionId = null,
        string? newSessionId = null,
        bool isSessionSwitch = false)
    {
        var targetSession = session ?? previewSession;
        var deviceCode = deviceCodeOverride ?? targetSession?.DeviceCode ?? selectedPointDeviceCode ?? "none";
        var sessionId = sessionIdOverride ?? targetSession?.PlaybackSessionId ?? "none";
        var protocol = protocolOverride ?? (targetSession is null ? "unknown" : ToProtocolKey(targetSession.SelectedProtocol));
        var fallbackSession = isFallbackSessionOverride ?? targetSession?.SelectedProtocol is SitePreviewProtocol.Flv or SitePreviewProtocol.Hls;
        return string.Join(", ", new[]
        {
            $"caller={SanitizeDiagnosticValue(caller)}",
            $"reason={SanitizeDiagnosticValue(reason ?? "none")}",
            $"deviceCode={SanitizeDiagnosticValue(deviceCode)}",
            $"sessionId={SanitizeDiagnosticValue(sessionId)}",
            $"protocol={SanitizeDiagnosticValue(protocol)}",
            $"previewRequested={previewRequested}",
            $"previewPlaybackState={SanitizeDiagnosticValue(previewPlaybackState.ToString())}",
            $"hasPreviewSessionJson={PreviewSessionJson is not null}",
            $"hasPreviewSession={previewSession is not null}",
            $"selectedPointDeviceCode={SanitizeDiagnosticValue(selectedPointDeviceCode ?? "none")}",
            $"isWindowClosing={isWindowClosing}",
            $"isFallbackSession={fallbackSession}",
            $"isSessionSwitch={isSessionSwitch}",
            $"oldSessionId={SanitizeDiagnosticValue(oldSessionId ?? targetSession?.PlaybackSessionId ?? "none")}",
            $"newSessionId={SanitizeDiagnosticValue(newSessionId ?? "none")}",
            $"stackTrace={SanitizeDiagnosticValue(Environment.StackTrace)}"
        });
    }

    private string BuildPreviewAttemptDiagnostic(
        string caller,
        string deviceCode,
        string? sessionId,
        SitePreviewProtocol protocol,
        int protocolAttemptIndex,
        int totalAttemptIndex,
        string stage,
        PreviewFailureCategory failureCategory,
        string? retryReason,
        SitePreviewProtocol nextProtocol,
        SitePreviewProtocol failureProtocol = SitePreviewProtocol.Unknown,
        string? failureReason = null)
    {
        return string.Join(", ", new[]
        {
            $"caller={SanitizeDiagnosticValue(caller)}",
            $"deviceCode={SanitizeDiagnosticValue(deviceCode)}",
            $"sessionId={SanitizeDiagnosticValue(sessionId ?? "none")}",
            $"protocol={ToProtocolKey(protocol)}",
            $"protocolAttemptIndex={protocolAttemptIndex}",
            $"totalAttemptIndex={totalAttemptIndex}",
            $"maxTotalAttempts={MaxPreviewTotalAttempts}",
            $"stage={SanitizeDiagnosticValue(stage)}",
            $"retryReason={SanitizeDiagnosticValue(retryReason ?? "none")}",
            $"failureCategory={SanitizeDiagnosticValue(failureCategory.ToString())}",
            $"failureProtocol={ToProtocolKey(failureProtocol)}",
            $"failureReason={SanitizeDiagnosticValue(failureReason ?? "none")}",
            $"nextProtocol={ToProtocolKey(nextProtocol)}"
        });
    }

    private bool ShouldPreservePreviewForDevice(string? deviceCode)
    {
        return previewRequested
               && !string.IsNullOrWhiteSpace(deviceCode)
               && string.Equals(
                   previewAttemptDeviceCode ?? previewSession?.DeviceCode ?? selectedPointDeviceCode,
                   deviceCode.Trim(),
                   StringComparison.OrdinalIgnoreCase)
               && HasActiveOrPendingPreview();
    }

    private bool ShouldPreserveRenderedSessionDuringResolve()
    {
        return previewRequested
               && !isWindowClosing
               && !string.IsNullOrWhiteSpace(PreviewSessionJson);
    }

    private bool TryReservePreviewAttempt(
        string deviceCode,
        SitePreviewProtocol protocol,
        out int protocolAttemptIndex,
        out int totalAttemptIndex,
        out string? abortReason)
    {
        protocolAttemptIndex = 0;
        totalAttemptIndex = 0;
        abortReason = null;

        if (!string.Equals(previewAttemptDeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
        {
            ResetPreviewAttemptState(deviceCode);
        }

        if (previewTotalAttemptCount >= MaxPreviewTotalAttempts)
        {
            abortReason = "max_total_attempts_reached";
            return false;
        }

        previewTotalAttemptCount++;
        totalAttemptIndex = previewTotalAttemptCount;
        protocolAttemptIndex = IncrementProtocolAttemptCount(protocol);
        return true;
    }

    private int IncrementProtocolAttemptCount(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => ++previewWebRtcAttemptCount,
            SitePreviewProtocol.Flv => ++previewFlvAttemptCount,
            SitePreviewProtocol.Hls => ++previewHlsAttemptCount,
            _ => 1
        };
    }

    private int GetProtocolAttemptCount(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => previewWebRtcAttemptCount,
            SitePreviewProtocol.Flv => previewFlvAttemptCount,
            SitePreviewProtocol.Hls => previewHlsAttemptCount,
            _ => 0
        };
    }

    private void ResetPreviewAttemptState(string? deviceCode = null)
    {
        previewAttemptDeviceCode = string.IsNullOrWhiteSpace(deviceCode) ? null : deviceCode.Trim();
        previewTotalAttemptCount = 0;
        previewWebRtcAttemptCount = 0;
        previewFlvAttemptCount = 0;
        previewHlsAttemptCount = 0;
    }

    private PreviewRetryDecision DecidePreviewRetry(
        SitePreviewSession currentSession,
        SitePreviewProtocol failedProtocol,
        PreviewFailureCategory failureCategory)
    {
        if (isWindowClosing || failureCategory == PreviewFailureCategory.ProgramLifecycleKill)
        {
            return new PreviewRetryDecision(false, true, SitePreviewProtocol.Unknown, "program_lifecycle_kill");
        }

        var remainingSlots = MaxPreviewTotalAttempts - currentSession.TotalAttemptIndex;
        return failedProtocol switch
        {
            SitePreviewProtocol.WebRtc when failureCategory == PreviewFailureCategory.ConnectionNegotiation
                                            && currentSession.ProtocolAttemptIndex < MaxWebRtcAttempts
                                            && remainingSlots > 0
                => new PreviewRetryDecision(true, false, SitePreviewProtocol.WebRtc, "connection_negotiation_retry"),
            SitePreviewProtocol.WebRtc
                => new PreviewRetryDecision(false, false, SitePreviewProtocol.Flv, failureCategory == PreviewFailureCategory.NoFirstFrameBlackScreen
                    ? "no_first_frame_switch_to_flv"
                    : "switch_to_flv"),
            SitePreviewProtocol.Flv when currentSession.ProtocolAttemptIndex < MaxFlvAttempts
                                         && remainingSlots >= 2
                => new PreviewRetryDecision(true, false, SitePreviewProtocol.Flv, "short_flv_rebuild_retry"),
            SitePreviewProtocol.Flv when remainingSlots > 0
                => new PreviewRetryDecision(false, false, SitePreviewProtocol.Hls, "switch_to_hls"),
            SitePreviewProtocol.Hls when currentSession.ProtocolAttemptIndex >= MaxHlsAttempts
                => new PreviewRetryDecision(false, true, SitePreviewProtocol.Unknown, "hls_single_attempt_exhausted"),
            SitePreviewProtocol.Hls
                => new PreviewRetryDecision(false, true, SitePreviewProtocol.Unknown, "hls_failed"),
            _ => new PreviewRetryDecision(false, true, SitePreviewProtocol.Unknown, "preview_chain_exhausted")
        };
    }

    private static PreviewFailureCategory ClassifyPreviewFailure(
        SitePreviewProtocol protocol,
        string? category,
        string? reason)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category)
            ? string.Empty
            : category.Trim().ToLowerInvariant();
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : reason.Trim().ToLowerInvariant();

        if (normalizedCategory.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
            || normalizedReason.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
            || normalizedReason.Contains("window_closing", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewFailureCategory.ProgramLifecycleKill;
        }

        if (normalizedCategory is "webrtc_answer_failed"
            or "webrtc_ice_failed"
            or "webrtc_connection_failed"
            or "webrtc_offer_missing"
            or "host_start_failed")
        {
            return PreviewFailureCategory.ConnectionNegotiation;
        }

        if (protocol == SitePreviewProtocol.WebRtc
            && (normalizedCategory is "webrtc_no_first_frame"
                or "webrtc_black_screen_timeout"
                or "connected_without_first_frame"
                || normalizedReason.Contains("connected without first frame", StringComparison.OrdinalIgnoreCase)))
        {
            return PreviewFailureCategory.NoFirstFrameBlackScreen;
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory) && normalizedCategory.Contains("host", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewFailureCategory.HostInitialization;
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory) || !string.IsNullOrWhiteSpace(normalizedReason))
        {
            return PreviewFailureCategory.StreamRuntime;
        }

        return PreviewFailureCategory.ResolveUnavailable;
    }

    private static SitePreviewProtocol GetNextProtocol(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => SitePreviewProtocol.Flv,
            SitePreviewProtocol.Flv => SitePreviewProtocol.Hls,
            _ => SitePreviewProtocol.Unknown
        };
    }

    private static string SanitizeDiagnosticValue(string value)
    {
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " | ", StringComparison.Ordinal)
            .Replace(", ", "; ", StringComparison.Ordinal);
    }

    private void Notify(string title, string message)
    {
        NotificationRequested?.Invoke(this, new NotificationRequestedEventArgs(title, message));
    }

    private bool HasActiveOrPendingPreview()
    {
        return previewResolveCts is not null
            || previewSession is not null
            || PreviewSessionJson is not null;
    }

    private Task WriteDiagnosticAsync(string eventName, string message)
    {
        return diagnosticService.WriteAsync(eventName, message);
    }

    private static string ResolveDispatchFailureStage(Exception exception)
    {
        return exception.Data["dispatch-stage"] as string ?? "unknown";
    }

    private void ResetAcceptanceForcedPreviewFailure()
    {
        acceptanceForceNextWebRtcFailure = false;
        acceptanceForcedFailureCategory = null;
        acceptanceForcedFailureReason = null;
    }
}

internal enum PreviewPlaybackState
{
    Idle = 0,
    WebRtcUrlAcquired = 1,
    WebRtcHostInitializing = 2,
    WebRtcPlaybackReady = 3,
    WebRtcPlaybackFailed = 4,
    FallbackToFlv = 5,
    FlvPlaybackReady = 6,
    FallbackToHls = 7,
    HlsPlaybackReady = 8,
    Unavailable = 9
}

internal enum SecondaryEntryMode
{
    Unmapped = 0,
    Ignored = 1
}

internal enum PreviewFailureCategory
{
    None = 0,
    ResolveUnavailable = 1,
    ConnectionNegotiation = 2,
    NoFirstFrameBlackScreen = 3,
    HostInitialization = 4,
    StreamRuntime = 5,
    ProgramLifecycleKill = 6
}

internal sealed record PreviewRetryDecision(
    bool ShouldRetrySameProtocol,
    bool ShouldAbortChain,
    SitePreviewProtocol NextProtocol,
    string RetryReason);
