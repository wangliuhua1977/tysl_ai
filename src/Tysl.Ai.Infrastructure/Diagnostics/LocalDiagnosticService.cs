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

    public event EventHandler<LocalDiagnosticWrittenEventArgs>? Written;

    public string LogPath => logPath;

    public async Task WriteAsync(string eventName, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var occurredAt = DateTimeOffset.Now;
        var line = $"[{occurredAt:yyyy-MM-dd HH:mm:ss.fff}] [{eventName}] {message}{Environment.NewLine}";

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(logPath, line, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }

        Written?.Invoke(this, new LocalDiagnosticWrittenEventArgs(occurredAt, eventName, message));
    }

    public void Dispose()
    {
        writeLock.Dispose();
    }
}

public sealed class LocalDiagnosticWrittenEventArgs(
    DateTimeOffset occurredAt,
    string eventName,
    string message) : EventArgs
{
    public DateTimeOffset OccurredAt { get; } = occurredAt;

    public string EventName { get; } = eventName;

    public string Message { get; } = message;
}
