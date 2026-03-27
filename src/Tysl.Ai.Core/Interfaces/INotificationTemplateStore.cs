using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface INotificationTemplateStore
{
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(CancellationToken cancellationToken = default);

    Task<NotificationTemplate> GetAsync(
        NotificationTemplateKind kind,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
}
