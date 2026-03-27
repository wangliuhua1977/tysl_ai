using Tysl.Ai.Core.Enums;

namespace Tysl.Ai.Core.Models;

public sealed record ManualDispatchPreparation
{
    public required string DeviceCode { get; init; }

    public required string SiteDisplayName { get; init; }

    public string? ProductAccessNumber { get; init; }

    public required string FaultReason { get; init; }

    public string? MaintenanceUnit { get; init; }

    public string? MaintainerName { get; init; }

    public string? MaintainerPhone { get; init; }

    public required NotificationTemplateKind TemplateKind { get; init; }

    public required WebhookEndpointPool NotificationPool { get; init; }

    public required int EnabledEndpointCount { get; init; }

    public required string TemplatePreview { get; init; }
}
