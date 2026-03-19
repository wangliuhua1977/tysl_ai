using Microsoft.Data.Sqlite;

namespace Tysl.Ai.Infrastructure.Persistence.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;

        var directoryPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
