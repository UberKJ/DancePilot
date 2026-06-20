using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Storage;

public sealed class SqliteConnectionFactory
{
    private readonly DatabaseOptions _options;

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        _options = options;
    }

    public SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        return new SqliteConnection(builder.ConnectionString);
    }
}
