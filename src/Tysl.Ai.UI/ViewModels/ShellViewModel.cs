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
    private static readonly JsonSerializerOptions MapHostJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<MapStyleOption> BuiltInMapStyleOptions =
    [
        new("default", "默认地图"),
        new("amap://styles/darkblue", "深色科技风"),
        new("amap://styles/whitesmoke", "简洁浅色风"),
        new("amap://styles/grey", "灰阶监控风")
    ];

    private readonly SemaphoreSlim dashboardLoadLock = new(1, 1);
    private readonly IDispatchService dispatchService;
    private readonly ILocalDiagnosticService diagnosticService;
    private readonly IMapStylePreferenceStore mapStylePreferenceStore;
    private readonly DispatcherTimer refreshTimer;
    private readonly bool isMapHostConfigured;
    private DemoCoordinate? coordinatePickCandidate;
    private readonly Dictionary<string, DemoCoordinate> renderedPointCoordinates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISiteLocalProfileService siteLocalProfileService;
    private readonly ISiteMapQueryService siteMapQueryService;
    private SiteEditorViewModel? activeEditor;
    private int currentMapVisibleCount;
    private int dispatchedCount;
    private string dispatchOverviewDetail = "当前无派单中的点位。";
    private string dispatchOverviewText = "派单待命";
    private int faultCount;
    private bool hasIgnoredPoints;
    private bool hasUnmappedPoints;
    private bool hasVisiblePoints;
    private string inspectionStatusDetail = "当前无纳入静默巡检的点位。";
    private string inspectionStatusText = "巡检待机";
    private bool isCoordinatePickActive;
    private bool isDisposed;
    private bool isFilterPanelExpanded = true;
    private string mapCoverageDetailText = "“全部”包含全部点位；仅具备可用显示坐标的点位会落图。";
    private string mapEmptyStateText;
    private string mapHostStateJson = "{\"points\":[],\"candidateCoordinate\":null,\"coordinatePickActive\":false}";
    private string mapInteractionHint;
    private int mappedPointCount;
    private int monitoredCount;
    private int pointCount;
    private string platformStatusDetail = "正在准备平台设备源。";
    private string platformStatusText = "平台准备中";
    private string searchText = string.Empty;
    private SiteDetailViewModel? selectedDetail;
    private SiteMergedView? selectedDetailSource;
    private DashboardFilterOption selectedFilter;
    private string selectedMapStyleKey;
    private SiteMapPointViewModel? selectedPoint;
    private string? selectedPointDeviceCode;
    private int unmappedPointCount;

    public ShellViewModel(
        ISiteMapQueryService siteMapQueryService,
        ISiteLocalProfileService siteLocalProfileService,
        IDispatchService dispatchService,
        ILocalDiagnosticService diagnosticService,
        IMapStylePreferenceStore mapStylePreferenceStore,
        bool isMapHostConfigured,
        string? initialMapStyleKey)
    {
        this.siteMapQueryService = siteMapQueryService;
        this.siteLocalProfileService = siteLocalProfileService;
        this.dispatchService = dispatchService;
        this.diagnosticService = diagnosticService;
        this.mapStylePreferenceStore = mapStylePreferenceStore;
        this.isMapHostConfigured = isMapHostConfigured;
        selectedMapStyleKey = ResolveMapStyleKey(initialMapStyleKey);

        mapEmptyStateText = isMapHostConfigured
            ? "正在等待平台点位。"
            : "地图未配置。请准备 amap-js.json 后重启。";
        mapInteractionHint = ResolveDefaultMapInteractionHint();

        Filters =
        [
            new DashboardFilterOption("全部", SiteDashboardFilter.All),
            new DashboardFilterOption("异常", SiteDashboardFilter.Fault),
            new DashboardFilterOption("已纳管", SiteDashboardFilter.Monitored),
            new DashboardFilterOption("已处置", SiteDashboardFilter.Disposed),
            new DashboardFilterOption("未落图", SiteDashboardFilter.Unmapped),
            new DashboardFilterOption("已忽略", SiteDashboardFilter.Ignored)
        ];

        selectedFilter = Filters[0];
        MapStyleOptions = BuiltInMapStyleOptions;
        VisiblePoints = [];
        VisibleAlerts = [];
        UnmappedPoints = [];
        IgnoredPoints = [];
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelExpanded = !IsFilterPanelExpanded);
        EditSelectedSiteCommand = new AsyncRelayCommand(OpenEditEditorAsync, () => SelectedDetail is not null);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, () => SelectedDetail is not null);
        ToggleIgnoreCommand = new AsyncRelayCommand(ToggleIgnoreAsync, () => SelectedDetail is not null);
        ConfirmRecoveryCommand = new AsyncRelayCommand(ConfirmRecoveryAsync, () => SelectedDetail?.CanConfirmRecovery == true);
        SelectPointCommand = new RelayCommand<SiteMapPointViewModel>(SelectPoint);
        SelectAlertCommand = new RelayCommand<SiteAlertDigestViewModel>(SelectAlert);
        SelectUnmappedPointCommand = new RelayCommand<UnmappedPointDigestViewModel>(SelectUnmappedPoint);
        SelectIgnoredPointCommand = new RelayCommand<IgnoredPointDigestViewModel>(SelectIgnoredPoint);
        EditUnmappedPointCommand = new RelayCommand<UnmappedPointDigestViewModel>(EditUnmappedPoint);

        refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        refreshTimer.Tick += HandleRefreshTimerTick;
        refreshTimer.Start();

        _ = LoadDashboardAsync();
    }

    public event EventHandler<SiteEditorDialogRequestedEventArgs>? EditorDialogRequested;

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

    public int DispatchedCount
    {
        get => dispatchedCount;
        private set => SetProperty(ref dispatchedCount, value);
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

    public AsyncRelayCommand ToggleMonitoringCommand { get; }

    public AsyncRelayCommand ToggleIgnoreCommand { get; }

    public AsyncRelayCommand ConfirmRecoveryCommand { get; }

    public RelayCommand<SiteMapPointViewModel> SelectPointCommand { get; }

    public RelayCommand<SiteAlertDigestViewModel> SelectAlertCommand { get; }

    public RelayCommand<UnmappedPointDigestViewModel> SelectUnmappedPointCommand { get; }

    public RelayCommand<IgnoredPointDigestViewModel> SelectIgnoredPointCommand { get; }

    public RelayCommand<UnmappedPointDigestViewModel> EditUnmappedPointCommand { get; }

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
                OnPropertyChanged(nameof(IsIgnoredFilterSelected));
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
            ToggleMonitoringCommand.NotifyCanExecuteChanged();
            ToggleIgnoreCommand.NotifyCanExecuteChanged();
            ConfirmRecoveryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MonitorToggleText));
            OnPropertyChanged(nameof(IgnoreToggleText));
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

    public string MapCoverageText => $"当前地图显示 {CurrentMapVisibleCount} / 总点位 {PointCount}";

    public string MapCoverageDetailText
    {
        get => mapCoverageDetailText;
        private set => SetProperty(ref mapCoverageDetailText, value);
    }

    public string UnmappedSectionTitle => $"未落图治理 {UnmappedPointCount}";

    public string UnmappedSectionDetail => "点击条目联动详情，可直接进入编辑补充信息。";

    public bool IsIgnoredFilterSelected => SelectedFilter.Value == SiteDashboardFilter.Ignored;

    public string SecondarySectionTitle => IsIgnoredFilterSelected
        ? $"已忽略点位 {IgnoredPoints.Count}"
        : $"未落图治理 {UnmappedPointCount}";

    public string SecondarySectionDetail => IsIgnoredFilterSelected
        ? "已忽略点位已退出地图、巡检和派单主线，可在右侧详情中恢复关注。"
        : "点击条目联动详情，可直接进入编辑补充信息。";

    public string SecondarySectionEmptyText => IsIgnoredFilterSelected
        ? "当前无已忽略点位。"
        : "当前无未落图点位。";

    public bool HasSecondaryItems => IsIgnoredFilterSelected ? HasIgnoredPoints : HasUnmappedPoints;

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

        _ = SelectDeviceAsync(deviceCode.Trim());
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
            DispatchedCount = snapshot.DispatchedCount;
            CurrentMapVisibleCount = snapshot.CoverageSummary.CurrentVisiblePointCount;
            MapCoverageDetailText = BuildMapCoverageDetailText(snapshot.CoverageSummary, SelectedFilter.Value);
            UpdateOverviewStatus(snapshot);

            LastRefreshText = snapshot.LastRefreshedAt.ToLocalTime().ToString("HH:mm:ss");
            OnPropertyChanged(nameof(LastRefreshText));
            OnPropertyChanged(nameof(MapCoverageText));
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

            if (SelectedFilter.Value == SiteDashboardFilter.Ignored)
            {
                if (IgnoredPoints.FirstOrDefault() is { } firstIgnoredPoint)
                {
                    await SelectDeviceAsync(firstIgnoredPoint.DeviceCode, notifyOnError);
                    return;
                }
            }

            var firstVisiblePoint = VisiblePoints.FirstOrDefault();
            if (firstVisiblePoint is not null)
            {
                SelectedPoint = firstVisiblePoint;
                return;
            }

            if (UnmappedPoints.FirstOrDefault() is { } firstUnmappedPoint)
            {
                await SelectDeviceAsync(firstUnmappedPoint.DeviceCode, notifyOnError);
                return;
            }

            SelectedPoint = null;
            selectedPointDeviceCode = null;
            selectedDetailSource = null;
            SelectedDetail = null;
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
            return;
        }

        try
        {
            var detail = await siteMapQueryService.GetSiteDetailAsync(deviceCode);
            selectedDetailSource = detail;
            SelectedDetail = detail is null
                ? null
                : SiteDetailViewModel.FromSnapshot(detail, GetRenderedCoordinate(detail.DeviceCode));
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
            await dispatchService.ConfirmRecoveryAsync(dispatchRecordId);
            await LoadDashboardAsync(SelectedDetail.DeviceCode, true);
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

        await SelectDeviceAsync(point.DeviceCode);
    }

    private async void SelectIgnoredPoint(IgnoredPointDigestViewModel? point)
    {
        if (point is null)
        {
            return;
        }

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
        else if (snapshot.CoverageSummary.UnmappedPointCount > 0)
        {
            InspectionStatusText = "坐标治理中";
            InspectionStatusDetail = $"共 {snapshot.CoverageSummary.UnmappedPointCount} 个点位未落图，可通过本地补充信息继续治理。";
        }
        else
        {
            InspectionStatusText = "巡检运行";
            InspectionStatusDetail = $"已纳管 {snapshot.MonitoredCount} 个点位。";
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
            return "地图主视图只保留当前关注点位，并联动状态、异常和详情抽屉。";
        }

        return "地图主视图聚焦点位状态、异常联动和右侧详情，不在主视图强调落图治理统计。";
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

        if (coverageSummary.FilteredUnmappedPointCount > 0)
        {
            return "当前筛选下暂无可展示点位，可在下方未落图治理区继续补录坐标。";
        }

        return "当前筛选条件下暂无可展示点位。";
    }

    private string ResolveDefaultMapInteractionHint()
    {
        return isMapHostConfigured
            ? "点位默认只显示小图标和单行名称，单击联动右侧详情。"
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
            return "default";
        }

        var normalized = value.Trim();
        return string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "native", StringComparison.OrdinalIgnoreCase)
            ? "default"
            : normalized;
    }

    private void Notify(string title, string message)
    {
        NotificationRequested?.Invoke(this, new NotificationRequestedEventArgs(title, message));
    }

    private Task WriteDiagnosticAsync(string eventName, string message)
    {
        return diagnosticService.WriteAsync(eventName, message);
    }
}
