using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Services.Sites;

public sealed class SitePreviewService : ISitePreviewService
{
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
        return ResolvePreviewAsync(deviceCode, Array.Empty<SitePreviewProtocol>(), cancellationToken);
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
            SitePreviewProtocol.Flv => [SitePreviewProtocol.Hls, SitePreviewProtocol.WebRtc],
            SitePreviewProtocol.Hls => [SitePreviewProtocol.Flv, SitePreviewProtocol.WebRtc],
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

    public Task<PreviewProxyResourceResult> FetchPreviewResourceAsync(
        PreviewProxyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return platformPreviewProvider.FetchPreviewResourceAsync(request, cancellationToken);
    }

    public async Task RecordPlaybackAsync(
        SitePreviewPlaybackRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var currentState = await runtimeStateRepository.GetByDeviceCodeAsync(record.DeviceCode, cancellationToken)
                           ?? CreateDefaultRuntimeState(record.DeviceCode, record.OccurredAt);
        var clearsPreviewFault = record.IsSuccess
                                 && currentState.LastFaultCode == RuntimeFaultCode.PreviewResolveFailed;
        var marksPreviewFault = !record.IsSuccess && record.IsFinalChainFailure;
        var finalFailureSummary = string.IsNullOrWhiteSpace(record.FinalFailureSummary)
            ? "全协议预览失败"
            : record.FinalFailureSummary.Trim();
        var nextFaultCode = clearsPreviewFault
            ? RuntimeFaultCode.None
            : marksPreviewFault
                ? RuntimeFaultCode.PreviewResolveFailed
                : currentState.LastFaultCode;
        var nextFaultSummary = clearsPreviewFault
            ? null
            : marksPreviewFault
                ? finalFailureSummary
                : currentState.LastFaultSummary;
        var nextPreviewResolveState = record.IsSuccess
            ? PreviewResolveState.Resolved
            : marksPreviewFault
                ? PreviewResolveState.Failed
                : currentState.LastPreviewResolveState;
        var nextConsecutiveFailureCount = clearsPreviewFault
            ? 0
            : marksPreviewFault
                ? (currentState.ConsecutiveFailureCount <= 0 ? 1 : currentState.ConsecutiveFailureCount + 1)
                : currentState.ConsecutiveFailureCount;

        await runtimeStateRepository.UpsertAsync(
            currentState with
            {
                LastPreviewResolveState = nextPreviewResolveState,
                LastPreviewAt = record.OccurredAt,
                LastPreviewSessionId = record.PlaybackSessionId,
                LastPreviewPreferredProtocol = record.PreferredProtocol,
                LastPreviewProtocol = record.Protocol,
                LastPreviewSucceeded = record.IsSuccess,
                LastPreviewUsedFallback = record.UsedFallback,
                LastPreviewFailureProtocol = record.FailureProtocol,
                LastPreviewFailureReason = record.FailureReason,
                LastFaultCode = nextFaultCode,
                LastFaultSummary = nextFaultSummary,
                ConsecutiveFailureCount = nextConsecutiveFailureCount,
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
