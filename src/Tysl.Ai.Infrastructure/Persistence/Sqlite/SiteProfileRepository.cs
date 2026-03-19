using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
using Tysl.Ai.Core.Interfaces;
using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SiteProfileRepository : ISiteProfileRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SiteProfileRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SiteProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                device_code,
                device_name,
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
                demo_status,
                demo_dispatch_status,
                created_at,
                updated_at
            FROM site_profile
            ORDER BY created_at ASC;
            """;

        var results = new List<SiteProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<SiteProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                device_code,
                device_name,
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
                demo_status,
                demo_dispatch_status,
                created_at,
                updated_at
            FROM site_profile
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task CreateAsync(SiteProfile siteProfile, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO site_profile (
                id,
                device_code,
                device_name,
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
                demo_status,
                demo_dispatch_status,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $deviceCode,
                $deviceName,
                $alias,
                $remark,
                $isMonitored,
                $longitude,
                $latitude,
                $addressText,
                $productAccessNumber,
                $maintenanceUnit,
                $maintainerName,
                $maintainerPhone,
                $demoStatus,
                $demoDispatchStatus,
                $createdAt,
                $updatedAt
            );
            """;

        Bind(command, siteProfile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(SiteProfile siteProfile, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE site_profile
            SET
                device_code = $deviceCode,
                device_name = $deviceName,
                alias = $alias,
                remark = $remark,
                is_monitored = $isMonitored,
                longitude = $longitude,
                latitude = $latitude,
                address_text = $addressText,
                product_access_number = $productAccessNumber,
                maintenance_unit = $maintenanceUnit,
                maintainer_name = $maintainerName,
                maintainer_phone = $maintainerPhone,
                demo_status = $demoStatus,
                demo_dispatch_status = $demoDispatchStatus,
                created_at = $createdAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;

        Bind(command, siteProfile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(SqliteCommand command, SiteProfile siteProfile)
    {
        command.Parameters.AddWithValue("$id", siteProfile.Id.ToString());
        command.Parameters.AddWithValue("$deviceCode", siteProfile.DeviceCode);
        command.Parameters.AddWithValue("$deviceName", siteProfile.DeviceName);
        command.Parameters.AddWithValue("$alias", (object?)siteProfile.Alias ?? DBNull.Value);
        command.Parameters.AddWithValue("$remark", (object?)siteProfile.Remark ?? DBNull.Value);
        command.Parameters.AddWithValue("$isMonitored", siteProfile.IsMonitored ? 1 : 0);
        command.Parameters.AddWithValue("$longitude", siteProfile.Longitude);
        command.Parameters.AddWithValue("$latitude", siteProfile.Latitude);
        command.Parameters.AddWithValue("$addressText", (object?)siteProfile.AddressText ?? DBNull.Value);
        command.Parameters.AddWithValue("$productAccessNumber", (object?)siteProfile.ProductAccessNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintenanceUnit", (object?)siteProfile.MaintenanceUnit ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerName", (object?)siteProfile.MaintainerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$maintainerPhone", (object?)siteProfile.MaintainerPhone ?? DBNull.Value);
        command.Parameters.AddWithValue("$demoStatus", (int)siteProfile.DemoStatus);
        command.Parameters.AddWithValue("$demoDispatchStatus", (int)siteProfile.DemoDispatchStatus);
        command.Parameters.AddWithValue("$createdAt", siteProfile.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", siteProfile.UpdatedAt.UtcDateTime.ToString("O"));
    }

    private static SiteProfile Map(SqliteDataReader reader)
    {
        return new SiteProfile
        {
            Id = Guid.Parse(reader.GetString(0)),
            DeviceCode = reader.GetString(1),
            DeviceName = reader.GetString(2),
            Alias = reader.IsDBNull(3) ? null : reader.GetString(3),
            Remark = reader.IsDBNull(4) ? null : reader.GetString(4),
            IsMonitored = reader.GetInt64(5) == 1,
            Longitude = reader.GetDouble(6),
            Latitude = reader.GetDouble(7),
            AddressText = reader.IsDBNull(8) ? null : reader.GetString(8),
            ProductAccessNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
            MaintenanceUnit = reader.IsDBNull(10) ? null : reader.GetString(10),
            MaintainerName = reader.IsDBNull(11) ? null : reader.GetString(11),
            MaintainerPhone = reader.IsDBNull(12) ? null : reader.GetString(12),
            DemoStatus = (PointDemoStatus)reader.GetInt32(13),
            DemoDispatchStatus = (DispatchDemoStatus)reader.GetInt32(14),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(15)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(16))
        };
    }
}
