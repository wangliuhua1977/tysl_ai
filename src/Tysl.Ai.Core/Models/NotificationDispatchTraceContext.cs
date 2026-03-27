namespace Tysl.Ai.Core.Models;

public sealed record NotificationDispatchTraceContext
{
    public required string EventPrefix { get; init; }

    public required string DeviceCode { get; init; }
}
