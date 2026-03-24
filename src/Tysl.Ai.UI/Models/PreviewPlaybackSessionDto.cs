namespace Tysl.Ai.UI.Models;

public sealed class PreviewPlaybackSessionDto
{
    public string PlaybackSessionId { get; set; } = string.Empty;

    public string DeviceCode { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public string? WebRtcApiUrl { get; set; }

    public bool WebRtcUrlAcquired { get; set; }

    public int ReadyTimeoutSeconds { get; set; } = 10;

    public bool ForceInitialWebRtcFailure { get; set; }

    public string? ForceFailureCategory { get; set; }

    public string? ForceFailureReason { get; set; }
}
