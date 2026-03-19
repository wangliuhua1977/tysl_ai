using Microsoft.Data.Sqlite;
using Tysl.Ai.Core.Enums;
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

        await using var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS site_profile (
                id TEXT PRIMARY KEY NOT NULL,
                device_code TEXT NOT NULL,
                device_name TEXT NOT NULL,
                alias TEXT NULL,
                remark TEXT NULL,
                is_monitored INTEGER NOT NULL,
                longitude REAL NOT NULL,
                latitude REAL NOT NULL,
                address_text TEXT NULL,
                product_access_number TEXT NULL,
                maintenance_unit TEXT NULL,
                maintainer_name TEXT NULL,
                maintainer_phone TEXT NULL,
                demo_status INTEGER NOT NULL,
                demo_dispatch_status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;

        await createTableCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM site_profile;";
        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (existingCount > 0)
        {
            return;
        }

        foreach (var seedSite in GetSeedProfiles())
        {
            await InsertSeedAsync(connection, seedSite, cancellationToken);
        }
    }

    private static async Task InsertSeedAsync(
        SqliteConnection connection,
        SiteProfile seedSite,
        CancellationToken cancellationToken)
    {
        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
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

        insertCommand.Parameters.AddWithValue("$id", seedSite.Id.ToString());
        insertCommand.Parameters.AddWithValue("$deviceCode", seedSite.DeviceCode);
        insertCommand.Parameters.AddWithValue("$deviceName", seedSite.DeviceName);
        insertCommand.Parameters.AddWithValue("$alias", (object?)seedSite.Alias ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$remark", (object?)seedSite.Remark ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$isMonitored", seedSite.IsMonitored ? 1 : 0);
        insertCommand.Parameters.AddWithValue("$longitude", seedSite.Longitude);
        insertCommand.Parameters.AddWithValue("$latitude", seedSite.Latitude);
        insertCommand.Parameters.AddWithValue("$addressText", (object?)seedSite.AddressText ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$productAccessNumber", (object?)seedSite.ProductAccessNumber ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintenanceUnit", (object?)seedSite.MaintenanceUnit ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintainerName", (object?)seedSite.MaintainerName ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$maintainerPhone", (object?)seedSite.MaintainerPhone ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("$demoStatus", (int)seedSite.DemoStatus);
        insertCommand.Parameters.AddWithValue("$demoDispatchStatus", (int)seedSite.DemoDispatchStatus);
        insertCommand.Parameters.AddWithValue("$createdAt", seedSite.CreatedAt.UtcDateTime.ToString("O"));
        insertCommand.Parameters.AddWithValue("$updatedAt", seedSite.UpdatedAt.UtcDateTime.ToString("O"));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<SiteProfile> GetSeedProfiles()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-7);

        return
        [
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A001"),
                DeviceCode = "SX-A-001",
                DeviceName = "环城北路隧道东口枪机",
                Alias = "环城北路隧道",
                Remark = "夜间车流量高，保留为首屏故障演示点。",
                IsMonitored = true,
                Longitude = 120.5958,
                Latitude = 30.0125,
                AddressText = "绍兴越城区环城北路隧道东口",
                ProductAccessNumber = "3306020001001",
                MaintenanceUnit = "越城北片区维护组",
                MaintainerName = "周工",
                MaintainerPhone = "13800000001",
                DemoStatus = PointDemoStatus.Fault,
                DemoDispatchStatus = DispatchDemoStatus.None,
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddHours(3)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A002"),
                DeviceCode = "SX-B-003",
                DeviceName = "高铁站前广场全景枪机",
                Alias = "高铁站前广场",
                Remark = "模拟已派单点位。",
                IsMonitored = true,
                Longitude = 120.6462,
                Latitude = 30.0442,
                AddressText = "绍兴北站南广场",
                ProductAccessNumber = "3306020001002",
                MaintenanceUnit = "客运枢纽联保组",
                MaintainerName = "沈工",
                MaintainerPhone = "13800000002",
                DemoStatus = PointDemoStatus.Warning,
                DemoDispatchStatus = DispatchDemoStatus.Dispatched,
                CreatedAt = createdAt.AddMinutes(20),
                UpdatedAt = createdAt.AddHours(2)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A003"),
                DeviceCode = "SX-C-007",
                DeviceName = "滨江公园南栈桥水岸枪机",
                Alias = "滨江公园南栈桥",
                Remark = "模拟冷却中异常点位。",
                IsMonitored = true,
                Longitude = 120.6211,
                Latitude = 29.9852,
                AddressText = "滨江公园南栈桥亲水平台",
                ProductAccessNumber = "3306020001003",
                MaintenanceUnit = "滨江巡检组",
                MaintainerName = "何工",
                MaintainerPhone = "13800000003",
                DemoStatus = PointDemoStatus.Warning,
                DemoDispatchStatus = DispatchDemoStatus.Cooling,
                CreatedAt = createdAt.AddMinutes(40),
                UpdatedAt = createdAt.AddHours(5)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A004"),
                DeviceCode = "SX-D-005",
                DeviceName = "区政务中心东门门岗枪机",
                Alias = "政务中心东门",
                Remark = "稳定点位。",
                IsMonitored = true,
                Longitude = 120.5739,
                Latitude = 30.0288,
                AddressText = "越城区政务中心东门",
                ProductAccessNumber = "3306020001004",
                MaintenanceUnit = "政务中心值守组",
                MaintainerName = "李工",
                MaintainerPhone = "13800000004",
                DemoStatus = PointDemoStatus.Normal,
                DemoDispatchStatus = DispatchDemoStatus.None,
                CreatedAt = createdAt.AddHours(1),
                UpdatedAt = createdAt.AddHours(1)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A005"),
                DeviceCode = "SX-E-009",
                DeviceName = "江心洲泵站周界球机",
                Alias = null,
                Remark = "演示别名为空时回退设备名。",
                IsMonitored = true,
                Longitude = 120.5534,
                Latitude = 29.9981,
                AddressText = "江心洲泵站外场",
                ProductAccessNumber = "3306020001005",
                MaintenanceUnit = "水务保障组",
                MaintainerName = "徐工",
                MaintainerPhone = "13800000005",
                DemoStatus = PointDemoStatus.Idle,
                DemoDispatchStatus = DispatchDemoStatus.None,
                CreatedAt = createdAt.AddHours(2),
                UpdatedAt = createdAt.AddHours(6)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A006"),
                DeviceCode = "SX-F-012",
                DeviceName = "科创园一期西门半球",
                Alias = "科创园一期西门",
                Remark = "未纳入监测的演示点位。",
                IsMonitored = false,
                Longitude = 120.6678,
                Latitude = 30.0084,
                AddressText = "科创园一期西门",
                ProductAccessNumber = "3306020001006",
                MaintenanceUnit = "园区联保组",
                MaintainerName = "顾工",
                MaintainerPhone = "13800000006",
                DemoStatus = PointDemoStatus.Normal,
                DemoDispatchStatus = DispatchDemoStatus.None,
                CreatedAt = createdAt.AddHours(3),
                UpdatedAt = createdAt.AddHours(3)
            },
            new SiteProfile
            {
                Id = Guid.Parse("6A7F596C-9F93-4EAB-A381-0C39F4F3A007"),
                DeviceCode = "SX-G-016",
                DeviceName = "迪荡湖公园北侧枪机",
                Alias = "迪荡湖公园北侧",
                Remark = "用于补齐常态监测点位分布。",
                IsMonitored = true,
                Longitude = 120.6354,
                Latitude = 30.0219,
                AddressText = "迪荡湖公园北侧步道",
                ProductAccessNumber = "3306020001007",
                MaintenanceUnit = "景观带保障组",
                MaintainerName = "王工",
                MaintainerPhone = "13800000007",
                DemoStatus = PointDemoStatus.Normal,
                DemoDispatchStatus = DispatchDemoStatus.None,
                CreatedAt = createdAt.AddHours(4),
                UpdatedAt = createdAt.AddHours(4)
            }
        ];
    }
}
