using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISiteLocalProfileService
{
    Task<SiteLocalProfile> UpsertAsync(SiteLocalProfileInput input, CancellationToken cancellationToken = default);

    Task<SiteLocalProfile?> GetByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default);

    Task<SiteLocalProfile> IgnoreAsync(
        string deviceCode,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<SiteLocalProfile?> RestoreAsync(string deviceCode, CancellationToken cancellationToken = default);
}
