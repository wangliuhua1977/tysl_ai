using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SqliteNotificationTemplateStore : INotificationTemplateStore
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteNotificationTemplateStore(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                kind,
                content,
                updated_at
            FROM notification_template
            ORDER BY kind ASC;
            """;

        var items = new Dictionary<NotificationTemplateKind, NotificationTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var template = Map(reader);
            items[template.Kind] = template;
        }

        foreach (var kind in Enum.GetValues<NotificationTemplateKind>())
        {
            items.TryAdd(kind, NotificationTemplate.CreateDefault(kind));
        }

        return items
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToArray();
    }

    public async Task<NotificationTemplate> GetAsync(
        NotificationTemplateKind kind,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                kind,
                content,
                updated_at
            FROM notification_template
            WHERE kind = $kind
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Map(reader)
            : NotificationTemplate.CreateDefault(kind);
    }

    public async Task UpsertAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO notification_template (
                kind,
                content,
                updated_at
            )
            VALUES (
                $kind,
                $content,
                $updatedAt
            )
            ON CONFLICT(kind) DO UPDATE SET
                content = excluded.content,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$kind", (int)template.Kind);
        command.Parameters.AddWithValue("$content", template.Content);
        command.Parameters.AddWithValue("$updatedAt", template.UpdatedAt.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NotificationTemplate Map(SqliteDataReader reader)
    {
        return new NotificationTemplate
        {
            Kind = (NotificationTemplateKind)reader.GetInt64(0),
            Content = reader.GetString(1),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(2))
        };
    }
}
