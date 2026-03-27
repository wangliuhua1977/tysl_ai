using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IWebhookNotificationService
{
    Task<WebhookNotificationDispatchPlan> BuildDispatchPlanAsync(
        NotificationTemplateKind templateKind,
        NotificationTemplateRenderContext context,
        NotificationDispatchTraceContext? traceContext = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookSendResult>> SendAsync(
        NotificationTemplateKind templateKind,
        NotificationTemplateRenderContext context,
        NotificationDispatchTraceContext? traceContext = null,
        CancellationToken cancellationToken = default);
}
