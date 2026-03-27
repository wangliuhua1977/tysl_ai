using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record WebhookNotificationDispatchPlan
{
    public required NotificationTemplateKind TemplateKind { get; init; }

    public required WebhookEndpointPool Pool { get; init; }

    public required string RenderedContent { get; init; }

    public IReadOnlyList<WebhookEndpoint> Endpoints { get; init; } = Array.Empty<WebhookEndpoint>();
}
