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
    private int faultCount;
    private bool hasVisiblePoints;
    private bool isCoordinatePickActive;
    private bool isDisposed;
    private bool isFilterPanelExpanded = true;
    private string mapEmptyStateText;
    private string mapHostStateJson = "{\"points\":[],\"coordinatePickActive\":false}";
    private string mapInteractionHint;
    private int monitoredCount;
    private int pointCount;
    private string platformStatusDetail = "正在准备平台设备源。";
    private string platformStatusText = "平台连接准备中";
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
            : "地图未配置。请补充 amap-js.json 后重启。";
        mapInteractionHint = isMapHostConfigured
            ? "地图只负责展示与联动，坐标拾取仅用于补录本地手工坐标。"
            : "地图宿主未配置，暂不可拾取手工坐标。";

        Filters =
        [
            new DashboardFilterOption("全部", SiteDashboardFilter.All),
            new DashboardFilterOption("异常", SiteDashboardFilter.Fault),
            new DashboardFilterOption("已监测", SiteDashboardFilter.Monitored),
            new DashboardFilterOption("已派单", SiteDashboardFilter.Dispatched)
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

    public string MonitorToggleText => SelectedDetail?.IsMonitored == false ? "纳入监测" : "暂停监测";

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
            Notify("操作失败", "监测状态更新失败，请稍后重试。");
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
            Notify("地图未配置", "请先补充 amap-js.json，再使用地图拾取手工坐标。");
            return;
        }

        activeEditor = editor;
        IsCoordinatePickActive = true;
        MapInteractionHint = "手工坐标补录中，请点击地图回填经纬度。";
        editor.MarkCoordinatePickPending();
    }

    private void ClearCoordinatePick()
    {
        IsCoordinatePickActive = false;
        MapInteractionHint = isMapHostConfigured
            ? "地图只负责展示与联动，坐标拾取仅用于补录本地手工坐标。"
            : "地图宿主未配置，暂不可拾取手工坐标。";
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

    private static string ResolveMapEmptyStateText(
        bool isMapHostConfigured,
        bool isPlatformConnected,
        string platformStatusText)
    {
        if (!isMapHostConfigured)
        {
            return "地图未配置。请补充 amap-js.json 后重启。";
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
