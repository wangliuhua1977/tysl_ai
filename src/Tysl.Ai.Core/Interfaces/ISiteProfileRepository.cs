using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteProfileRepository
{
    Task<IReadOnlyList<SiteProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<SiteProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task CreateAsync(SiteProfile siteProfile, CancellationToken cancellationToken = default);

    Task UpdateAsync(SiteProfile siteProfile, CancellationToken cancellationToken = default);
}
