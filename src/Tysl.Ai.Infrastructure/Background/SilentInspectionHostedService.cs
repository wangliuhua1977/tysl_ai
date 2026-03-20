using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Background;

public sealed class SilentInspectionHostedService : IDisposable
{
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IInspectionSettingsProvider inspectionSettingsProvider;
    private readonly ISilentInspectionService silentInspectionService;
    private CancellationTokenSource? shutdownSource;
    private Task? loopTask;

    public SilentInspectionHostedService(
        ISilentInspectionService silentInspectionService,
        IInspectionSettingsProvider inspectionSettingsProvider)
    {
        this.silentInspectionService = silentInspectionService;
        this.inspectionSettingsProvider = inspectionSettingsProvider;
    }

    public void Start()
    {
        if (loopTask is not null)
        {
            return;
        }

        shutdownSource = new CancellationTokenSource();
        loopTask = Task.Run(() => RunLoopAsync(shutdownSource.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var source = Interlocked.Exchange(ref shutdownSource, null);
        var loop = Interlocked.Exchange(ref loopTask, null);

        if (source is null || loop is null)
        {
            source?.Dispose();
            return;
        }

        source.Cancel();

        try
        {
            await loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            source.Dispose();
        }
    }

    public void Dispose()
    {
        using var shutdownTimeout = new CancellationTokenSource(ShutdownWaitTimeout);

        try
        {
            StopAsync(shutdownTimeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Best effort shutdown. App exit should not wait forever.
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var settings = await inspectionSettingsProvider.GetAsync(cancellationToken);
                if (settings.Enabled && settings.IsWithinWindow(DateTimeOffset.Now))
                {
                    await silentInspectionService.RunCycleAsync(cancellationToken);
                }

                await Task.Delay(ResolveDelay(settings), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }
    }

    private static TimeSpan ResolveDelay(InspectionSettings settings)
    {
        if (!settings.Enabled)
        {
            return TimeSpan.FromMinutes(1);
        }

        return settings.IsWithinWindow(DateTimeOffset.Now)
            ? TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes))
            : TimeSpan.FromMinutes(1);
    }
}
