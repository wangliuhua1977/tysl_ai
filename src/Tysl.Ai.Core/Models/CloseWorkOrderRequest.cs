namespace Tysl.Ai.Core.Models;

public sealed record CloseWorkOrderRequest
{
    public required long WorkOrderId { get; init; }

    public string? ClosingRemark { get; init; }
}
