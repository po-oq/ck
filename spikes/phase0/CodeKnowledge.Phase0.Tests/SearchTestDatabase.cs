using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0.Tests;

internal static class SearchTestDatabase
{
    public static SqliteConnection Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE knowledge_records(id INTEGER PRIMARY KEY, title TEXT NOT NULL);
            CREATE VIRTUAL TABLE knowledge_fts USING fts5(
                id UNINDEXED,
                title,
                tokenize='trigram'
            );
            INSERT INTO knowledge_records(id, title) VALUES
                (1, '注文完了メール仕様'),
                (2, '100%_safe sui-memory'),
                (4, '詳細設計'),
                (5, 'wildcard decoy'),
                (6, 'path\segment');
            INSERT INTO knowledge_fts(id, title) VALUES
                (1, '注文完了メール仕様'),
                (2, '100%_safe sui-memory'),
                (3, '配送完了メール');
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
