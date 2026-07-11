using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0;

internal static class SqliteProbe
{
    private static readonly Version MinimumSqliteVersion = new(3, 34, 0);

    public static ProbeReport Run(string databasePath)
    {
        var checks = new List<ProbeCheck>();
        var details = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var connection = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadWriteCreate");
            connection.Open();
            Execute(connection, "PRAGMA foreign_keys = ON;");
            Execute(connection, "PRAGMA busy_timeout = 5000;");

            var journalMode = Scalar(connection, "PRAGMA journal_mode = WAL;");
            var sqliteVersion = Scalar(connection, "SELECT sqlite_version();");
            Execute(
                connection,
                "CREATE TABLE knowledge_records(id INTEGER PRIMARY KEY, title TEXT NOT NULL);");
            Execute(
                connection,
                "CREATE VIRTUAL TABLE knowledge_fts USING fts5(id UNINDEXED, title, tokenize='trigram');");

            InsertRecord(connection, 1, "注文完了メール仕様");
            InsertRecord(connection, 2, "確認事項");

            var busyTimeout = Scalar(connection, "PRAGMA busy_timeout;");
            var foreignKeys = Scalar(connection, "PRAGMA foreign_keys;");
            details.Add("sqliteVersion", sqliteVersion);
            details.Add("journalMode", journalMode);
            details.Add("busyTimeout", busyTimeout);
            details.Add("foreignKeys", foreignKeys);

            AddCheck(
                checks,
                "sqlite.version",
                Version.TryParse(sqliteVersion, out var version) && version >= MinimumSqliteVersion,
                $"SQLite {sqliteVersion} must be at least {MinimumSqliteVersion}.");
            AddCheck(checks, "sqlite.fts5-trigram", true, "FTS5 trigram table was created.");
            AddSearchCheck(connection, checks, "search.fts-mail", ["メール"], [1]);
            AddSearchCheck(connection, checks, "search.like-specification", ["仕様"], [1]);
            AddSearchCheck(connection, checks, "search.like-confirmation", ["確認"], [2]);
            AddSearchCheck(connection, checks, "search.mixed", ["メール", "仕様"], [1]);
            AddCheck(
                checks,
                "sqlite.journal-mode",
                string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase),
                $"journal_mode was {journalMode}; expected wal.");
            AddCheck(
                checks,
                "sqlite.busy-timeout",
                string.Equals(busyTimeout, "5000", StringComparison.Ordinal),
                $"busy_timeout was {busyTimeout}; expected 5000.");
            AddCheck(
                checks,
                "sqlite.foreign-keys",
                string.Equals(foreignKeys, "1", StringComparison.Ordinal),
                $"foreign_keys was {foreignKeys}; expected 1.");
        }
        catch (SqliteException exception)
        {
            checks.Clear();
            checks.Add(new("sqlite.runtime", false, $"{exception.GetType().Name}: {exception.Message}"));
        }

        return new(
            "self-check",
            checks.All(static check => check.Passed) ? "ok" : "failed",
            typeof(SqliteProbe).Assembly.GetName().Version?.ToString() ?? "unknown",
            checks,
            details);
    }

    private static void AddSearchCheck(
        SqliteConnection connection,
        ICollection<ProbeCheck> checks,
        string id,
        IReadOnlyCollection<string> terms,
        IReadOnlyCollection<long> expectedIds)
    {
        var actualIds = SearchProbe.Search(connection, terms).Order().ToArray();
        var passed = actualIds.SequenceEqual(expectedIds.Order());
        AddCheck(
            checks,
            id,
            passed,
            passed
                ? "Search returned the expected records."
                : $"Search returned [{string.Join(",", actualIds)}]; expected [{string.Join(",", expectedIds)}].");
    }

    private static void AddCheck(
        ICollection<ProbeCheck> checks,
        string id,
        bool passed,
        string message) =>
        checks.Add(new(id, passed, message));

    private static void InsertRecord(SqliteConnection connection, long id, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO knowledge_records(id, title) VALUES ($id, $title);
            INSERT INTO knowledge_fts(id, title) VALUES ($id, $title);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Scalar query returned null: {sql}");
    }
}
