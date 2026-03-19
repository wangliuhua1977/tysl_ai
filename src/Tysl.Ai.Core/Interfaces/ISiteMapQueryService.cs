using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteMapQueryService
{
    Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<SiteDetailSnapshot?> GetSiteDetailAsync(Guid siteId, CancellationToken cancellationToken = default);

    DemoCoordinate CreateDemoCoordinate(double relativeX, double relativeY);
}
