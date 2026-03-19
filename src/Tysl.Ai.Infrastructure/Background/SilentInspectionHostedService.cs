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

    public async Task StopAsync()
    {
        if (shutdownSource is null || loopTask is null)
        {
            return;
        }

        shutdownSource.Cancel();

        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            shutdownSource.Dispose();
            shutdownSource = null;
            loopTask = null;
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
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
