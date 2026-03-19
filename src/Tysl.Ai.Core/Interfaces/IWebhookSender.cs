using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(
        string webhookUrl,
        WebhookMessage message,
        CancellationToken cancellationToken = default);
}
