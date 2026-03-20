using System.Text;
using Tysl.Ai.Core.Interfaces;

namespace Tysl.Ai.Infrastructure.Diagnostics;

public sealed class LocalDiagnosticService : ILocalDiagnosticService, IDisposable
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string logPath;

    public LocalDiagnosticService(string directoryPath, string fileName = "app-diagnostics.log")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Directory.CreateDirectory(directoryPath);
        logPath = Path.Combine(directoryPath, fileName);
    }

    public async Task WriteAsync(string eventName, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{eventName}] {message}{Environment.NewLine}";

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(logPath, line, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public void Dispose()
    {
        writeLock.Dispose();
    }
}
