using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteLocalProfileRepository
{
    Task<IReadOnlyList<SiteLocalProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<SiteLocalProfile?> GetByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default);

    Task UpsertAsync(SiteLocalProfile profile, CancellationToken cancellationToken = default);
}
