using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Dashboard;

public sealed class StubInspectionDashboardService : IInspectionDashboardService
{
    public DashboardSnapshot GetSnapshot()
    {
        var points = new List<MonitoringPoint>
        {
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Alias = "环城北路隧道",
                DeviceName = "A-01 路口球机",
                RegionName = "越城北片区",
                MaintenanceUnit = "绍兴北区值守组",
                Maintainer = "周工",
                ScreenshotHint = "截图占位：夜间车流画面",
                MaintenanceNote = "待复检后确认是否恢复。",
                Status = PointStatus.Alert,
                IsOnline = true
            },
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Alias = "高铁站前广场",
                DeviceName = "B-03 广场全景枪机",
                RegionName = "高铁枢纽片区",
                MaintenanceUnit = "客运枢纽维护组",
                Maintainer = "沈工",
                ScreenshotHint = "截图占位：晨间人流区域",
                MaintenanceNote = "已进入派单池，等待现场确认。",
                Status = PointStatus.Dispatched,
                IsOnline = true
            },
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Alias = "滨江公园南栈桥",
                DeviceName = "C-07 水岸枪机",
                RegionName = "滨江景观带",
                MaintenanceUnit = "滨江巡检组",
                Maintainer = "何工",
                ScreenshotHint = "截图占位：岸线视角",
                MaintenanceNote = "疑似离线，待 ACIS 接入后补真实状态链。",
                Status = PointStatus.Offline,
                IsOnline = false
            },
            new()
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Alias = "区政务中心东门",
                DeviceName = "D-05 门岗枪机",
                RegionName = "政务中心片区",
                MaintenanceUnit = "政务中心值守组",
                Maintainer = "李工",
                ScreenshotHint = "截图占位：门岗通道",
                MaintenanceNote = "当前为稳定点位，占位展示维护信息。",
                Status = PointStatus.Normal,
                IsOnline = true
            },
            new()
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Alias = "江心洲泵站",
                DeviceName = "E-09 周界球机",
                RegionName = "江心洲片区",
                MaintenanceUnit = "水务保障组",
                Maintainer = "徐工",
                ScreenshotHint = "截图占位：泵站外场",
                MaintenanceNote = "处于本轮监测队列，用于表现已监测状态。",
                Status = PointStatus.Monitoring,
                IsOnline = true
            },
            new()
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Alias = "科创园一期西门",
                DeviceName = "F-12 园区半球",
                RegionName = "科创园片区",
                MaintenanceUnit = "园区联保组",
                Maintainer = "顾工",
                ScreenshotHint = "截图占位：园区门区",
                MaintenanceNote = "当前正常，保留为地图区占位卡片。",
                Status = PointStatus.Normal,
                IsOnline = true
            }
        };

        var alerts = new List<AlertDigest>
        {
            new()
            {
                PointId = points[0].Id,
                PointAlias = points[0].Alias,
                IssueLabel = "画面异常",
                OccurredAtText = "09:14"
            },
            new()
            {
                PointId = points[1].Id,
                PointAlias = points[1].Alias,
                IssueLabel = "已派单待到场",
                OccurredAtText = "09:02"
            },
            new()
            {
                PointId = points[2].Id,
                PointAlias = points[2].Alias,
                IssueLabel = "设备离线",
                OccurredAtText = "08:47"
            }
        };

        return new DashboardSnapshot
        {
            PointCount = points.Count,
            OnlineCount = points.Count(point => point.IsOnline),
            AlertCount = points.Count(point => point.Status is PointStatus.Alert or PointStatus.Offline),
            DispatchedCount = points.Count(point => point.Status == PointStatus.Dispatched),
            LastRefreshedAt = DateTimeOffset.Now,
            Points = points,
            Alerts = alerts
        };
    }
}
