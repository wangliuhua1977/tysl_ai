using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IDispatchPolicyProvider
{
    Task<DispatchPolicy> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(DispatchPolicy policy, CancellationToken cancellationToken = default);
}
