using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SqliteWebhookEndpointStore : IWebhookEndpointStore
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteWebhookEndpointStore(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(
        WebhookEndpointPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            pool is null
                ? """
                  SELECT
                      id,
                      pool,
                      name,
                      webhook_url,
                      usage_remark,
                      is_enabled,
                      sort_order,
                      created_at,
                      updated_at
                  FROM webhook_endpoint
                  ORDER BY pool ASC, sort_order ASC, updated_at DESC;
                  """
                : """
                  SELECT
                      id,
                      pool,
                      name,
                      webhook_url,
                      usage_remark,
                      is_enabled,
                      sort_order,
                      created_at,
                      updated_at
                  FROM webhook_endpoint
                  WHERE pool = $pool
                  ORDER BY sort_order ASC, updated_at DESC;
                  """;

        if (pool is not null)
        {
            command.Parameters.AddWithValue("$pool", (int)pool.Value);
        }

        var items = new List<WebhookEndpoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public async Task<WebhookEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                pool,
                name,
                webhook_url,
                usage_remark,
                is_enabled,
                sort_order,
                created_at,
                updated_at
            FROM webhook_endpoint
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Map(reader)
            : null;
    }

    public async Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO webhook_endpoint (
                id,
                pool,
                name,
                webhook_url,
                usage_remark,
                is_enabled,
                sort_order,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $pool,
                $name,
                $webhookUrl,
                $usageRemark,
                $isEnabled,
                $sortOrder,
                $createdAt,
                $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                pool = excluded.pool,
                name = excluded.name,
                webhook_url = excluded.webhook_url,
                usage_remark = excluded.usage_remark,
                is_enabled = excluded.is_enabled,
                sort_order = excluded.sort_order,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$id", endpoint.Id);
        command.Parameters.AddWithValue("$pool", (int)endpoint.Pool);
        command.Parameters.AddWithValue("$name", endpoint.Name);
        command.Parameters.AddWithValue("$webhookUrl", endpoint.WebhookUrl);
        command.Parameters.AddWithValue("$usageRemark", (object?)endpoint.UsageRemark ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", endpoint.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", endpoint.SortOrder);
        command.Parameters.AddWithValue("$createdAt", endpoint.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", endpoint.UpdatedAt.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM webhook_endpoint WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WebhookEndpoint Map(SqliteDataReader reader)
    {
        return new WebhookEndpoint
        {
            Id = reader.GetString(0),
            Pool = (WebhookEndpointPool)reader.GetInt64(1),
            Name = reader.GetString(2),
            WebhookUrl = reader.GetString(3),
            UsageRemark = reader.IsDBNull(4) ? null : reader.GetString(4),
            IsEnabled = reader.GetInt64(5) == 1,
            SortOrder = reader.GetInt32(6),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(8))
        };
    }
}
