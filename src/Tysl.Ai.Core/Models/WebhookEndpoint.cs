using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record WebhookEndpoint
{
    public required string Id { get; init; }

    public required WebhookEndpointPool Pool { get; init; }

    public required string Name { get; init; }

    public required string WebhookUrl { get; init; }

    public string? UsageRemark { get; init; }

    public required bool IsEnabled { get; init; }

    public required int SortOrder { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
