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

    private readonly SemaphoreSlim dashboardLoadLock = new(1, 1);
    private readonly IDispatchService dispatchService;
    private readonly DispatcherTimer refreshTimer;
    private readonly bool isMapHostConfigured;
    private readonly Dictionary<string, DemoCoordinate> renderedPointCoordinates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISiteLocalProfileService siteLocalProfileService;
    private readonly ISiteMapQueryService siteMapQueryService;
    private SiteEditorViewModel? activeEditor;
    private int dispatchedCount;
    private string dispatchOverviewDetail = "当前无派单中的点位。";
    private string dispatchOverviewText = "派单待命";
    private int faultCount;
    private bool hasVisiblePoints;
    private string inspectionStatusDetail = "当前无纳入静默巡检的点位。";
    private string inspectionStatusText = "巡检待机";
    private bool isCoordinatePickActive;
    private bool isDisposed;
    private bool isFilterPanelExpanded = true;
    private string mapEmptyStateText;
    private string mapHostStateJson = "{\"points\":[],\"coordinatePickActive\":false}";
    private string mapInteractionHint;
    private int monitoredCount;
    private int pointCount;
    private string platformStatusDetail = "正在准备平台设备源。";
    private string platformStatusText = "平台准备中";
    private string searchText = string.Empty;
    private SiteDetailViewModel? selectedDetail;
    private SiteMergedView? selectedDetailSource;
    private DashboardFilterOption selectedFilter;
    private SiteMapPointViewModel? selectedPoint;
    private string? selectedPointDeviceCode;

    public ShellViewModel(
        ISiteMapQueryService siteMapQueryService,
        ISiteLocalProfileService siteLocalProfileService,
        IDispatchService dispatchService,
        bool isMapHostConfigured)
    {
        this.siteMapQueryService = siteMapQueryService;
        this.siteLocalProfileService = siteLocalProfileService;
        this.dispatchService = dispatchService;
        this.isMapHostConfigured = isMapHostConfigured;

        mapEmptyStateText = isMapHostConfigured
            ? "正在等待平台点位。"
            : "地图未配置。请准备 amap-js.json 后重启。";
        mapInteractionHint = isMapHostConfigured
            ? "悬停查看摘要，单击联动详情。"
            : "地图未配置，暂不支持坐标补录。";

        Filters =
        [
            new DashboardFilterOption("全部", SiteDashboardFilter.All),
            new DashboardFilterOption("异常", SiteDashboardFilter.Fault),
            new DashboardFilterOption("已纳管", SiteDashboardFilter.Monitored),
            new DashboardFilterOption("处理中", SiteDashboardFilter.Dispatched)
        ];

        selectedFilter = Filters[0];
        VisiblePoints = [];
        VisibleAlerts = [];
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelExpanded = !IsFilterPanelExpanded);
        EditSelectedSiteCommand = new AsyncRelayCommand(OpenEditEditorAsync, () => SelectedDetail is not null);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, () => SelectedDetail is not null);
        ConfirmRecoveryCommand = new AsyncRelayCommand(ConfirmRecoveryAsync, () => SelectedDetail?.CanConfirmRecovery == true);
        SelectPointCommand = new RelayCommand<SiteMapPointViewModel>(SelectPoint);
        SelectAlertCommand = new RelayCommand<SiteAlertDigestViewModel>(SelectAlert);

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

    public string LastRefreshText { get; private set; } = "--:--:--";

    public IReadOnlyList<DashboardFilterOption> Filters { get; }

    public ObservableCollection<SiteMapPointViewModel> VisiblePoints { get; }

    public ObservableCollection<SiteAlertDigestViewModel> VisibleAlerts { get; }

    public RelayCommand ToggleFilterPanelCommand { get; }

    public AsyncRelayCommand EditSelectedSiteCommand { get; }

    public AsyncRelayCommand ToggleMonitoringCommand { get; }

    public AsyncRelayCommand ConfirmRecoveryCommand { get; }

    public RelayCommand<SiteMapPointViewModel> SelectPointCommand { get; }

    public RelayCommand<SiteAlertDigestViewModel> SelectAlertCommand { get; }

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
            ConfirmRecoveryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MonitorToggleText));
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

    public string MonitorToggleText => SelectedDetail?.IsMonitored == false ? "纳入巡检" : "暂停巡检";

    public void HandleMapClicked(double longitude, double latitude)
    {
        if (!IsCoordinatePickActive || activeEditor is null)
        {
            return;
        }

        activeEditor.ApplyPickedCoordinate(new DemoCoordinate
        {
            Longitude = Math.Round(longitude, 6),
            Latitude = Math.Round(latitude, 6)
        });

        ClearCoordinatePick();
    }

    public void HandleMapPointSelected(string deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return;
        }

        var point = VisiblePoints.FirstOrDefault(item => item.DeviceCode.Equals(deviceCode, StringComparison.OrdinalIgnoreCase));
        if (point is not null)
        {
            SelectedPoint = point;
            return;
        }

        selectedPointDeviceCode = deviceCode;
        RefreshMapHostState();
        _ = LoadSelectedSiteDetailAsync(deviceCode);
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
        dashboardLoadLock.Dispose();
    }

    private async void HandleRefreshTimerTick(object? sender, EventArgs e)
    {
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
            PointCount = snapshot.PointCount;
            MonitoredCount = snapshot.MonitoredCount;
            FaultCount = snapshot.FaultCount;
            DispatchedCount = snapshot.DispatchedCount;
            UpdateOverviewStatus(snapshot);

            LastRefreshText = snapshot.LastRefreshedAt.ToLocalTime().ToString("HH:mm:ss");
            OnPropertyChanged(nameof(LastRefreshText));

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

            HasVisiblePoints = VisiblePoints.Count > 0;
            MapEmptyStateText = ResolveMapEmptyStateText(isMapHostConfigured, snapshot.IsPlatformConnected, snapshot.PlatformStatusText);

            var targetDeviceCode = preferredSelectionDeviceCode ?? selectedPointDeviceCode;
            var targetPoint = !string.IsNullOrWhiteSpace(targetDeviceCode)
                ? VisiblePoints.FirstOrDefault(point => point.DeviceCode.Equals(targetDeviceCode, StringComparison.OrdinalIgnoreCase))
                : VisiblePoints.FirstOrDefault();

            if (targetPoint is not null)
            {
                SelectedPoint = targetPoint;
                return;
            }

            SelectedPoint = null;
            RefreshMapHostState();

            if (!string.IsNullOrWhiteSpace(targetDeviceCode))
            {
                selectedPointDeviceCode = targetDeviceCode;
                await LoadSelectedSiteDetailAsync(targetDeviceCode, notifyOnError);
                return;
            }

            selectedDetailSource = null;
            SelectedDetail = null;
        }
        catch
        {
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
        catch
        {
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

        OpenEditor(SelectedDetail.CreateEditorViewModel());
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
        catch
        {
            Notify("操作失败", "巡检状态更新失败，请稍后重试。");
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
        catch
        {
            Notify("恢复确认失败", "恢复状态更新失败，请稍后重试。");
        }
    }

    private void OpenEditor(SiteEditorViewModel editor)
    {
        if (activeEditor is not null)
        {
            activeEditor.RequestClose();
        }

        activeEditor = editor;
        editor.SaveRequested += HandleEditorSaveRequested;
        editor.CancelRequested += HandleEditorCancelRequested;
        editor.CoordinatePickRequested += HandleEditorCoordinatePickRequested;
        EditorDialogRequested?.Invoke(this, new SiteEditorDialogRequestedEventArgs(editor));
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

        selectedPointDeviceCode = alert.PointId;
        var point = VisiblePoints.FirstOrDefault(item => item.DeviceCode.Equals(alert.PointId, StringComparison.OrdinalIgnoreCase));
        if (point is not null)
        {
            SelectedPoint = point;
            return;
        }

        RefreshMapHostState();
        await LoadSelectedSiteDetailAsync(alert.PointId);
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
            await siteLocalProfileService.UpsertAsync(input!);
            editor.RequestClose();
            await LoadDashboardAsync(input!.DeviceCode, true);
        }
        catch
        {
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

        activeEditor = editor;
        IsCoordinatePickActive = true;
        MapInteractionHint = "坐标补录中，请在地图上点选位置。";
        editor.MarkCoordinatePickPending();
    }

    private void ClearCoordinatePick()
    {
        IsCoordinatePickActive = false;
        MapInteractionHint = isMapHostConfigured
            ? "悬停查看摘要，单击联动详情。"
            : "地图未配置，暂不支持坐标补录。";
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

    private static string ResolveMapEmptyStateText(
        bool isMapHostConfigured,
        bool isPlatformConnected,
        string platformStatusText)
    {
        if (!isMapHostConfigured)
        {
            return "地图未配置。请准备 amap-js.json 后重启。";
        }

        if (!isPlatformConnected)
        {
            return $"{platformStatusText}。请补充 ACIS 配置后重试。";
        }

        return "当前筛选条件下暂无可展示点位。";
    }

    private void Notify(string title, string message)
    {
        NotificationRequested?.Invoke(this, new NotificationRequestedEventArgs(title, message));
    }
}
