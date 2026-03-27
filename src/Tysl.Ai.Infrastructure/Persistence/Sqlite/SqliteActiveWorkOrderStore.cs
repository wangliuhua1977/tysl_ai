using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SqliteActiveWorkOrderStore : IActiveWorkOrderStore
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteActiveWorkOrderStore(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ActiveWorkOrder>> ListLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                work_order_id,
                device_code,
                site_name_snapshot,
                site_alias_snapshot,
                product_access_number_snapshot,
                current_fault_code,
                current_fault_reason,
                dispatch_source,
                status,
                first_dispatched_at,
                latest_exception_at,
                latest_notification_at,
                maintenance_unit_snapshot,
                maintainer_name_snapshot,
                maintainer_phone_snapshot,
                dispatch_remark_snapshot,
                recovery_confirmation_mode_snapshot,
                allow_recovery_auto_archive_snapshot,
                last_notification_summary,
                recovery_source,
                recovery_summary,
                closing_remark,
                product_status_snapshot,
                arrears_amount_snapshot,
                recovered_at,
                recovery_confirmed_at,
                closed_archived_at,
                created_at,
                updated_at
            FROM active_work_order
            ORDER BY device_code ASC, updated_at DESC, work_order_id DESC;
            """;

        var results = new List<ActiveWorkOrder>();
        var seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var workOrder = Map(reader);
            if (seenDevices.Add(workOrder.DeviceCode))
            {
                results.Add(workOrder);
            }
        }

        return results;
    }

    public async Task<ActiveWorkOrder?> GetByIdAsync(long workOrderId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE work_order_id = $workOrderId LIMIT 1;";
        command.Parameters.AddWithValue("$workOrderId", workOrderId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<ActiveWorkOrder?> GetLatestOpenByDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectSql} WHERE device_code = $deviceCode AND status <> $closedStatus ORDER BY updated_at DESC, work_order_id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$deviceCode", deviceCode);
        command.Parameters.AddWithValue("$closedStatus", (int)DispatchWorkOrderStatus.ClosedArchived);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ActiveWorkOrder>> ListByDeviceAsync(
        string deviceCode,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectSql} WHERE device_code = $deviceCode ORDER BY updated_at DESC, work_order_id DESC LIMIT $take;";
        command.Parameters.AddWithValue("$deviceCode", deviceCode);
        command.Parameters.AddWithValue("$take", Math.Max(1, take));

        var results = new List<ActiveWorkOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<long> AddAsync(ActiveWorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO active_work_order (
                device_code,
                site_name_snapshot,
                site_alias_snapshot,
                product_access_number_snapshot,
                current_fault_code,
                current_fault_reason,
                dispatch_source,
                status,
                first_dispatched_at,
                latest_exception_at,
                latest_notification_at,
                maintenance_unit_snapshot,
                maintainer_name_snapshot,
                maintainer_phone_snapshot,
                dispatch_remark_snapshot,
                recovery_confirmation_mode_snapshot,
                allow_recovery_auto_archive_snapshot,
                last_notification_summary,
                recovery_source,
                recovery_summary,
                closing_remark,
                product_status_snapshot,
                arrears_amount_snapshot,
                recovered_at,
                recovery_confirmed_at,
                closed_archived_at,
                created_at,
                updated_at
            )
            VALUES (
                $deviceCode,
                $siteNameSnapshot,
                $siteAliasSnapshot,
                $productAccessNumberSnapshot,
                $currentFaultCode,
                $currentFaultReason,
                $dispatchSource,
                $status,
                $firstDispatchedAt,
                $latestExceptionAt,
                $latestNotificationAt,
                $maintenanceUnitSnapshot,
                $maintainerNameSnapshot,
                $maintainerPhoneSnapshot,
                $dispatchRemarkSnapshot,
                $recoveryConfirmationModeSnapshot,
                $allowRecoveryAutoArchiveSnapshot,
                $lastNotificationSummary,
                $recoverySource,
                $recoverySummary,
                $closingRemark,
                $productStatusSnapshot,
                $arrearsAmountSnapshot,
                $recoveredAt,
                $recoveryConfirmedAt,
                $closedArchivedAt,
                $createdAt,
                $updatedAt
            );

            SELECT last_insert_rowid();
            """;

        Bind(command, workOrder);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAsync(ActiveWorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE active_work_order
            SET
                device_code = $deviceCode,
                site_name_snapshot = $siteNameSnapshot,
                site_alias_snapshot = $siteAliasSnapshot,
                product_access_number_snapshot = $productAccessNumberSnapshot,
                current_fault_code = $currentFaultCode,
                current_fault_reason = $currentFaultReason,
                dispatch_source = $dispatchSource,
                status = $status,
                first_dispatched_at = $firstDispatchedAt,
                latest_exception_at = $latestExceptionAt,
                latest_notification_at = $latestNotificationAt,
                maintenance_unit_snapshot = $maintenanceUnitSnapshot,
                maintainer_name_snapshot = $maintainerNameSnapshot,
                maintainer_phone_snapshot = $maintainerPhoneSnapshot,
                dispatch_remark_snapshot = $dispatchRemarkSnapshot,
                recovery_confirmation_mode_snapshot = $recoveryConfirmationModeSnapshot,
                allow_recovery_auto_archive_snapshot = $allowRecoveryAutoArchiveSnapshot,
                last_notification_summary = $lastNotificationSummary,
                recovery_source = $recoverySource,
                recovery_summary = $recoverySummary,
                closing_remark = $closingRemark,
                product_status_snapshot = $productStatusSnapshot,
                arrears_amount_snapshot = $arrearsAmountSnapshot,
                recovered_at = $recoveredAt,
                recovery_confirmed_at = $recoveryConfirmedAt,
                closed_archived_at = $closedArchivedAt,
                created_at = $createdAt,
                updated_at = $updatedAt
            WHERE work_order_id = $workOrderId;
            """;

        command.Parameters.AddWithValue("$workOrderId", workOrder.WorkOrderId);
        Bind(command, workOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectSql =
        """
        SELECT
            work_order_id,
            device_code,
            site_name_snapshot,
            site_alias_snapshot,
            product_access_number_snapshot,
            current_fault_code,
            current_fault_reason,
            dispatch_source,
            status,
            first_dispatched_at,
            latest_exception_at,
            latest_notification_at,
            maintenance_unit_snapshot,
            maintainer_name_snapshot,
            maintainer_phone_snapshot,
            dispatch_remark_snapshot,
            recovery_confirmation_mode_snapshot,
            allow_recovery_auto_archive_snapshot,
            last_notification_summary,
            recovery_source,
            recovery_summary,
            closing_remark,
            product_status_snapshot,
            arrears_amount_snapshot,
            recovered_at,
            recovery_confirmed_at,
            closed_archived_at,
            created_at,
            updated_at
        FROM active_work_order
        """;

    private static void Bind(SqliteCommand command, ActiveWorkOrder workOrder)
    {
        command.Parameters.AddWithValue("$deviceCode", workOrder.DeviceCode);
        command.Parameters.AddWithValue("$siteNameSnapshot", workOrder.SiteNameSnapshot);
        command.Parameters.AddWithValue("$siteAliasSnapshot", (object?)workOrder.SiteAliasSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$productAccessNumberSnapshot", (object?)workOrder.ProductAccessNumberSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentFaultCode", workOrder.CurrentFaultCode);
        command.Parameters.AddWithValue("$currentFaultReason", workOrder.CurrentFaultReason);
        command.Parameters.AddWithValue("$dispatchSource", (int)workOrder.DispatchSource);
        command.Parameters.AddWithValue("$status", (int)workOrder.Status);
        command.Parameters.AddWithValue("$firstDispatchedAt", ToDbValue(workOrder.FirstDispatchedAt));
        command.Parameters.AddWithValue("$latestExceptionAt", workOrder.LatestExceptionAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$latestNotificationAt", ToDbValue(workOrder.LatestNotificationAt));
        command.Parameters.AddWithValue("$maintenanceUnitSnapshot", (object?)workOrder.MaintenanceUnitSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerNameSnapshot", (object?)workOrder.MaintainerNameSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerPhoneSnapshot", (object?)workOrder.MaintainerPhoneSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$dispatchRemarkSnapshot", (object?)workOrder.DispatchRemarkSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$recoveryConfirmationModeSnapshot", (int)workOrder.RecoveryConfirmationModeSnapshot);
        command.Parameters.AddWithValue("$allowRecoveryAutoArchiveSnapshot", workOrder.AllowRecoveryAutoArchiveSnapshot ? 1 : 0);
        command.Parameters.AddWithValue("$lastNotificationSummary", (object?)workOrder.LastNotificationSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$recoverySource", workOrder.RecoverySource is null ? DBNull.Value : (int)workOrder.RecoverySource.Value);
        command.Parameters.AddWithValue("$recoverySummary", (object?)workOrder.RecoverySummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$closingRemark", (object?)workOrder.ClosingRemark ?? DBNull.Value);
        command.Parameters.AddWithValue("$productStatusSnapshot", (object?)workOrder.ProductStatusSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$arrearsAmountSnapshot", workOrder.ArrearsAmountSnapshot is null ? DBNull.Value : workOrder.ArrearsAmountSnapshot.Value);
        command.Parameters.AddWithValue("$recoveredAt", ToDbValue(workOrder.RecoveredAt));
        command.Parameters.AddWithValue("$recoveryConfirmedAt", ToDbValue(workOrder.RecoveryConfirmedAt));
        command.Parameters.AddWithValue("$closedArchivedAt", ToDbValue(workOrder.ClosedArchivedAt));
        command.Parameters.AddWithValue("$createdAt", workOrder.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", workOrder.UpdatedAt.UtcDateTime.ToString("O"));
    }

    private static ActiveWorkOrder Map(SqliteDataReader reader)
    {
        return new ActiveWorkOrder
        {
            WorkOrderId = reader.GetInt64(0),
            DeviceCode = reader.GetString(1),
            SiteNameSnapshot = reader.GetString(2),
            SiteAliasSnapshot = reader.IsDBNull(3) ? null : reader.GetString(3),
            ProductAccessNumberSnapshot = reader.IsDBNull(4) ? null : reader.GetString(4),
            CurrentFaultCode = reader.GetString(5),
            CurrentFaultReason = reader.GetString(6),
            DispatchSource = (DispatchSource)reader.GetInt64(7),
            Status = (DispatchWorkOrderStatus)reader.GetInt64(8),
            FirstDispatchedAt = ParseDateTimeOffset(reader, 9),
            LatestExceptionAt = DateTimeOffset.Parse(reader.GetString(10)),
            LatestNotificationAt = ParseDateTimeOffset(reader, 11),
            MaintenanceUnitSnapshot = reader.IsDBNull(12) ? null : reader.GetString(12),
            MaintainerNameSnapshot = reader.IsDBNull(13) ? null : reader.GetString(13),
            MaintainerPhoneSnapshot = reader.IsDBNull(14) ? null : reader.GetString(14),
            DispatchRemarkSnapshot = reader.IsDBNull(15) ? null : reader.GetString(15),
            RecoveryConfirmationModeSnapshot = (RecoveryConfirmationMode)reader.GetInt64(16),
            AllowRecoveryAutoArchiveSnapshot = reader.GetInt64(17) == 1,
            LastNotificationSummary = reader.IsDBNull(18) ? null : reader.GetString(18),
            RecoverySource = reader.IsDBNull(19) ? null : (RecoverySource)reader.GetInt64(19),
            RecoverySummary = reader.IsDBNull(20) ? null : reader.GetString(20),
            ClosingRemark = reader.IsDBNull(21) ? null : reader.GetString(21),
            ProductStatusSnapshot = reader.IsDBNull(22) ? null : reader.GetString(22),
            ArrearsAmountSnapshot = reader.IsDBNull(23) ? null : Convert.ToDecimal(reader.GetDouble(23)),
            RecoveredAt = ParseDateTimeOffset(reader, 24),
            RecoveryConfirmedAt = ParseDateTimeOffset(reader, 25),
            ClosedArchivedAt = ParseDateTimeOffset(reader, 26),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(27)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(28))
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
