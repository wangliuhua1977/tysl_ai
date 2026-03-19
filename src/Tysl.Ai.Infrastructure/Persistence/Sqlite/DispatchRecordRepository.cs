using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class DispatchRecordRepository : IDispatchRecordRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DispatchRecordRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DispatchRecord>> ListLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                device_code,
                fault_code,
                fault_summary,
                dispatch_status,
                dispatch_mode,
                triggered_at,
                sent_at,
                cooling_until,
                recovered_at,
                recovery_mode,
                recovery_status,
                recovery_summary,
                message_digest,
                snapshot_path,
                last_inspection_at,
                updated_at
            FROM dispatch_record
            ORDER BY device_code ASC, triggered_at DESC, id DESC;
            """;

        var results = new List<DispatchRecord>();
        var seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = Map(reader);
            if (seenDevices.Add(record.DeviceCode))
            {
                results.Add(record);
            }
        }

        return results;
    }

    public async Task<DispatchRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<DispatchRecord?> GetLatestByDeviceAndFaultAsync(
        string deviceCode,
        string faultCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectSql} WHERE device_code = $deviceCode AND fault_code = $faultCode ORDER BY triggered_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$deviceCode", deviceCode);
        command.Parameters.AddWithValue("$faultCode", faultCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<DispatchRecord?> GetLatestUnrecoveredByDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectSql} WHERE device_code = $deviceCode AND recovered_at IS NULL ORDER BY triggered_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$deviceCode", deviceCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<long> AddAsync(DispatchRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dispatch_record (
                device_code,
                fault_code,
                fault_summary,
                dispatch_status,
                dispatch_mode,
                triggered_at,
                sent_at,
                cooling_until,
                recovered_at,
                recovery_mode,
                recovery_status,
                recovery_summary,
                message_digest,
                snapshot_path,
                last_inspection_at,
                updated_at
            )
            VALUES (
                $deviceCode,
                $faultCode,
                $faultSummary,
                $dispatchStatus,
                $dispatchMode,
                $triggeredAt,
                $sentAt,
                $coolingUntil,
                $recoveredAt,
                $recoveryMode,
                $recoveryStatus,
                $recoverySummary,
                $messageDigest,
                $snapshotPath,
                $lastInspectionAt,
                $updatedAt
            );

            SELECT last_insert_rowid();
            """;

        Bind(command, record);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAsync(DispatchRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE dispatch_record
            SET
                device_code = $deviceCode,
                fault_code = $faultCode,
                fault_summary = $faultSummary,
                dispatch_status = $dispatchStatus,
                dispatch_mode = $dispatchMode,
                triggered_at = $triggeredAt,
                sent_at = $sentAt,
                cooling_until = $coolingUntil,
                recovered_at = $recoveredAt,
                recovery_mode = $recoveryMode,
                recovery_status = $recoveryStatus,
                recovery_summary = $recoverySummary,
                message_digest = $messageDigest,
                snapshot_path = $snapshotPath,
                last_inspection_at = $lastInspectionAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$id", record.Id);
        Bind(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectSql =
        """
        SELECT
            id,
            device_code,
            fault_code,
            fault_summary,
            dispatch_status,
            dispatch_mode,
            triggered_at,
            sent_at,
            cooling_until,
            recovered_at,
            recovery_mode,
            recovery_status,
            recovery_summary,
            message_digest,
            snapshot_path,
            last_inspection_at,
            updated_at
        FROM dispatch_record
        """;

    private static void Bind(SqliteCommand command, DispatchRecord record)
    {
        command.Parameters.AddWithValue("$deviceCode", record.DeviceCode);
        command.Parameters.AddWithValue("$faultCode", record.FaultCode);
        command.Parameters.AddWithValue("$faultSummary", record.FaultSummary);
        command.Parameters.AddWithValue("$dispatchStatus", (int)record.DispatchStatus);
        command.Parameters.AddWithValue("$dispatchMode", (int)record.DispatchMode);
        command.Parameters.AddWithValue("$triggeredAt", record.TriggeredAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$sentAt", ToDbValue(record.SentAt));
        command.Parameters.AddWithValue("$coolingUntil", ToDbValue(record.CoolingUntil));
        command.Parameters.AddWithValue("$recoveredAt", ToDbValue(record.RecoveredAt));
        command.Parameters.AddWithValue("$recoveryMode", (int)record.RecoveryMode);
        command.Parameters.AddWithValue("$recoveryStatus", (int)record.RecoveryStatus);
        command.Parameters.AddWithValue("$recoverySummary", (object?)record.RecoverySummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$messageDigest", (object?)record.MessageDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$snapshotPath", (object?)record.SnapshotPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastInspectionAt", ToDbValue(record.LastInspectionAt));
        command.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.UtcDateTime.ToString("O"));
    }

    private static DispatchRecord Map(SqliteDataReader reader)
    {
        return new DispatchRecord
        {
            Id = reader.GetInt64(0),
            DeviceCode = reader.GetString(1),
            FaultCode = reader.GetString(2),
            FaultSummary = reader.GetString(3),
            DispatchStatus = (DispatchStatus)reader.GetInt64(4),
            DispatchMode = (DispatchMode)reader.GetInt64(5),
            TriggeredAt = DateTimeOffset.Parse(reader.GetString(6)),
            SentAt = ParseDateTimeOffset(reader, 7),
            CoolingUntil = ParseDateTimeOffset(reader, 8),
            RecoveredAt = ParseDateTimeOffset(reader, 9),
            RecoveryMode = (RecoveryMode)reader.GetInt64(10),
            RecoveryStatus = (RecoveryStatus)reader.GetInt64(11),
            RecoverySummary = reader.IsDBNull(12) ? null : reader.GetString(12),
            MessageDigest = reader.IsDBNull(13) ? null : reader.GetString(13),
            SnapshotPath = reader.IsDBNull(14) ? null : reader.GetString(14),
            LastInspectionAt = ParseDateTimeOffset(reader, 15),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(16))
        };
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value is null ? DBNull.Value : value.Value.UtcDateTime.ToString("O");
    }

    private static DateTimeOffset? ParseDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    }
}
