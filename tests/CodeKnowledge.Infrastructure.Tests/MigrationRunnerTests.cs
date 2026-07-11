using CodeKnowledge.Core.Errors;
using CodeKnowledge.Infrastructure.Database;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class MigrationRunnerTests
{
    private static long Scalar(TestDatabase db, string sql)
    {
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void Apply_creates_v1_schema_on_fresh_database()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);

        Assert.Equal(1, Scalar(db, "PRAGMA user_version;"));
        Assert.Equal(1, Scalar(db,
            "SELECT COUNT(*) FROM sqlite_master WHERE name = 'knowledge_fts';"));
        foreach (var table in new[]
        {
            "projects", "knowledge", "knowledge_versions", "facts", "fact_evidence",
            "inferences", "inference_evidence", "evidence", "relations",
        })
        {
            Assert.Equal(1, Scalar(db,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
        }
    }

    [Fact]
    public void Apply_is_idempotent()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);
        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.Equal(1, Scalar(db, "PRAGMA user_version;"));
    }

    [Fact]
    public void Apply_creates_backup_before_migrating_existing_database()
    {
        using var db = new TestDatabase();
        using (var connection = db.Factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE pre_existing (id INTEGER);";
            command.ExecuteNonQuery();
        }

        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.True(File.Exists(db.DbPath + ".bak-0"));
    }

    [Fact]
    public void Apply_skips_backup_for_fresh_database()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.False(File.Exists(db.DbPath + ".bak-0"));
    }

    [Fact]
    public void Apply_rejects_newer_schema_version()
    {
        using var db = new TestDatabase();
        using (var connection = db.Factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => MigrationRunner.Apply(db.Factory, db.DbPath));
        Assert.Equal(CodeKnowledgeException.SchemaVersionUnsupported, exception.Code);
    }

    [Fact]
    public void Open_applies_required_pragmas()
    {
        using var db = new TestDatabase().Migrated();
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string)command.ExecuteScalar()!);
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }
}
