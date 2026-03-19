using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteMapQueryService
{
    Task<SiteDashboardSnapshot> GetDashboardAsync(
        SiteDashboardFilter filter,
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<SiteMergedView?> GetSiteDetailAsync(string deviceCode, CancellationToken cancellationToken = default);
}
