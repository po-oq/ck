using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Database;

public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Mode=ReadWriteCreate");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
