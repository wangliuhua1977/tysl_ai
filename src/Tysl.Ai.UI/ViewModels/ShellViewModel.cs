using System.Collections.ObjectModel;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;
using Tysl.Ai.Services.Dashboard;

namespace Tysl.Ai.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IReadOnlyList<MonitoringPoint> allPoints;
    private readonly Dictionary<Guid, MonitoringPoint> pointsById;
    private readonly DashboardSnapshot snapshot;
    private bool isFilterPanelExpanded = true;
    private string searchText = string.Empty;
    private string selectedFilter = "全部";
    private MonitoringPoint? selectedPoint;

    public ShellViewModel(IInspectionDashboardService dashboardService)
    {
        snapshot = dashboardService.GetSnapshot();
        allPoints = snapshot.Points;
        pointsById = allPoints.ToDictionary(point => point.Id);
        VisiblePoints = new ObservableCollection<MonitoringPoint>();
        Filters = new[] { "全部", "异常", "已监测", "已派单" };
        RecentAlerts = snapshot.Alerts;
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelExpanded = !IsFilterPanelExpanded);
        NoOpCommand = new RelayCommand(() => { });
        SelectAlertCommand = new RelayCommand<AlertDigest>(SelectAlert);
        RefreshVisiblePoints();
    }

    public int PointCount => snapshot.PointCount;

    public int OnlineCount => snapshot.OnlineCount;

    public int AlertCount => snapshot.AlertCount;

    public int DispatchedCount => snapshot.DispatchedCount;

    public string LastRefreshText => snapshot.LastRefreshedAt.ToLocalTime().ToString("HH:mm:ss");

    public IReadOnlyList<string> Filters { get; }

    public IReadOnlyList<AlertDigest> RecentAlerts { get; }

    public ObservableCollection<MonitoringPoint> VisiblePoints { get; }

    public RelayCommand ToggleFilterPanelCommand { get; }

    public RelayCommand NoOpCommand { get; }

    public RelayCommand<AlertDigest> SelectAlertCommand { get; }

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
                RefreshVisiblePoints();
            }
        }
    }

    public string SelectedFilter
    {
        get => selectedFilter;
        set
        {
            if (SetProperty(ref selectedFilter, value))
            {
                RefreshVisiblePoints();
            }
        }
    }

    public MonitoringPoint? SelectedPoint
    {
        get => selectedPoint;
        set => SetProperty(ref selectedPoint, value);
    }

    private void RefreshVisiblePoints()
    {
        var query = allPoints.Where(MatchesFilter);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(point =>
                point.Alias.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                point.DeviceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                point.RegionName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var points = query.ToList();
        VisiblePoints.Clear();

        foreach (var point in points)
        {
            VisiblePoints.Add(point);
        }

        if (SelectedPoint is null || points.All(point => point.Id != SelectedPoint.Id))
        {
            SelectedPoint = points.FirstOrDefault() ?? allPoints.FirstOrDefault();
        }
    }

    private bool MatchesFilter(MonitoringPoint point)
    {
        return SelectedFilter switch
        {
            "异常" => point.Status is PointStatus.Alert or PointStatus.Offline,
            "已监测" => point.Status is PointStatus.Monitoring or PointStatus.Normal,
            "已派单" => point.Status == PointStatus.Dispatched,
            _ => true
        };
    }

    private void SelectAlert(AlertDigest? alert)
    {
        if (alert is null)
        {
            return;
        }

        if (pointsById.TryGetValue(alert.PointId, out var point))
        {
            SelectedPoint = point;
        }
    }
}

public sealed class RelayCommand<T> : System.Windows.Input.ICommand
{
    private readonly Action<T?> execute;
    private readonly Func<T?, bool>? canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke((T?)parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        execute((T?)parameter);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
