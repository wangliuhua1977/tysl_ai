using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IWebhookEndpointStore
{
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(
        WebhookEndpointPool? pool = null,
        CancellationToken cancellationToken = default);

    Task<WebhookEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
