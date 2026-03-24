using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IPlatformPreviewProvider
{
    Task<SitePreviewResolveResult> ResolvePreviewAsync(
        string deviceCode,
        IReadOnlyList<SitePreviewProtocol> protocolOrder,
        CancellationToken cancellationToken = default);

    Task<WebRtcPlaybackNegotiationResult> NegotiateWebRtcAsync(
        string deviceCode,
        string apiUrl,
        string streamUrl,
        string offerSdp,
        CancellationToken cancellationToken = default);
}
