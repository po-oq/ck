using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class TestDatabase : IDisposable
{
    public string Directory { get; } =
        Path.Combine(Path.GetTempPath(), $"ck-p1-{Guid.NewGuid():N}");

    public string DbPath { get; }
    public SqliteConnectionFactory Factory { get; }

    public TestDatabase()
    {
        System.IO.Directory.CreateDirectory(Directory);
        DbPath = Path.Combine(Directory, "knowledge.db");
        Factory = new SqliteConnectionFactory(DbPath);
    }

    public TestDatabase Migrated()
    {
        MigrationRunner.Apply(Factory, DbPath);
        return this;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
    }
}
