using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IDispatchService
{
    Task ProcessInspectionResultAsync(
        PlatformSiteSnapshot platformSite,
        SiteLocalProfile? localProfile,
        SiteRuntimeState currentState,
        CancellationToken cancellationToken = default);

    Task<ManualDispatchPreparation> PrepareManualDispatchAsync(
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task ManualDispatchAsync(
        ManualDispatchRequest request,
        CancellationToken cancellationToken = default);

    Task<CloseWorkOrderPreparation> PrepareCloseWorkOrderAsync(
        long workOrderId,
        CancellationToken cancellationToken = default);

    Task CloseWorkOrderAsync(
        CloseWorkOrderRequest request,
        CancellationToken cancellationToken = default);
}
