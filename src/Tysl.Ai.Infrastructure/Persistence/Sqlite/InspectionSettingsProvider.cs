using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class InspectionSettingsProvider : IInspectionSettingsProvider
{
    private readonly SqliteConnectionFactory connectionFactory;

    public InspectionSettingsProvider(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<InspectionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                enabled,
                start_time,
                end_time,
                interval_minutes,
                snapshot_retention_count,
                preview_resolve_enabled,
                snapshot_enabled,
                max_points_per_cycle,
                detail_batch_size
            FROM inspection_settings
            WHERE id = 1
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return InspectionSettings.Default;
        }

        return new InspectionSettings
        {
            Enabled = reader.GetInt64(0) == 1,
            StartTime = ParseTime(reader.GetString(1), InspectionSettings.Default.StartTime),
            EndTime = ParseTime(reader.GetString(2), InspectionSettings.Default.EndTime),
            IntervalMinutes = NormalizePositive(reader.GetInt32(3), InspectionSettings.Default.IntervalMinutes),
            SnapshotRetentionCount = NormalizePositive(reader.GetInt32(4), InspectionSettings.Default.SnapshotRetentionCount),
            PreviewResolveEnabled = reader.GetInt64(5) == 1,
            SnapshotEnabled = reader.GetInt64(6) == 1,
            MaxPointsPerCycle = NormalizePositive(reader.GetInt32(7), InspectionSettings.Default.MaxPointsPerCycle),
            DetailBatchSize = NormalizePositive(reader.GetInt32(8), InspectionSettings.Default.DetailBatchSize)
        };
    }

    private static TimeOnly ParseTime(string value, TimeOnly fallback)
    {
        return TimeOnly.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }
}
