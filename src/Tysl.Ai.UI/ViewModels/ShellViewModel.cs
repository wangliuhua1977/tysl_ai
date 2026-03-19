using System.Collections.ObjectModel;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;

namespace Tysl.Ai.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly ISiteLocalProfileService siteLocalProfileService;
    private readonly ISiteMapQueryService siteMapQueryService;
    private SiteEditorViewModel? activeEditor;
    private int dispatchedCount;
    private int faultCount;
    private bool hasVisiblePoints;
    private bool isCoordinatePickActive;
    private bool isFilterPanelExpanded = true;
    private string mapEmptyStateText = "正在等待平台点位。";
    private string mapInteractionHint = "地图点位由平台权威源驱动，坐标拾取仅用于补录本地手工坐标。";
    private int monitoredCount;
    private int pointCount;
    private string platformStatusDetail = "正在准备平台设备源。";
    private string platformStatusText = "平台连接准备中";
    private string searchText = string.Empty;
    private DashboardFilterOption selectedFilter;
    private SiteDetailViewModel? selectedDetail;
    private SiteMapPointViewModel? selectedPoint;
    private string? selectedPointDeviceCode;

    public ShellViewModel(ISiteMapQueryService siteMapQueryService, ISiteLocalProfileService siteLocalProfileService)
    {
        this.siteMapQueryService = siteMapQueryService;
        this.siteLocalProfileService = siteLocalProfileService;

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
        SelectPointCommand = new RelayCommand<SiteMapPointViewModel>(SelectPoint);
        SelectAlertCommand = new RelayCommand<SiteAlertDigestViewModel>(SelectAlert);

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
            OnPropertyChanged(nameof(MonitorToggleText));
        }
    }

    public bool IsCoordinatePickActive
    {
        get => isCoordinatePickActive;
        private set => SetProperty(ref isCoordinatePickActive, value);
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

    public string MonitorToggleText => SelectedDetail?.IsMonitored == false ? "纳入监测" : "暂停监测";

    public void HandleMapSurfaceClick(double relativeX, double relativeY)
    {
        if (!IsCoordinatePickActive || activeEditor is null)
        {
            return;
        }

        activeEditor.ApplyPickedCoordinate(siteMapQueryService.CreateDemoCoordinate(relativeX, relativeY));
        ClearCoordinatePick();
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

    private async Task LoadDashboardAsync(string? preferredSelectionDeviceCode = null)
    {
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
            MapEmptyStateText = ResolveMapEmptyStateText(snapshot.IsPlatformConnected, snapshot.PlatformStatusText);

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

            if (!string.IsNullOrWhiteSpace(targetDeviceCode))
            {
                selectedPointDeviceCode = targetDeviceCode;
                await LoadSelectedSiteDetailAsync(targetDeviceCode);
                return;
            }

            SelectedDetail = null;
        }
        catch
        {
            Notify("加载失败", "地图数据暂不可用，请稍后重试。");
        }
    }

    private async Task LoadSelectedSiteDetailAsync(string? deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            SelectedDetail = null;
            return;
        }

        try
        {
            var detail = await siteMapQueryService.GetSiteDetailAsync(deviceCode);
            SelectedDetail = detail is null ? null : SiteDetailViewModel.FromSnapshot(detail);
        }
        catch
        {
            Notify("详情加载失败", "点位详情暂不可用，请稍后重试。");
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
            await LoadDashboardAsync(SelectedDetail.DeviceCode);
        }
        catch
        {
            Notify("操作失败", "监测状态更新失败，请稍后重试。");
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
            await LoadDashboardAsync(input!.DeviceCode);
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

        activeEditor = editor;
        IsCoordinatePickActive = true;
        MapInteractionHint = "手工坐标补录中，请点击地图占位区回填经纬度。";
        editor.MarkCoordinatePickPending();
    }

    private void ClearCoordinatePick()
    {
        IsCoordinatePickActive = false;
        MapInteractionHint = "地图点位由平台权威源驱动，坐标拾取仅用于补录本地手工坐标。";
    }

    private static string ResolveMapEmptyStateText(bool isPlatformConnected, string platformStatusText)
    {
        if (!isPlatformConnected)
        {
            return $"{platformStatusText}。请补充 ACIS 配置后重新刷新。";
        }

        return "当前筛选条件下暂无可展示点位。";
    }

    private void Notify(string title, string message)
    {
        NotificationRequested?.Invoke(this, new NotificationRequestedEventArgs(title, message));
    }
}
