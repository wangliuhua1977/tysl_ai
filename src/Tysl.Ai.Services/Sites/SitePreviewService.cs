using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SitePreviewService : ISitePreviewService
{
    private static readonly IReadOnlyList<SitePreviewProtocol> PrimaryOrder =
    [
        SitePreviewProtocol.WebRtc,
        SitePreviewProtocol.Flv,
        SitePreviewProtocol.Hls
    ];

    private readonly IPlatformPreviewProvider platformPreviewProvider;
    private readonly ISiteRuntimeStateRepository runtimeStateRepository;

    public SitePreviewService(
        IPlatformPreviewProvider platformPreviewProvider,
        ISiteRuntimeStateRepository runtimeStateRepository)
    {
        this.platformPreviewProvider = platformPreviewProvider;
        this.runtimeStateRepository = runtimeStateRepository;
    }

    public Task<SitePreviewResolveResult> ResolveUserPreviewAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        return ResolvePreviewAsync(deviceCode, PrimaryOrder, cancellationToken);
    }

    public Task<SitePreviewResolveResult> ResolvePreviewAsync(
        string deviceCode,
        IReadOnlyList<SitePreviewProtocol> protocolOrder,
        CancellationToken cancellationToken = default)
    {
        return platformPreviewProvider.ResolvePreviewAsync(deviceCode, protocolOrder, cancellationToken);
    }

    public Task<SitePreviewResolveResult> ResolveFallbackPreviewAsync(
        string deviceCode,
        SitePreviewProtocol failedProtocol,
        CancellationToken cancellationToken = default)
    {
        var order = failedProtocol switch
        {
            SitePreviewProtocol.WebRtc => [SitePreviewProtocol.Flv, SitePreviewProtocol.Hls],
            SitePreviewProtocol.Flv => [SitePreviewProtocol.Hls],
            _ => Array.Empty<SitePreviewProtocol>()
        };

        return order.Length == 0
            ? Task.FromResult(new SitePreviewResolveResult
            {
                IsSuccess = false,
                AttemptedProtocols = [failedProtocol],
                FailureReason = "预览暂不可用，请稍后重试。"
            })
            : ResolvePreviewAsync(deviceCode, order, cancellationToken);
    }

    public Task<WebRtcPlaybackNegotiationResult> NegotiateWebRtcAsync(
        string deviceCode,
        string apiUrl,
        string streamUrl,
        string offerSdp,
        CancellationToken cancellationToken = default)
    {
        return platformPreviewProvider.NegotiateWebRtcAsync(
            deviceCode,
            apiUrl,
            streamUrl,
            offerSdp,
            cancellationToken);
    }

    public async Task RecordPlaybackAsync(
        SitePreviewPlaybackRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var currentState = await runtimeStateRepository.GetByDeviceCodeAsync(record.DeviceCode, cancellationToken)
                           ?? CreateDefaultRuntimeState(record.DeviceCode, record.OccurredAt);

        await runtimeStateRepository.UpsertAsync(
            currentState with
            {
                LastPreviewAt = record.OccurredAt,
                LastPreviewSessionId = record.PlaybackSessionId,
                LastPreviewPreferredProtocol = record.PreferredProtocol,
                LastPreviewProtocol = record.Protocol,
                LastPreviewSucceeded = record.IsSuccess,
                LastPreviewUsedFallback = record.UsedFallback,
                LastPreviewFailureProtocol = record.FailureProtocol,
                LastPreviewFailureReason = record.FailureReason,
                UpdatedAt = record.OccurredAt
            },
            cancellationToken);
    }

    private static SiteRuntimeState CreateDefaultRuntimeState(string deviceCode, DateTimeOffset updatedAt)
    {
        return new SiteRuntimeState
        {
            DeviceCode = deviceCode,
            LastOnlineState = DemoOnlineState.Unknown,
            LastPreviewResolveState = PreviewResolveState.Unknown,
            LastPreviewPreferredProtocol = SitePreviewProtocol.Unknown,
            LastPreviewProtocol = SitePreviewProtocol.Unknown,
            LastPreviewUsedFallback = false,
            LastPreviewFailureProtocol = SitePreviewProtocol.Unknown,
            LastFaultCode = RuntimeFaultCode.None,
            ConsecutiveFailureCount = 0,
            LastInspectionRunState = InspectionRunState.None,
            UpdatedAt = updatedAt
        };
    }
}
