namespace Tysl.Ai.Core.Models;

public sealed record ManualDispatchRequest
{
    public required string DeviceCode { get; init; }
}
