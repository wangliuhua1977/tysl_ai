using System.Globalization;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Infrastructure.Diagnostics;
using Tysl.Ai.Infrastructure.Integrations.Acis;
using Tysl.Ai.UI.ViewModels;
using Tysl.Ai.UI.Views;

namespace Tysl.Ai.App.Acceptance;

internal sealed class PreviewAcceptanceRunner : IDisposable
{
    private static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly object syncRoot = new();
    private readonly List<AcceptanceDiagnosticEvent> diagnosticEvents = [];
    private readonly LocalDiagnosticService diagnosticService;
    private readonly AcisKernelPlatformSiteProvider platformSiteProvider;
    private readonly ShellViewModel shellViewModel;
    private readonly ShellWindow shellWindow;
    private bool disposed;

    public PreviewAcceptanceRunner(
        ShellWindow shellWindow,
        ShellViewModel shellViewModel,
        AcisKernelPlatformSiteProvider platformSiteProvider,
        LocalDiagnosticService diagnosticService)
    {
        this.shellWindow = shellWindow;
        this.shellViewModel = shellViewModel;
        this.platformSiteProvider = platformSiteProvider;
        this.diagnosticService = diagnosticService;
        diagnosticService.Written += HandleDiagnosticWritten;
    }

    public void Start()
    {
        _ = RunAsync();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        diagnosticService.Written -= HandleDiagnosticWritten;
    }

    private async Task RunAsync()
    {
        try
        {
            await WriteRunnerEventAsync("preview-acceptance-start", "mode=v2");
            await WaitForConditionAsync(
                () => shellWindow.IsLoaded && shellWindow.IsVisible,
                StartupTimeout);

            var devices = (await platformSiteProvider.ListAsync().ConfigureAwait(false))
                .Select(site => site.DeviceCode)
                .Where(deviceCode => !string.IsNullOrWhiteSpace(deviceCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await WriteRunnerEventAsync(
                "preview-acceptance-device-list",
                $"count={devices.Length}, devices={string.Join("|", devices)}");

            var results = new List<PreviewAttemptResult>(devices.Length);
            foreach (var deviceCode in devices)
            {
                results.Add(await ExercisePreviewAsync(deviceCode, closeAfter: true).ConfigureAwait(false));
                await Task.Delay(350).ConfigureAwait(false);
            }

            var directSuccess = results.FirstOrDefault(result => result.IsSuccess && result.FinalProtocol == SitePreviewProtocol.WebRtc);
            var fallbackSuccess = results.FirstOrDefault(result => result.IsSuccess && result.FinalProtocol is SitePreviewProtocol.Flv or SitePreviewProtocol.Hls);
            if (fallbackSuccess is null)
            {
                fallbackSuccess = await TryAcquireFallbackSampleAsync(results).ConfigureAwait(false);
            }

            await WriteWebRtcSuccessMetricsAsync(results).ConfigureAwait(false);
            await WriteFallbackSampleAsync(results, fallbackSuccess).ConfigureAwait(false);
            var alternate = results.FirstOrDefault(result =>
                !string.Equals(result.DeviceCode, directSuccess?.DeviceCode, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(result.DeviceCode, fallbackSuccess?.DeviceCode, StringComparison.OrdinalIgnoreCase));

            await WriteRunnerEventAsync(
                "preview-acceptance-summary",
                $"tested={results.Count}, directWebRtc={results.Count(result => result.IsSuccess && result.FinalProtocol == SitePreviewProtocol.WebRtc)}, fallbackSuccess={results.Count(result => result.IsSuccess && result.FinalProtocol is SitePreviewProtocol.Flv or SitePreviewProtocol.Hls) + (fallbackSuccess is not null && results.All(result => !string.Equals(result.LastSessionId, fallbackSuccess.LastSessionId, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)}, totalFailed={results.Count(result => !result.IsSuccess)}");

            var repeatDevice = directSuccess?.DeviceCode ?? fallbackSuccess?.DeviceCode ?? alternate?.DeviceCode;
            if (!string.IsNullOrWhiteSpace(repeatDevice))
            {
                await RunRepeatedOpenCloseScenarioAsync(repeatDevice!).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(directSuccess?.DeviceCode) && !string.IsNullOrWhiteSpace(alternate?.DeviceCode))
            {
                await RunQuickSwitchScenarioAsync(directSuccess!.DeviceCode, alternate!.DeviceCode).ConfigureAwait(false);
                await RunSwitchWhilePlayingScenarioAsync(directSuccess.DeviceCode, alternate.DeviceCode).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(fallbackSuccess?.DeviceCode))
            {
                await RunFallbackCloseScenarioAsync(fallbackSuccess!.DeviceCode).ConfigureAwait(false);
            }
            else
            {
                await WriteRunnerEventAsync("preview-acceptance-skip", "scenario=fallback-close, reason=no_fallback_success_device");
            }

            if (!string.IsNullOrWhiteSpace(directSuccess?.DeviceCode))
            {
                await RunCloseWindowWhilePlayingScenarioAsync(directSuccess!.DeviceCode).ConfigureAwait(false);
            }
            else
            {
                await WriteRunnerEventAsync("preview-acceptance-skip", "scenario=close-main-window-while-webrtc, reason=no_direct_webrtc_success_device");
                await CloseMainWindowAsync().ConfigureAwait(false);
            }

            await WriteSessionAndReleaseValidationAsync().ConfigureAwait(false);
            await WriteExitChainValidationAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-runner-failed",
                $"type={ex.GetType().FullName}, message={ex.Message}");
            await CloseMainWindowAsync().ConfigureAwait(false);
        }
    }

    private async Task RunRepeatedOpenCloseScenarioAsync(string deviceCode)
    {
        await WriteRunnerEventAsync("preview-acceptance-scenario-start", $"scenario=repeated-open-close, deviceCode={deviceCode}");
        for (var index = 0; index < 3; index++)
        {
            var result = await ExercisePreviewAsync(deviceCode, closeAfter: true).ConfigureAwait(false);
            await WriteRunnerEventAsync(
                "preview-acceptance-scenario-step",
                $"scenario=repeated-open-close, iteration={index + 1}, deviceCode={deviceCode}, success={result.IsSuccess}, finalProtocol={ToProtocolKey(result.FinalProtocol)}, failureReason={result.FailureReason ?? "none"}");
            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private async Task RunQuickSwitchScenarioAsync(string firstDeviceCode, string secondDeviceCode)
    {
        await WriteRunnerEventAsync(
            "preview-acceptance-scenario-start",
            $"scenario=quick-switch, firstDeviceCode={firstDeviceCode}, secondDeviceCode={secondDeviceCode}");

        var baseline = SnapshotEventCount();
        await SelectDeviceAsync(firstDeviceCode).ConfigureAwait(false);
        await OpenPreviewAsync().ConfigureAwait(false);
        var firstResolved = await WaitForEventAsync(
            baseline,
            evt => evt.EventName == "preview-session-resolved"
                   && MatchesDevice(evt, firstDeviceCode),
            DeviceTimeout).ConfigureAwait(false);

        if (firstResolved is null)
        {
            await WriteRunnerEventAsync("preview-acceptance-skip", $"scenario=quick-switch, reason=first_session_not_resolved, deviceCode={firstDeviceCode}");
            return;
        }

        var firstSessionId = firstResolved.GetValue("sessionId");
        await SelectDeviceAsync(secondDeviceCode).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(firstSessionId))
        {
            await WaitForReleaseAsync(firstSessionId!, baseline).ConfigureAwait(false);
        }

        var secondResult = await WaitForPreviewOutcomeAsync(secondDeviceCode, baseline, DeviceTimeout).ConfigureAwait(false);
        await ClosePreviewAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(secondResult.LastSessionId))
        {
            await WaitForReleaseAsync(secondResult.LastSessionId!, baseline).ConfigureAwait(false);
        }

        await WriteRunnerEventAsync(
            "preview-acceptance-scenario-step",
            $"scenario=quick-switch, firstDeviceCode={firstDeviceCode}, secondDeviceCode={secondDeviceCode}, success={secondResult.IsSuccess}, finalProtocol={ToProtocolKey(secondResult.FinalProtocol)}, failureReason={secondResult.FailureReason ?? "none"}");
    }

    private async Task RunSwitchWhilePlayingScenarioAsync(string firstDeviceCode, string secondDeviceCode)
    {
        await WriteRunnerEventAsync(
            "preview-acceptance-scenario-start",
            $"scenario=switch-while-webrtc-playing, firstDeviceCode={firstDeviceCode}, secondDeviceCode={secondDeviceCode}");

        var baseline = SnapshotEventCount();
        await SelectDeviceAsync(firstDeviceCode).ConfigureAwait(false);
        await OpenPreviewAsync().ConfigureAwait(false);
        var firstResult = await WaitForPreviewOutcomeAsync(firstDeviceCode, baseline, DeviceTimeout).ConfigureAwait(false);
        if (!firstResult.IsSuccess || firstResult.FinalProtocol != SitePreviewProtocol.WebRtc || string.IsNullOrWhiteSpace(firstResult.LastSessionId))
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-skip",
                $"scenario=switch-while-webrtc-playing, reason=first_device_not_webrtc_ready, deviceCode={firstDeviceCode}, finalProtocol={ToProtocolKey(firstResult.FinalProtocol)}, success={firstResult.IsSuccess}");
            await ClosePreviewAsync().ConfigureAwait(false);
            return;
        }

        var switchBaseline = SnapshotEventCount();
        await SelectDeviceAsync(secondDeviceCode).ConfigureAwait(false);
        await WaitForReleaseAsync(firstResult.LastSessionId!, switchBaseline).ConfigureAwait(false);
        var secondResult = await WaitForPreviewOutcomeAsync(secondDeviceCode, switchBaseline, DeviceTimeout).ConfigureAwait(false);
        await ClosePreviewAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(secondResult.LastSessionId))
        {
            await WaitForReleaseAsync(secondResult.LastSessionId!, switchBaseline).ConfigureAwait(false);
        }

        await WriteRunnerEventAsync(
            "preview-acceptance-scenario-step",
            $"scenario=switch-while-webrtc-playing, firstDeviceCode={firstDeviceCode}, secondDeviceCode={secondDeviceCode}, success={secondResult.IsSuccess}, finalProtocol={ToProtocolKey(secondResult.FinalProtocol)}, failureReason={secondResult.FailureReason ?? "none"}");
    }

    private async Task RunFallbackCloseScenarioAsync(string deviceCode)
    {
        await WriteRunnerEventAsync("preview-acceptance-scenario-start", $"scenario=fallback-close, deviceCode={deviceCode}");
        var baseline = SnapshotEventCount();
        await SelectDeviceAsync(deviceCode).ConfigureAwait(false);
        await OpenPreviewAsync().ConfigureAwait(false);
        var result = await WaitForPreviewOutcomeAsync(deviceCode, baseline, DeviceTimeout).ConfigureAwait(false);
        if (!result.IsSuccess || result.FinalProtocol == SitePreviewProtocol.WebRtc || string.IsNullOrWhiteSpace(result.LastSessionId))
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-skip",
                $"scenario=fallback-close, reason=device_not_ready_on_fallback, deviceCode={deviceCode}, finalProtocol={ToProtocolKey(result.FinalProtocol)}, success={result.IsSuccess}");
            await ClosePreviewAsync().ConfigureAwait(false);
            return;
        }

        await ClosePreviewAsync().ConfigureAwait(false);
        await WaitForReleaseAsync(result.LastSessionId!, baseline).ConfigureAwait(false);
        await WriteRunnerEventAsync(
            "preview-acceptance-scenario-step",
            $"scenario=fallback-close, deviceCode={deviceCode}, finalProtocol={ToProtocolKey(result.FinalProtocol)}");
    }

    private async Task RunCloseWindowWhilePlayingScenarioAsync(string deviceCode)
    {
        await WriteRunnerEventAsync("preview-acceptance-scenario-start", $"scenario=close-main-window-while-webrtc-playing, deviceCode={deviceCode}");
        var baseline = SnapshotEventCount();
        await SelectDeviceAsync(deviceCode).ConfigureAwait(false);
        await OpenPreviewAsync().ConfigureAwait(false);
        var result = await WaitForPreviewOutcomeAsync(deviceCode, baseline, DeviceTimeout).ConfigureAwait(false);
        if (!result.IsSuccess || result.FinalProtocol != SitePreviewProtocol.WebRtc)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-skip",
                $"scenario=close-main-window-while-webrtc-playing, reason=device_not_webrtc_ready, deviceCode={deviceCode}, finalProtocol={ToProtocolKey(result.FinalProtocol)}, success={result.IsSuccess}");
            await CloseMainWindowAsync(
                deviceCode,
                result.LastSessionId,
                result.FinalProtocol,
                "device_not_webrtc_ready").ConfigureAwait(false);
            return;
        }

        await WriteRunnerEventAsync(
            "preview-acceptance-close-main-window",
            $"deviceCode={deviceCode}, sessionId={result.LastSessionId ?? "unknown"}, finalProtocol={ToProtocolKey(result.FinalProtocol)}");
        await CloseMainWindowAsync(
            deviceCode,
            result.LastSessionId,
            result.FinalProtocol,
            "validated_webrtc_playing").ConfigureAwait(false);
    }

    private async Task<PreviewAttemptResult> ExercisePreviewAsync(
        string deviceCode,
        bool closeAfter,
        bool forceInitialWebRtcFailure = false)
    {
        await WriteRunnerEventAsync("preview-acceptance-device-start", $"deviceCode={deviceCode}");
        var baseline = SnapshotEventCount();
        await SelectDeviceAsync(deviceCode).ConfigureAwait(false);
        if (forceInitialWebRtcFailure)
        {
            await shellWindow.Dispatcher.InvokeAsync(
                () => shellViewModel.PrepareAcceptanceForcedWebRtcFailure());
        }

        await OpenPreviewAsync().ConfigureAwait(false);

        var result = await WaitForPreviewOutcomeAsync(deviceCode, baseline, DeviceTimeout).ConfigureAwait(false);

        if (closeAfter)
        {
            await ClosePreviewAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.LastSessionId))
            {
                await WaitForReleaseAsync(result.LastSessionId!, baseline).ConfigureAwait(false);
            }
        }

        await WriteRunnerEventAsync(
            "preview-acceptance-device-end",
            $"deviceCode={deviceCode}, success={result.IsSuccess}, preferredProtocol={ToProtocolKey(result.PreferredProtocol)}, finalProtocol={ToProtocolKey(result.FinalProtocol)}, fallbackTriggered={result.FallbackTriggered}, failureReason={result.FailureReason ?? "none"}, sessionId={result.LastSessionId ?? "none"}");
        return result;
    }

    private async Task WriteWebRtcSuccessMetricsAsync(IReadOnlyList<PreviewAttemptResult> results)
    {
        if (results.Count == 0)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-webrtc-metrics",
                "devices=0, resolved=0, preferredWebRtc=0, preferredWebRtcRate=0.00%, success=0, webrtcSuccess=0, webrtcSuccessRate=0.00%");
            return;
        }

        var resolvedCount = results.Count(result => result.PreferredProtocol != SitePreviewProtocol.Unknown || result.FinalProtocol != SitePreviewProtocol.Unknown);
        var preferredWebRtc = results.Count(result => result.PreferredProtocol == SitePreviewProtocol.WebRtc);
        var successCount = results.Count(result => result.IsSuccess);
        var webrtcSuccessCount = results.Count(result => result.IsSuccess && result.FinalProtocol == SitePreviewProtocol.WebRtc);
        var preferredRate = preferredWebRtc * 100d / results.Count;
        var webrtcSuccessRate = webrtcSuccessCount * 100d / results.Count;

        await WriteRunnerEventAsync(
            "preview-acceptance-webrtc-metrics",
            FormattableString.Invariant(
                $"devices={results.Count}, resolved={resolvedCount}, preferredWebRtc={preferredWebRtc}, preferredWebRtcRate={preferredRate:F2}%, success={successCount}, webrtcSuccess={webrtcSuccessCount}, webrtcSuccessRate={webrtcSuccessRate:F2}%"));
    }

    private async Task WriteFallbackSampleAsync(
        IReadOnlyList<PreviewAttemptResult> results,
        PreviewAttemptResult? fallbackOverride = null)
    {
        var fallback = fallbackOverride ?? results.FirstOrDefault(result =>
            result.IsSuccess
            && result.PreferredProtocol == SitePreviewProtocol.WebRtc
            && result.FinalProtocol is SitePreviewProtocol.Flv or SitePreviewProtocol.Hls
            && result.FallbackTriggered);

        if (fallback is null)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-fallback-sample",
                "status=missing, requirement=need_at_least_one_webrtc_fallback_success");
            return;
        }

        await WriteRunnerEventAsync(
            "preview-acceptance-fallback-sample",
            $"status=ok, sampleSource={(fallbackOverride is null ? "scan" : "probe")}, deviceCode={fallback.DeviceCode}, sessionId={fallback.LastSessionId ?? "none"}, preferredProtocol={ToProtocolKey(fallback.PreferredProtocol)}, finalProtocol={ToProtocolKey(fallback.FinalProtocol)}, failureReason={fallback.FailureReason ?? "none"}");
    }

    private async Task WriteSessionAndReleaseValidationAsync()
    {
        List<AcceptanceDiagnosticEvent> snapshot;
        lock (syncRoot)
        {
            snapshot = [.. diagnosticEvents];
        }

        var playbackReadyEvents = snapshot
            .Where(evt => evt.EventName == "preview-playback-ready" && !string.IsNullOrWhiteSpace(evt.GetValue("sessionId")))
            .ToArray();
        var resolvedSessionIds = snapshot
            .Where(evt => evt.EventName == "preview-session-resolved")
            .Select(evt => evt.GetValue("sessionId"))
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var releasedSessionIds = snapshot
            .Where(evt => evt.EventName == "preview-resources-released")
            .Select(evt => evt.GetValue("sessionId"))
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .Select(sessionId => sessionId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var readySessionsWithoutResolve = playbackReadyEvents
            .Select(evt => evt.GetValue("sessionId")!)
            .Where(sessionId => !resolvedSessionIds.Contains(sessionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readySessionsWithoutRelease = playbackReadyEvents
            .Select(evt => evt.GetValue("sessionId")!)
            .Where(sessionId => !releasedSessionIds.Contains(sessionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteRunnerEventAsync(
            "preview-acceptance-session-trace",
            $"resolvedSessions={resolvedSessionIds.Count}, readySessions={playbackReadyEvents.Length}, releasedSessions={releasedSessionIds.Count}, missingResolve={readySessionsWithoutResolve.Length}, missingRelease={readySessionsWithoutRelease.Length}");

        if (readySessionsWithoutResolve.Length > 0)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-session-trace-missing-resolve",
                $"sessionIds={string.Join("|", readySessionsWithoutResolve)}");
        }

        if (readySessionsWithoutRelease.Length > 0)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-session-trace-missing-release",
                $"sessionIds={string.Join("|", readySessionsWithoutRelease)}");
        }
    }

    private async Task WriteExitChainValidationAsync()
    {
        List<AcceptanceDiagnosticEvent> snapshot;
        lock (syncRoot)
        {
            snapshot = [.. diagnosticEvents];
        }

        var closeWindowEvent = snapshot.LastOrDefault(evt => evt.EventName == "preview-acceptance-close-main-window");
        var shutdownEvent = snapshot.LastOrDefault(evt => evt.EventName == "preview-shutdown-requested");
        var releaseAfterShutdown = snapshot.LastOrDefault(evt =>
            evt.EventName == "preview-resources-released"
            && shutdownEvent is not null
            && evt.OccurredAt >= shutdownEvent.OccurredAt);

        await WriteRunnerEventAsync(
            "preview-acceptance-exit-chain",
            $"closedMainWindow={closeWindowEvent is not null}, shutdownRequested={shutdownEvent is not null}, releaseAfterShutdown={releaseAfterShutdown is not null}");
    }

    private async Task SelectDeviceAsync(string deviceCode)
    {
        await shellWindow.Dispatcher.InvokeAsync(() => shellViewModel.HandleMapPointSelected(deviceCode));
        await WaitForConditionAsync(
            () => string.Equals(shellViewModel.SelectedDetail?.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase),
            SelectionTimeout).ConfigureAwait(false);
    }

    private async Task OpenPreviewAsync()
    {
        await shellWindow.Dispatcher.InvokeAsync(() => shellViewModel.OpenPreviewCommand.Execute(null));
    }

    private async Task ClosePreviewAsync()
    {
        await shellWindow.Dispatcher.InvokeAsync(() => shellViewModel.ClosePreviewCommand.Execute(null));
    }

    private async Task CloseMainWindowAsync(
        string? deviceCode = null,
        string? sessionId = null,
        SitePreviewProtocol finalProtocol = SitePreviewProtocol.Unknown,
        string reason = "runner_request")
    {
        if (!HasEvent("preview-acceptance-close-main-window"))
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-close-main-window",
                $"deviceCode={deviceCode ?? "unknown"}, sessionId={sessionId ?? "unknown"}, finalProtocol={ToProtocolKey(finalProtocol)}, reason={reason}");
        }

        if (!shellWindow.Dispatcher.HasShutdownStarted)
        {
            await shellWindow.RequestCloseForAcceptanceAsync().ConfigureAwait(false);
        }
    }

    private async Task<PreviewAttemptResult?> TryAcquireFallbackSampleAsync(IReadOnlyList<PreviewAttemptResult> results)
    {
        var candidates = results
            .Where(result => result.IsSuccess && result.FinalProtocol == SitePreviewProtocol.WebRtc)
            .Select(result => result.DeviceCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var deviceCode in candidates)
        {
            await WriteRunnerEventAsync(
                "preview-acceptance-scenario-start",
                $"scenario=forced-webrtc-fallback-probe, deviceCode={deviceCode}");

            var result = await ExercisePreviewAsync(
                deviceCode,
                closeAfter: true,
                forceInitialWebRtcFailure: true).ConfigureAwait(false);

            await WriteRunnerEventAsync(
                "preview-acceptance-scenario-step",
                $"scenario=forced-webrtc-fallback-probe, deviceCode={deviceCode}, success={result.IsSuccess}, finalProtocol={ToProtocolKey(result.FinalProtocol)}, fallbackTriggered={result.FallbackTriggered}, failureReason={result.FailureReason ?? "none"}");

            if (result.IsSuccess
                && result.PreferredProtocol == SitePreviewProtocol.WebRtc
                && result.FinalProtocol is SitePreviewProtocol.Flv or SitePreviewProtocol.Hls
                && result.FallbackTriggered)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<PreviewAttemptResult> WaitForPreviewOutcomeAsync(string deviceCode, int baselineIndex, TimeSpan timeout)
    {
        var preferredProtocol = SitePreviewProtocol.Unknown;
        var finalProtocol = SitePreviewProtocol.Unknown;
        string? lastSessionId = null;
        string? failureReason = null;
        var fallbackTriggered = false;
        var scanIndex = baselineIndex;
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var diagnosticEvent in GetEventsSince(ref scanIndex))
            {
                if (!MatchesDevice(diagnosticEvent, deviceCode))
                {
                    continue;
                }

                switch (diagnosticEvent.EventName)
                {
                    case "preview-session-resolved":
                        preferredProtocol = ParseProtocol(diagnosticEvent.GetValue("preferredProtocol")) ?? preferredProtocol;
                        finalProtocol = ParseProtocol(diagnosticEvent.GetValue("finalProtocol")) ?? finalProtocol;
                        lastSessionId = diagnosticEvent.GetValue("sessionId") ?? lastSessionId;
                        fallbackTriggered = ParseBool(diagnosticEvent.GetValue("fallbackTriggered")) || fallbackTriggered;
                        break;

                    case "preview-playback-ready":
                        preferredProtocol = ParseProtocol(diagnosticEvent.GetValue("preferredProtocol")) ?? preferredProtocol;
                        finalProtocol = ParseProtocol(diagnosticEvent.GetValue("finalProtocol")) ?? finalProtocol;
                        lastSessionId = diagnosticEvent.GetValue("sessionId") ?? lastSessionId;
                        failureReason = NullIfNone(diagnosticEvent.GetValue("failureReason"));
                        fallbackTriggered = ParseBool(diagnosticEvent.GetValue("fallbackTriggered")) || fallbackTriggered;
                        return new PreviewAttemptResult(deviceCode, true, preferredProtocol, finalProtocol, fallbackTriggered, failureReason, lastSessionId);

                    case "preview-playback-failed":
                        lastSessionId = diagnosticEvent.GetValue("sessionId") ?? lastSessionId;
                        failureReason = diagnosticEvent.GetValue("reason") ?? failureReason;
                        finalProtocol = ParseProtocol(diagnosticEvent.GetValue("protocol")) ?? finalProtocol;
                        if (finalProtocol is not SitePreviewProtocol.WebRtc and not SitePreviewProtocol.Flv)
                        {
                            return new PreviewAttemptResult(deviceCode, false, preferredProtocol, finalProtocol, fallbackTriggered, failureReason, lastSessionId);
                        }

                        break;

                    case "preview-session-unavailable":
                        preferredProtocol = ParseProtocol(diagnosticEvent.GetValue("preferredProtocol")) ?? preferredProtocol;
                        failureReason = diagnosticEvent.GetValue("failureReason") ?? failureReason;
                        return new PreviewAttemptResult(deviceCode, false, preferredProtocol, finalProtocol, fallbackTriggered, failureReason, lastSessionId);
                }
            }

            await Task.Delay(120).ConfigureAwait(false);
        }

        return new PreviewAttemptResult(deviceCode, false, preferredProtocol, finalProtocol, fallbackTriggered, failureReason ?? "acceptance_timeout", lastSessionId);
    }

    private async Task WaitForReleaseAsync(string sessionId, int baselineIndex)
    {
        await WaitForEventAsync(
            baselineIndex,
            evt => evt.EventName == "preview-resources-released"
                   && string.Equals(evt.GetValue("sessionId"), sessionId, StringComparison.OrdinalIgnoreCase),
            ReleaseTimeout).ConfigureAwait(false);
    }

    private async Task<AcceptanceDiagnosticEvent?> WaitForEventAsync(
        int baselineIndex,
        Func<AcceptanceDiagnosticEvent, bool> predicate,
        TimeSpan timeout)
    {
        var scanIndex = baselineIndex;
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var diagnosticEvent in GetEventsSince(ref scanIndex))
            {
                if (predicate(diagnosticEvent))
                {
                    return diagnosticEvent;
                }
            }

            await Task.Delay(120).ConfigureAwait(false);
        }

        return null;
    }

    private async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var isSatisfied = await shellWindow.Dispatcher.InvokeAsync(predicate).Task.ConfigureAwait(false);
            if (isSatisfied)
            {
                return;
            }

            await Task.Delay(150).ConfigureAwait(false);
        }

        throw new TimeoutException("Preview acceptance condition timed out.");
    }

    private int SnapshotEventCount()
    {
        lock (syncRoot)
        {
            return diagnosticEvents.Count;
        }
    }

    private bool HasEvent(string eventName)
    {
        lock (syncRoot)
        {
            return diagnosticEvents.Any(evt => string.Equals(evt.EventName, eventName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private IReadOnlyList<AcceptanceDiagnosticEvent> GetEventsSince(ref int scanIndex)
    {
        lock (syncRoot)
        {
            if (scanIndex >= diagnosticEvents.Count)
            {
                return Array.Empty<AcceptanceDiagnosticEvent>();
            }

            var slice = diagnosticEvents.Skip(scanIndex).ToArray();
            scanIndex = diagnosticEvents.Count;
            return slice;
        }
    }

    private async Task WriteRunnerEventAsync(string eventName, string message)
    {
        await diagnosticService.WriteAsync(eventName, message).ConfigureAwait(false);
    }

    private void HandleDiagnosticWritten(object? sender, LocalDiagnosticWrittenEventArgs e)
    {
        lock (syncRoot)
        {
            diagnosticEvents.Add(new AcceptanceDiagnosticEvent(e.OccurredAt, e.EventName, e.Message));
        }
    }

    private static bool MatchesDevice(AcceptanceDiagnosticEvent diagnosticEvent, string deviceCode)
    {
        return string.Equals(diagnosticEvent.GetValue("deviceCode"), deviceCode, StringComparison.OrdinalIgnoreCase);
    }

    private static SitePreviewProtocol? ParseProtocol(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "webrtc" => SitePreviewProtocol.WebRtc,
            "flv" => SitePreviewProtocol.Flv,
            "hls" => SitePreviewProtocol.Hls,
            "h5" => SitePreviewProtocol.H5,
            "unknown" => SitePreviewProtocol.Unknown,
            _ => null
        };
    }

    private static bool ParseBool(string? value)
    {
        return bool.TryParse(value, out var boolValue) && boolValue;
    }

    private static string? NullIfNone(string? value)
    {
        return string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string ToProtocolKey(SitePreviewProtocol protocol)
    {
        return protocol switch
        {
            SitePreviewProtocol.WebRtc => "webrtc",
            SitePreviewProtocol.Flv => "flv",
            SitePreviewProtocol.Hls => "hls",
            SitePreviewProtocol.H5 => "h5",
            _ => "unknown"
        };
    }

    private sealed record PreviewAttemptResult(
        string DeviceCode,
        bool IsSuccess,
        SitePreviewProtocol PreferredProtocol,
        SitePreviewProtocol FinalProtocol,
        bool FallbackTriggered,
        string? FailureReason,
        string? LastSessionId);

    private sealed class AcceptanceDiagnosticEvent
    {
        private readonly Dictionary<string, string> values;

        public AcceptanceDiagnosticEvent(DateTimeOffset occurredAt, string eventName, string message)
        {
            OccurredAt = occurredAt;
            EventName = eventName;
            Message = message;
            values = ParseValues(message);
        }

        public DateTimeOffset OccurredAt { get; }

        public string EventName { get; }

        public string Message { get; }

        public string? GetValue(string key)
        {
            return values.TryGetValue(key, out var value) ? value : null;
        }

        private static Dictionary<string, string> ParseValues(string message)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(message))
            {
                return result;
            }

            foreach (var segment in message.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = segment.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
                {
                    continue;
                }

                var key = segment[..separatorIndex].Trim();
                var value = segment[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key] = value;
            }

            return result;
        }
    }
}
