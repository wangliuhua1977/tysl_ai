namespace Tysl.Ai.Core.Models;

public sealed record WebhookSendResult
{
    public required bool IsSuccess { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ResponseBody { get; init; }
}
