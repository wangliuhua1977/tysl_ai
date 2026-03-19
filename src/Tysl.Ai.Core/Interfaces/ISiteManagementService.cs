using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteManagementService
{
    Task<SiteProfile> CreateAsync(SiteProfileInput input, CancellationToken cancellationToken = default);

    Task<SiteProfile> UpdateAsync(SiteProfileInput input, CancellationToken cancellationToken = default);

    Task<SiteProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteProfile>> ListAsync(CancellationToken cancellationToken = default);
}
