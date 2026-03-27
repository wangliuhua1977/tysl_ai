using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record WebhookEndpointInput
{
    public string? Id { get; init; }

    public required WebhookEndpointPool Pool { get; init; }

    public string? Name { get; init; }

    public string? WebhookUrl { get; init; }

    public string? UsageRemark { get; init; }

    public required bool IsEnabled { get; init; }

    public int SortOrder { get; init; }
}
