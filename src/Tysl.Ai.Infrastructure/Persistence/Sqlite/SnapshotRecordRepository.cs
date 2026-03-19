using Microsoft.Data.Sqlite;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SnapshotRecordRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SnapshotRecordRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(
        string deviceCode,
        string snapshotPath,
        DateTimeOffset capturedAt,
        bool isPlaceholder,
        string? summary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO snapshot_record (
                device_code,
                snapshot_path,
                captured_at,
                is_placeholder,
                summary_text
            )
            VALUES (
                $deviceCode,
                $snapshotPath,
                $capturedAt,
                $isPlaceholder,
                $summaryText
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$deviceCode", deviceCode);
        command.Parameters.AddWithValue("$snapshotPath", snapshotPath);
        command.Parameters.AddWithValue("$capturedAt", capturedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$isPlaceholder", isPlaceholder ? 1 : 0);
        command.Parameters.AddWithValue("$summaryText", (object?)summary ?? DBNull.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<SnapshotRecord>> ListByDeviceCodeAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                device_code,
                snapshot_path,
                captured_at,
                is_placeholder,
                summary_text
            FROM snapshot_record
            WHERE device_code = $deviceCode
            ORDER BY captured_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$deviceCode", deviceCode);

        var results = new List<SnapshotRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SnapshotRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetInt64(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var parameterNames = ids
            .Select((_, index) => $"$id{index}")
            .ToArray();

        command.CommandText =
            $"""
            DELETE FROM snapshot_record
            WHERE id IN ({string.Join(", ", parameterNames)});
            """;

        var index = 0;
        foreach (var id in ids)
        {
            command.Parameters.AddWithValue(parameterNames[index++], id);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record SnapshotRecord(
    long Id,
    string DeviceCode,
    string SnapshotPath,
    DateTimeOffset CapturedAt,
    bool IsPlaceholder,
    string? SummaryText);
