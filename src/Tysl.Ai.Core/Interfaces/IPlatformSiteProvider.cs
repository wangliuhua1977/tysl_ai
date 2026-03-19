using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IPlatformSiteProvider
{
    Task<IReadOnlyList<PlatformSiteSnapshot>> ListAsync(CancellationToken cancellationToken = default);
}
