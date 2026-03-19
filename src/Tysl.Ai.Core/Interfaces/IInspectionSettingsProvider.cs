using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IInspectionSettingsProvider
{
    Task<InspectionSettings> GetAsync(CancellationToken cancellationToken = default);
}
