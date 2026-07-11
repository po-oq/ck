using CodeKnowledge.Core.Errors;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Database;

public static class MigrationRunner
{
    public const int CurrentVersion = 1;

    private const string V1Schema = """
        CREATE TABLE projects (
            project_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            repository_root TEXT NOT NULL,
            remote_url TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE knowledge (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL REFERENCES projects(project_id),
            canonical_key TEXT NOT NULL,
            title TEXT NOT NULL,
            current_version_id TEXT NULL REFERENCES knowledge_versions(id),
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE (project_id, canonical_key)
        );
        CREATE INDEX idx_knowledge_project ON knowledge(project_id);

        CREATE TABLE knowledge_versions (
            id TEXT PRIMARY KEY,
            knowledge_id TEXT NOT NULL REFERENCES knowledge(id) ON DELETE CASCADE,
            commit_hash TEXT NOT NULL,
            branch_name TEXT NULL,
            original_question TEXT NOT NULL,
            summary TEXT NOT NULL,
            confidence TEXT NOT NULL CHECK (confidence IN ('high', 'medium', 'low')),
            tags TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            created_by TEXT NOT NULL DEFAULT '',
            retain INTEGER NOT NULL DEFAULT 0,
            retain_reason TEXT NULL
        );
        CREATE INDEX idx_versions_knowledge ON knowledge_versions(knowledge_id);

        CREATE TABLE evidence (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL,
            symbol_id TEXT NULL,
            symbol_name TEXT NOT NULL,
            symbol_kind TEXT NULL,
            signature TEXT NULL,
            start_line INTEGER NULL,
            end_line INTEGER NULL,
            commit_hash TEXT NOT NULL,
            file_hash TEXT NOT NULL,
            symbol_hash TEXT NULL,
            reason TEXT NULL
        );
        CREATE INDEX idx_evidence_version ON evidence(knowledge_version_id);

        CREATE TABLE facts (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            text TEXT NOT NULL,
            sort_order INTEGER NOT NULL
        );
        CREATE INDEX idx_facts_version ON facts(knowledge_version_id);

        CREATE TABLE fact_evidence (
            fact_id TEXT NOT NULL REFERENCES facts(id) ON DELETE CASCADE,
            evidence_id TEXT NOT NULL REFERENCES evidence(id) ON DELETE CASCADE,
            PRIMARY KEY (fact_id, evidence_id)
        );

        CREATE TABLE inferences (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            text TEXT NOT NULL,
            confidence TEXT NOT NULL CHECK (confidence IN ('high', 'medium', 'low')),
            reason TEXT NOT NULL,
            sort_order INTEGER NOT NULL
        );
        CREATE INDEX idx_inferences_version ON inferences(knowledge_version_id);

        CREATE TABLE inference_evidence (
            inference_id TEXT NOT NULL REFERENCES inferences(id) ON DELETE CASCADE,
            evidence_id TEXT NOT NULL REFERENCES evidence(id) ON DELETE CASCADE,
            PRIMARY KEY (inference_id, evidence_id)
        );

        CREATE TABLE relations (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            from_symbol TEXT NOT NULL,
            to_symbol TEXT NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN (
                'calls', 'implements', 'inherits', 'reads', 'writes',
                'publishes', 'subscribes', 'configured-by', 'tested-by'))
        );
        CREATE INDEX idx_relations_version ON relations(knowledge_version_id);

        CREATE VIRTUAL TABLE knowledge_fts USING fts5(
            title, original_question, summary, facts, inferences, tags,
            symbol_names, symbol_ids, file_paths, canonical_key,
            knowledge_id UNINDEXED, project_id UNINDEXED,
            tokenize = "trigram"
        );
        """;

    public static void Apply(SqliteConnectionFactory factory, string databasePath)
    {
        using var connection = factory.Open();

        var currentVersion = GetUserVersion(connection);
        if (currentVersion == CurrentVersion)
            return;
        if (currentVersion > CurrentVersion)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.SchemaVersionUnsupported,
                $"Database schema version {currentVersion} is newer than supported version {CurrentVersion}.");

        var isFreshDatabase = currentVersion == 0 && CountObjects(connection) == 0;
        if (!isFreshDatabase)
            CreateBackup(connection, $"{databasePath}.bak-{currentVersion}");

        using var transaction = connection.BeginTransaction(deferred: false);
        // 排他トランザクション開始後に再確認し、同時起動した他プロセスの適用済みを検知する
        if (GetUserVersion(connection) == CurrentVersion)
            return;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = V1Schema;
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version = {CurrentVersion};";
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateBackup(SqliteConnection connection, string backupPath)
    {
        // File.Copy はWALに残る未チェックポイントのコミット済みデータを取りこぼすため、
        // VACUUM INTO でWAL状態に関わらず一貫したスナップショットを生成する。
        // VACUUM INTO は既存ファイルへの書き込みを拒否するので先に削除する。
        // (VACUUM はトランザクション内で実行できないため BeginTransaction より前に呼ぶこと)
        File.Delete(backupPath);
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO @path;";
        command.Parameters.AddWithValue("@path", backupPath);
        command.ExecuteNonQuery();
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long CountObjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
