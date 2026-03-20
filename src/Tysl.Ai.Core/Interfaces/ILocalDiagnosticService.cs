namespace Tysl.Ai.Core.Interfaces;

public interface ILocalDiagnosticService
{
    Task WriteAsync(string eventName, string message, CancellationToken cancellationToken = default);
}
