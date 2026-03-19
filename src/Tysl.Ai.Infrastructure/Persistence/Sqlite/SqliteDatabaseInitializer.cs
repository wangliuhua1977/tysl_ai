using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SqliteDatabaseInitializer
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA journal_mode = WAL;";
        await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);

        await CreateSiteLocalProfileTableAsync(connection, cancellationToken);
        await CreateSiteRuntimeStateTableAsync(connection, cancellationToken);
        await CreateSnapshotRecordTableAsync(connection, cancellationToken);
        await CreateInspectionSettingsTableAsync(connection, cancellationToken);
        await CreateDispatchPolicyTableAsync(connection, cancellationToken);
        await CreateDispatchRecordTableAsync(connection, cancellationToken);

        if (await TableExistsAsync(connection, "site_profile", cancellationToken))
        {
            await MigrateLegacySiteProfileAsync(connection, cancellationToken);

            await using var dropLegacyTableCommand = connection.CreateCommand();
            dropLegacyTableCommand.CommandText = "DROP TABLE IF EXISTS site_profile;";
            await dropLegacyTableCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await SeedInspectionSettingsAsync(connection, cancellationToken);
        await SeedDispatchPolicyAsync(connection, cancellationToken);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM site_local_profile;";
        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (existingCount > 0)
        {
            return;
        }

        foreach (var profile in GetSeedProfiles())
        {
            await InsertSeedAsync(connection, profile, cancellationToken);
        }
    }

    private static async Task CreateSiteLocalProfileTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS site_local_profile (
                device_code TEXT PRIMARY KEY NOT NULL,
                alias TEXT NULL,
                remark TEXT NULL,
                is_monitored INTEGER NOT NULL,
                manual_longitude REAL NULL,
                manual_latitude REAL NULL,
                address_text TEXT NULL,
                product_access_number TEXT NULL,
                maintenance_unit TEXT NULL,
                maintainer_name TEXT NULL,
                maintainer_phone TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateSiteRuntimeStateTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS site_runtime_state (
                device_code TEXT PRIMARY KEY NOT NULL,
                last_inspection_at TEXT NULL,
                last_online_state INTEGER NOT NULL,
                last_product_state TEXT NULL,
                last_preview_resolve_state INTEGER NOT NULL,
                last_snapshot_path TEXT NULL,
                last_snapshot_at TEXT NULL,
                last_fault_code INTEGER NOT NULL,
                last_fault_summary TEXT NULL,
                consecutive_failure_count INTEGER NOT NULL,
                last_inspection_run_state INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateSnapshotRecordTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS snapshot_record (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_code TEXT NOT NULL,
                snapshot_path TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                is_placeholder INTEGER NOT NULL,
                summary_text TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_snapshot_record_device_captured_at
            ON snapshot_record (device_code, captured_at DESC);
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateInspectionSettingsTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS inspection_settings (
                id INTEGER PRIMARY KEY NOT NULL,
                enabled INTEGER NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL,
                interval_minutes INTEGER NOT NULL,
                snapshot_retention_count INTEGER NOT NULL,
                preview_resolve_enabled INTEGER NOT NULL,
                snapshot_enabled INTEGER NOT NULL,
                max_points_per_cycle INTEGER NOT NULL,
                detail_batch_size INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateDispatchPolicyTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS dispatch_policy (
                id INTEGER PRIMARY KEY NOT NULL,
                enabled INTEGER NOT NULL,
                mode INTEGER NOT NULL,
                cooling_minutes INTEGER NOT NULL,
                recovery_mode INTEGER NOT NULL,
                repeat_after_recovery INTEGER NOT NULL,
                notify_on_recovery INTEGER NOT NULL,
                webhook_url TEXT NULL,
                mention_mobiles TEXT NULL,
                mention_all INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateDispatchRecordTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS dispatch_record (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_code TEXT NOT NULL,
                fault_code TEXT NOT NULL,
                fault_summary TEXT NOT NULL,
                dispatch_status INTEGER NOT NULL,
                dispatch_mode INTEGER NOT NULL,
                triggered_at TEXT NOT NULL,
                sent_at TEXT NULL,
                cooling_until TEXT NULL,
                recovered_at TEXT NULL,
                recovery_mode INTEGER NOT NULL,
                recovery_status INTEGER NOT NULL,
                recovery_summary TEXT NULL,
                message_digest TEXT NULL,
                snapshot_path TEXT NULL,
                last_inspection_at TEXT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_dispatch_record_device_triggered_at
            ON dispatch_record (device_code, triggered_at DESC, id DESC);

            CREATE INDEX IF NOT EXISTS ix_dispatch_record_device_recovered_at
            ON dispatch_record (device_code, recovered_at, updated_at DESC);
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static async Task MigrateLegacySiteProfileAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var migrateCommand = connection.CreateCommand();
        migrateCommand.CommandText =
            """
            INSERT OR REPLACE INTO site_local_profile (
                device_code,
                alias,
                remark,
                is_monitored,
                manual_longitude,
                manual_latitude,
                address_text,
                product_access_number,
                maintenance_unit,
                maintainer_name,
                maintainer_phone,
                created_at,
                updated_at
            )
            SELECT
                device_code,
                alias,
                remark,
                is_monitored,
                longitude,
                latitude,
                address_text,
                product_access_number,
                maintenance_unit,
                maintainer_name,
                maintainer_phone,
                created_at,
                updated_at
            FROM site_profile
            WHERE device_code IS NOT NULL
              AND TRIM(device_code) <> '';
            """;

        await migrateCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedInspectionSettingsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM inspection_settings WHERE id = 1;";
        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (existingCount > 0)
        {
            return;
        }

        var defaults = InspectionSettings.Default;

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO inspection_settings (
                id,
                enabled,
                start_time,
                end_time,
                interval_minutes,
                snapshot_retention_count,
                preview_resolve_enabled,
                snapshot_enabled,
                max_points_per_cycle,
                detail_batch_size,
                updated_at
            )
            VALUES (
                1,
                $enabled,
                $startTime,
                $endTime,
                $intervalMinutes,
                $snapshotRetentionCount,
                $previewResolveEnabled,
                $snapshotEnabled,
                $maxPointsPerCycle,
                $detailBatchSize,
                $updatedAt
            );
            """;

        insertCommand.Parameters.AddWithValue("$enabled", defaults.Enabled ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$startTime", defaults.StartTime.ToString("HH:mm"));
        insertCommand.Parameters.AddWithValue("$endTime", defaults.EndTime.ToString("HH:mm"));
        insertCommand.Parameters.AddWithValue("$intervalMinutes", defaults.IntervalMinutes);
        insertCommand.Parameters.AddWithValue("$snapshotRetentionCount", defaults.SnapshotRetentionCount);
        insertCommand.Parameters.AddWithValue("$previewResolveEnabled", defaults.PreviewResolveEnabled ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$snapshotEnabled", defaults.SnapshotEnabled ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$maxPointsPerCycle", defaults.MaxPointsPerCycle);
        insertCommand.Parameters.AddWithValue("$detailBatchSize", defaults.DetailBatchSize);
        insertCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedDispatchPolicyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM dispatch_policy WHERE id = 1;";
        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (existingCount > 0)
        {
            return;
        }

        var defaults = DispatchPolicy.Default with
        {
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
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
            );
            """;

        insertCommand.Parameters.AddWithValue("$enabled", defaults.Enabled ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$mode", (int)defaults.Mode);
        insertCommand.Parameters.AddWithValue("$coolingMinutes", defaults.CoolingMinutes);
        insertCommand.Parameters.AddWithValue("$recoveryMode", (int)defaults.RecoveryMode);
        insertCommand.Parameters.AddWithValue("$repeatAfterRecovery", defaults.RepeatAfterRecovery ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$notifyOnRecovery", defaults.NotifyOnRecovery ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$webhookUrl", (object?)defaults.WebhookUrl ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$mentionMobiles", "[]");
        insertCommand.Parameters.AddWithValue("$mentionAll", defaults.MentionAll ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$updatedAt", defaults.UpdatedAt.UtcDateTime.ToString("O"));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSeedAsync(
        SqliteConnection connection,
        SiteLocalProfile profile,
        CancellationToken cancellationToken)
    {
        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO site_local_profile (
                device_code,
                alias,
                remark,
                is_monitored,
                manual_longitude,
                manual_latitude,
                address_text,
                product_access_number,
                maintenance_unit,
                maintainer_name,
                maintainer_phone,
                created_at,
                updated_at
            )
            VALUES (
                $deviceCode,
                $alias,
                $remark,
                $isMonitored,
                $manualLongitude,
                $manualLatitude,
                $addressText,
                $productAccessNumber,
                $maintenanceUnit,
                $maintainerName,
                $maintainerPhone,
                $createdAt,
                $updatedAt
            );
            """;

        insertCommand.Parameters.AddWithValue("$deviceCode", profile.DeviceCode);
        insertCommand.Parameters.AddWithValue("$alias", (object?)profile.Alias ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$remark", (object?)profile.Remark ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$isMonitored", profile.IsMonitored ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$manualLongitude", (object?)profile.ManualLongitude ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$manualLatitude", (object?)profile.ManualLatitude ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$addressText", (object?)profile.AddressText ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$productAccessNumber", (object?)profile.ProductAccessNumber ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintenanceUnit", (object?)profile.MaintenanceUnit ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintainerName", (object?)profile.MaintainerName ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintainerPhone", (object?)profile.MaintainerPhone ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$createdAt", profile.CreatedAt.UtcDateTime.ToString("O"));
        insertCommand.Parameters.AddWithValue("$updatedAt", profile.UpdatedAt.UtcDateTime.ToString("O"));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<SiteLocalProfile> GetSeedProfiles()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-3);

        return
        [
            new SiteLocalProfile
            {
                DeviceCode = "ACIS-DEMO-002",
                Alias = "高铁站前广场",
                Remark = "保留别名与维护信息，演示平台快照与本地补充信息合并。",
                IsMonitored = true,
                AddressText = "绍兴北站南广场",
                ProductAccessNumber = "3306020002002",
                MaintenanceUnit = "客运枢纽联保组",
                MaintainerName = "沈工",
                MaintainerPhone = "13800000002",
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddHours(2)
            },
            new SiteLocalProfile
            {
                DeviceCode = "ACIS-DEMO-004",
                Alias = "政务中心东门",
                Remark = "演示本地监测开关关闭时的视图状态。",
                IsMonitored = false,
                AddressText = "越城区政务中心东门",
                MaintenanceUnit = "政务中心值守组",
                MaintainerName = "李工",
                MaintainerPhone = "13800000004",
                CreatedAt = createdAt.AddHours(1),
                UpdatedAt = createdAt.AddHours(1)
            },
            new SiteLocalProfile
            {
                DeviceCode = "ACIS-DEMO-006",
                Alias = "科创园一期西门",
                Remark = "平台暂无坐标，依赖本地手工补录坐标兜底展示。",
                IsMonitored = true,
                ManualLongitude = 120.6678,
                ManualLatitude = 30.0084,
                AddressText = "科创园一期西门",
                MaintenanceUnit = "园区联保组",
                MaintainerName = "顾工",
                MaintainerPhone = "13800000006",
                CreatedAt = createdAt.AddHours(2),
                UpdatedAt = createdAt.AddHours(2)
            }
        ];
    }
}
