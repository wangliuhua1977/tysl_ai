using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IDispatchService
{
    Task ProcessInspectionResultAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken = default);

    Task ManualDispatchAsync(string deviceCode, CancellationToken cancellationToken = default);

    Task ConfirmRecoveryAsync(long dispatchRecordId, CancellationToken cancellationToken = default);
}
