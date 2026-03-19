using System.Collections.ObjectModel;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly ISiteManagementService siteManagementService;
    private readonly ISiteMapQueryService siteMapQueryService;
    private SiteEditorViewModel? activeEditor;
    private int dispatchedCount;
    private int faultCount;
    private bool isCoordinatePickActive;
    private bool isFilterPanelExpanded = true;
    private string mapInteractionHint = "地图点位由本地 SQLite 驱动。";
    private int monitoredCount;
    private int pointCount;
    private string searchText = string.Empty;
    private DashboardFilterOption selectedFilter;
    private SiteDetailViewModel? selectedDetail;
    private SiteMapPointViewModel? selectedPoint;

    public ShellViewModel(ISiteMapQueryService siteMapQueryService, ISiteManagementService siteManagementService)
    {
        this.siteMapQueryService = siteMapQueryService;
        this.siteManagementService = siteManagementService;
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
        CreateSiteCommand = new AsyncRelayCommand(OpenCreateEditorAsync);
        EditSelectedSiteCommand = new AsyncRelayCommand(OpenEditEditorAsync, () => SelectedPoint is not null);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync, () => SelectedPoint is not null);
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

    public AsyncRelayCommand CreateSiteCommand { get; }

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
                _ = LoadDashboardAsync(SelectedPoint?.Id);
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
                _ = LoadDashboardAsync(SelectedPoint?.Id);
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

            EditSelectedSiteCommand.NotifyCanExecuteChanged();
            ToggleMonitoringCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MonitorToggleText));
            _ = LoadSelectedSiteDetailAsync(value?.Id);
        }
    }

    public SiteDetailViewModel? SelectedDetail
    {
        get => selectedDetail;
        private set => SetProperty(ref selectedDetail, value);
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

    public string MonitorToggleText => SelectedDetail?.IsMonitored == false ? "纳入监测" : "停用监测";

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

    private async Task LoadDashboardAsync(Guid? preferredSelectionId = null)
    {
        try
        {
            var snapshot = await siteMapQueryService.GetDashboardAsync(SelectedFilter.Value, SearchText);

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

            var targetPoint = preferredSelectionId.HasValue
                ? VisiblePoints.FirstOrDefault(point => point.Id == preferredSelectionId.Value)
                : null;

            SelectedPoint = targetPoint ?? VisiblePoints.FirstOrDefault();
            if (SelectedPoint is null)
            {
                SelectedDetail = null;
            }
        }
        catch (Exception ex)
        {
            Notify("加载失败", ex.Message);
        }
    }

    private async Task LoadSelectedSiteDetailAsync(Guid? siteId)
    {
        if (!siteId.HasValue)
        {
            SelectedDetail = null;
            return;
        }

        try
        {
            var detail = await siteMapQueryService.GetSiteDetailAsync(siteId.Value);
            SelectedDetail = detail is null ? null : SiteDetailViewModel.FromSnapshot(detail);
            OnPropertyChanged(nameof(MonitorToggleText));
        }
        catch (Exception ex)
        {
            Notify("详情加载失败", ex.Message);
        }
    }

    private Task OpenCreateEditorAsync()
    {
        OpenEditor(SiteEditorViewModel.CreateForNew());
        return Task.CompletedTask;
    }

    private async Task OpenEditEditorAsync()
    {
        if (SelectedPoint is null)
        {
            return;
        }

        try
        {
            var site = await siteManagementService.GetByIdAsync(SelectedPoint.Id);
            if (site is null)
            {
                Notify("编辑失败", "未找到当前点位。");
                return;
            }

            OpenEditor(SiteEditorViewModel.CreateForEdit(site));
        }
        catch (Exception ex)
        {
            Notify("编辑失败", ex.Message);
        }
    }

    private async Task ToggleMonitoringAsync()
    {
        if (SelectedPoint is null)
        {
            return;
        }

        try
        {
            var site = await siteManagementService.GetByIdAsync(SelectedPoint.Id);
            if (site is null)
            {
                Notify("操作失败", "未找到当前点位。");
                return;
            }

            var updated = await siteManagementService.UpdateAsync(new SiteProfileInput
            {
                Id = site.Id,
                DeviceCode = site.DeviceCode,
                DeviceName = site.DeviceName,
                Alias = site.Alias,
                Remark = site.Remark,
                IsMonitored = !site.IsMonitored,
                Longitude = site.Longitude,
                Latitude = site.Latitude,
                AddressText = site.AddressText,
                ProductAccessNumber = site.ProductAccessNumber,
                MaintenanceUnit = site.MaintenanceUnit,
                MaintainerName = site.MaintainerName,
                MaintainerPhone = site.MaintainerPhone,
                DemoStatus = site.DemoStatus,
                DemoDispatchStatus = site.DemoDispatchStatus
            });

            await LoadDashboardAsync(updated.Id);
        }
        catch (Exception ex)
        {
            Notify("操作失败", ex.Message);
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

    private void SelectAlert(SiteAlertDigestViewModel? alert)
    {
        if (alert is null)
        {
            return;
        }

        var point = VisiblePoints.FirstOrDefault(item => item.Id == alert.PointId);
        if (point is not null)
        {
            SelectedPoint = point;
        }
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
            SiteProfile savedSite = editor.IsEditMode
                ? await siteManagementService.UpdateAsync(input!)
                : await siteManagementService.CreateAsync(input!);

            editor.RequestClose();
            await LoadDashboardAsync(savedSite.Id);
        }
        catch (Exception ex)
        {
            Notify("保存失败", ex.Message);
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
        MapInteractionHint = "演示坐标拾取中，点击地图占位区域回填经纬度。";
        editor.MarkCoordinatePickPending();
    }

    private void ClearCoordinatePick()
    {
        IsCoordinatePickActive = false;
        MapInteractionHint = "地图点位由本地 SQLite 驱动。";
    }

    private void Notify(string title, string message)
    {
        NotificationRequested?.Invoke(this, new NotificationRequestedEventArgs(title, message));
    }
}
