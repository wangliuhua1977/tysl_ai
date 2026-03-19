using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SiteRuntimeStateRepository : ISiteRuntimeStateRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SiteRuntimeStateRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SiteRuntimeState>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                device_code,
                last_inspection_at,
                last_online_state,
                last_product_state,
                last_preview_resolve_state,
                last_snapshot_path,
                last_snapshot_at,
                last_fault_code,
                last_fault_summary,
                consecutive_failure_count,
                last_inspection_run_state,
                updated_at
            FROM site_runtime_state
            ORDER BY updated_at DESC;
            """;

        var results = new List<SiteRuntimeState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<SiteRuntimeState?> GetByDeviceCodeAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                device_code,
                last_inspection_at,
                last_online_state,
                last_product_state,
                last_preview_resolve_state,
                last_snapshot_path,
                last_snapshot_at,
                last_fault_code,
                last_fault_summary,
                consecutive_failure_count,
                last_inspection_run_state,
                updated_at
            FROM site_runtime_state
            WHERE device_code = $deviceCode
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$deviceCode", deviceCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpsertAsync(SiteRuntimeState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO site_runtime_state (
                device_code,
                last_inspection_at,
                last_online_state,
                last_product_state,
                last_preview_resolve_state,
                last_snapshot_path,
                last_snapshot_at,
                last_fault_code,
                last_fault_summary,
                consecutive_failure_count,
                last_inspection_run_state,
                updated_at
            )
            VALUES (
                $deviceCode,
                $lastInspectionAt,
                $lastOnlineState,
                $lastProductState,
                $lastPreviewResolveState,
                $lastSnapshotPath,
                $lastSnapshotAt,
                $lastFaultCode,
                $lastFaultSummary,
                $consecutiveFailureCount,
                $lastInspectionRunState,
                $updatedAt
            )
            ON CONFLICT(device_code) DO UPDATE SET
                last_inspection_at = excluded.last_inspection_at,
                last_online_state = excluded.last_online_state,
                last_product_state = excluded.last_product_state,
                last_preview_resolve_state = excluded.last_preview_resolve_state,
                last_snapshot_path = excluded.last_snapshot_path,
                last_snapshot_at = excluded.last_snapshot_at,
                last_fault_code = excluded.last_fault_code,
                last_fault_summary = excluded.last_fault_summary,
                consecutive_failure_count = excluded.consecutive_failure_count,
                last_inspection_run_state = excluded.last_inspection_run_state,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$deviceCode", state.DeviceCode);
        command.Parameters.AddWithValue("$lastInspectionAt", ToDbValue(state.LastInspectionAt));
        command.Parameters.AddWithValue("$lastOnlineState", (int)state.LastOnlineState);
        command.Parameters.AddWithValue("$lastProductState", (object?)state.LastProductState ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastPreviewResolveState", (int)state.LastPreviewResolveState);
        command.Parameters.AddWithValue("$lastSnapshotPath", (object?)state.LastSnapshotPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastSnapshotAt", ToDbValue(state.LastSnapshotAt));
        command.Parameters.AddWithValue("$lastFaultCode", (int)state.LastFaultCode);
        command.Parameters.AddWithValue("$lastFaultSummary", (object?)state.LastFaultSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$consecutiveFailureCount", state.ConsecutiveFailureCount);
        command.Parameters.AddWithValue("$lastInspectionRunState", (int)state.LastInspectionRunState);
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SiteRuntimeState Map(SqliteDataReader reader)
    {
        return new SiteRuntimeState
        {
            DeviceCode = reader.GetString(0),
            LastInspectionAt = ParseDateTimeOffset(reader, 1),
            LastOnlineState = (DemoOnlineState)reader.GetInt64(2),
            LastProductState = reader.IsDBNull(3) ? null : reader.GetString(3),
            LastPreviewResolveState = (PreviewResolveState)reader.GetInt64(4),
            LastSnapshotPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            LastSnapshotAt = ParseDateTimeOffset(reader, 6),
            LastFaultCode = (RuntimeFaultCode)reader.GetInt64(7),
            LastFaultSummary = reader.IsDBNull(8) ? null : reader.GetString(8),
            ConsecutiveFailureCount = reader.GetInt32(9),
            LastInspectionRunState = (InspectionRunState)reader.GetInt64(10),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(11))
        };
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value is null
            ? DBNull.Value
            : value.Value.UtcDateTime.ToString("O");
    }

    private static DateTimeOffset? ParseDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    }
}
