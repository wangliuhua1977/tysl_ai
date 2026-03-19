using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Integrations.Acis;

public sealed class StubPlatformSiteProvider : IPlatformSiteProvider
{
    private static readonly IReadOnlyList<PlatformSiteSnapshot> Snapshots =
    [
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-001",
            DeviceName = "环城北路隧道东口枪机",
            RawLongitude = 120.5958,
            RawLatitude = 30.0125,
            RawCoordinateType = "gcj02",
            DemoOnlineState = DemoOnlineState.Online,
            DemoStatus = PointDemoStatus.Fault,
            DemoDispatchStatus = DispatchDemoStatus.None
        },
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-002",
            DeviceName = "高铁站前广场全景枪机",
            RawLongitude = 120.6462,
            RawLatitude = 30.0442,
            RawCoordinateType = "gcj02",
            DemoOnlineState = DemoOnlineState.Online,
            DemoStatus = PointDemoStatus.Warning,
            DemoDispatchStatus = DispatchDemoStatus.Dispatched
        },
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-003",
            DeviceName = "滨江公园南栈桥水岸枪机",
            RawLongitude = 120.6211,
            RawLatitude = 29.9852,
            RawCoordinateType = "gcj02",
            DemoOnlineState = DemoOnlineState.Online,
            DemoStatus = PointDemoStatus.Warning,
            DemoDispatchStatus = DispatchDemoStatus.Cooling
        },
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-004",
            DeviceName = "区政务中心东门门岗枪机",
            RawLongitude = 120.5739,
            RawLatitude = 30.0288,
            RawCoordinateType = "gcj02",
            DemoOnlineState = DemoOnlineState.Online,
            DemoStatus = PointDemoStatus.Normal,
            DemoDispatchStatus = DispatchDemoStatus.None
        },
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-005",
            DeviceName = "江心泵站周界球机",
            RawLongitude = 120.5534,
            RawLatitude = 29.9981,
            RawCoordinateType = "gcj02",
            DemoOnlineState = DemoOnlineState.Offline,
            DemoStatus = PointDemoStatus.Normal,
            DemoDispatchStatus = DispatchDemoStatus.None
        },
        new PlatformSiteSnapshot
        {
            DeviceCode = "ACIS-DEMO-006",
            DeviceName = "科创园一期西门半球",
            RawLongitude = null,
            RawLatitude = null,
            RawCoordinateType = "unknown",
            DemoOnlineState = DemoOnlineState.Online,
            DemoStatus = PointDemoStatus.Idle,
            DemoDispatchStatus = DispatchDemoStatus.None
        }
    ];

    public Task<IReadOnlyList<PlatformSiteSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Snapshots);
    }
}
