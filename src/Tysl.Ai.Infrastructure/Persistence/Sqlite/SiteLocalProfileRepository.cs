using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SiteLocalProfileRepository : ISiteLocalProfileRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SiteLocalProfileRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SiteLocalProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
            FROM site_local_profile
            ORDER BY created_at ASC;
            """;

        var results = new List<SiteLocalProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<SiteLocalProfile?> GetByDeviceCodeAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
            FROM site_local_profile
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

    public async Task UpsertAsync(SiteLocalProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
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
            )
            ON CONFLICT(device_code) DO UPDATE SET
                alias = excluded.alias,
                remark = excluded.remark,
                is_monitored = excluded.is_monitored,
                manual_longitude = excluded.manual_longitude,
                manual_latitude = excluded.manual_latitude,
                address_text = excluded.address_text,
                product_access_number = excluded.product_access_number,
                maintenance_unit = excluded.maintenance_unit,
                maintainer_name = excluded.maintainer_name,
                maintainer_phone = excluded.maintainer_phone,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at;
            """;

        command.Parameters.AddWithValue("$deviceCode", profile.DeviceCode);
        command.Parameters.AddWithValue("$alias", (object?)profile.Alias ?? DBNull.Value);
        command.Parameters.AddWithValue("$remark", (object?)profile.Remark ?? DBNull.Value);
        command.Parameters.AddWithValue("$isMonitored", profile.IsMonitored ? 1 : 0);
        command.Parameters.AddWithValue("$manualLongitude", (object?)profile.ManualLongitude ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualLatitude", (object?)profile.ManualLatitude ?? DBNull.Value);
        command.Parameters.AddWithValue("$addressText", (object?)profile.AddressText ?? DBNull.Value);
        command.Parameters.AddWithValue("$productAccessNumber", (object?)profile.ProductAccessNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintenanceUnit", (object?)profile.MaintenanceUnit ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerName", (object?)profile.MaintainerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerPhone", (object?)profile.MaintainerPhone ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", profile.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", profile.UpdatedAt.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SiteLocalProfile Map(SqliteDataReader reader)
    {
        return new SiteLocalProfile
        {
            DeviceCode = reader.GetString(0),
            Alias = reader.IsDBNull(1) ? null : reader.GetString(1),
            Remark = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsMonitored = reader.GetInt64(3) == 1,
            ManualLongitude = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            ManualLatitude = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            AddressText = reader.IsDBNull(6) ? null : reader.GetString(6),
            ProductAccessNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
            MaintenanceUnit = reader.IsDBNull(8) ? null : reader.GetString(8),
            MaintainerName = reader.IsDBNull(9) ? null : reader.GetString(9),
            MaintainerPhone = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(11)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(12))
        };
    }
}
