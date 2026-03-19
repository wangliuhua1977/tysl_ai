namespace Tysl.Ai.Core.Interfaces;

public interface ISilentInspectionService
{
    Task RunCycleAsync(CancellationToken cancellationToken = default);
}
