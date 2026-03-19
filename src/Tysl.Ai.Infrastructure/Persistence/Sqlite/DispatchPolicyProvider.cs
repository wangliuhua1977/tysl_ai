using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class DispatchPolicyProvider : IDispatchPolicyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnectionFactory connectionFactory;

    public DispatchPolicyProvider(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<DispatchPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                enabled,
                mode,
                cooling_minutes,
                recovery_mode,
                repeat_after_recovery,
                notify_on_recovery,
                webhook_url,
                mention_mobiles,
                mention_all,
                updated_at
            FROM dispatch_policy
            WHERE id = 1
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return DispatchPolicy.Default;
        }

        return new DispatchPolicy
        {
            Enabled = reader.GetInt64(0) == 1,
            Mode = (DispatchMode)reader.GetInt64(1),
            CoolingMinutes = NormalizePositive(reader.GetInt32(2), DispatchPolicy.Default.CoolingMinutes),
            RecoveryMode = (RecoveryMode)reader.GetInt64(3),
            RepeatAfterRecovery = reader.GetInt64(4) == 1,
            NotifyOnRecovery = reader.GetInt64(5) == 1,
            WebhookUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
            MentionMobiles = ParseMentionMobiles(reader.IsDBNull(7) ? null : reader.GetString(7)),
            MentionAll = reader.GetInt64(8) == 1,
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(9))
        };
    }

    public async Task UpsertAsync(DispatchPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dispatch_policy (
                id,
                enabled,
                mode,
                cooling_minutes,
                recovery_mode,
                repeat_after_recovery,
                notify_on_recovery,
                webhook_url,
                mention_mobiles,
                mention_all,
                updated_at
            )
            VALUES (
                1,
                $enabled,
                $mode,
                $coolingMinutes,
                $recoveryMode,
                $repeatAfterRecovery,
                $notifyOnRecovery,
                $webhookUrl,
                $mentionMobiles,
                $mentionAll,
                $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                enabled = excluded.enabled,
                mode = excluded.mode,
                cooling_minutes = excluded.cooling_minutes,
                recovery_mode = excluded.recovery_mode,
                repeat_after_recovery = excluded.repeat_after_recovery,
                notify_on_recovery = excluded.notify_on_recovery,
                webhook_url = excluded.webhook_url,
                mention_mobiles = excluded.mention_mobiles,
                mention_all = excluded.mention_all,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$enabled", policy.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$mode", (int)policy.Mode);
        command.Parameters.AddWithValue("$coolingMinutes", Math.Max(1, policy.CoolingMinutes));
        command.Parameters.AddWithValue("$recoveryMode", (int)policy.RecoveryMode);
        command.Parameters.AddWithValue("$repeatAfterRecovery", policy.RepeatAfterRecovery ? 1 : 0);
        command.Parameters.AddWithValue("$notifyOnRecovery", policy.NotifyOnRecovery ? 1 : 0);
        command.Parameters.AddWithValue("$webhookUrl", (object?)NormalizeText(policy.WebhookUrl) ?? DBNull.Value);
        command.Parameters.AddWithValue("$mentionMobiles", SerializeMentionMobiles(policy.MentionMobiles));
        command.Parameters.AddWithValue("$mentionAll", policy.MentionAll ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", policy.UpdatedAt.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ParseMentionMobiles(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value, JsonOptions);
            return parsed?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string SerializeMentionMobiles(IReadOnlyList<string> values)
    {
        var normalized = values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
