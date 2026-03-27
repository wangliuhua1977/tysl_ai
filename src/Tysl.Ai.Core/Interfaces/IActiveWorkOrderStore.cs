using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IActiveWorkOrderStore
{
    Task<IReadOnlyList<ActiveWorkOrder>> ListLatestAsync(CancellationToken cancellationToken = default);

    Task<ActiveWorkOrder?> GetByIdAsync(long workOrderId, CancellationToken cancellationToken = default);

    Task<ActiveWorkOrder?> GetLatestOpenByDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveWorkOrder>> ListByDeviceAsync(
        string deviceCode,
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<long> AddAsync(ActiveWorkOrder workOrder, CancellationToken cancellationToken = default);

    Task UpdateAsync(ActiveWorkOrder workOrder, CancellationToken cancellationToken = default);
}
