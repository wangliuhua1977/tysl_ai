using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IDispatchRecordRepository
{
    Task<IReadOnlyList<DispatchRecord>> ListLatestAsync(CancellationToken cancellationToken = default);

    Task<DispatchRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<DispatchRecord?> GetLatestByDeviceAndFaultAsync(
        string deviceCode,
        string faultCode,
        CancellationToken cancellationToken = default);

    Task<DispatchRecord?> GetLatestUnrecoveredByDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<long> AddAsync(DispatchRecord record, CancellationToken cancellationToken = default);

    Task UpdateAsync(DispatchRecord record, CancellationToken cancellationToken = default);
}
