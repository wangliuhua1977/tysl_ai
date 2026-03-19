using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteRuntimeStateRepository
{
    Task<IReadOnlyList<SiteRuntimeState>> ListAsync(CancellationToken cancellationToken = default);

    Task<SiteRuntimeState?> GetByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default);

    Task UpsertAsync(SiteRuntimeState state, CancellationToken cancellationToken = default);
}
