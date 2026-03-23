using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Background;

public sealed class SilentInspectionHostedService : IDisposable
{
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

    public void RequestStop()
    {
        try
        {
            shutdownSource?.Cancel();
        }
        catch
        {
            // Best effort only.
        }
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

        try
        {
            source.Cancel();
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        RequestStop();
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

                await Task.Delay(ResolveDelay(settings), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
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
