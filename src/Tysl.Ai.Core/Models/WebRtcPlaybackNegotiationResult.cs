namespace Tysl.Ai.Core.Models;

public sealed record WebRtcPlaybackNegotiationResult
{
    public required bool IsSuccess { get; init; }

    public string? ApiUrl { get; init; }

    public string? AnswerSdp { get; init; }

    public int ResponseCode { get; init; }

    public string? SessionId { get; init; }

    public string? Server { get; init; }

    public string? FailureReason { get; init; }
}
