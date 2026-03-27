using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface ISitePreviewService
{
    Task<SitePreviewResolveResult> ResolvePreviewAsync(
        string deviceCode,
        IReadOnlyList<SitePreviewProtocol> protocolOrder,
        CancellationToken cancellationToken = default);

    Task<SitePreviewResolveResult> ResolveUserPreviewAsync(
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<SitePreviewResolveResult> ResolveFallbackPreviewAsync(
        string deviceCode,
        SitePreviewProtocol failedProtocol,
        CancellationToken cancellationToken = default);

    Task<WebRtcPlaybackNegotiationResult> NegotiateWebRtcAsync(
        string deviceCode,
        string apiUrl,
        string streamUrl,
        string offerSdp,
        CancellationToken cancellationToken = default);

    Task<PreviewProxyResourceResult> FetchPreviewResourceAsync(
        PreviewProxyRequest request,
        CancellationToken cancellationToken = default);

    Task RecordPlaybackAsync(
        SitePreviewPlaybackRecord record,
        CancellationToken cancellationToken = default);
}
