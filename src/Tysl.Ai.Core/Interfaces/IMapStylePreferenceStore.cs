namespace Tysl.Ai.Core.Interfaces;

public interface IMapStylePreferenceStore
{
    Task SaveMapStyleAsync(string? mapStyle, CancellationToken cancellationToken = default);
}
