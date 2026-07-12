using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Stores;

public sealed class SqliteKnowledgeStore(SqliteConnectionFactory factory) : IKnowledgeStore
{
    public SaveVersionResult SaveVersion(VersionToSave version)
    {
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var knowledgeId = FindKnowledgeId(connection, transaction, version.ProjectId, version.CanonicalKey);
        var createdNew = knowledgeId is null;
        if (createdNew)
        {
            knowledgeId = Guid.CreateVersion7().ToString("N");
            Execute(connection, transaction, """
                INSERT INTO knowledge (id, project_id, canonical_key, title, current_version_id, created_at, updated_at)
                VALUES (@id, @project, @key, @title, NULL, @now, @now);
                """,
                ("@id", knowledgeId), ("@project", version.ProjectId),
                ("@key", version.CanonicalKey), ("@title", version.Title), ("@now", now));
        }

        var versionId = Guid.CreateVersion7().ToString("N");
        Execute(connection, transaction, """
            INSERT INTO knowledge_versions
                (id, knowledge_id, commit_hash, branch_name, original_question, summary,
                 confidence, tags, created_at, created_by, retain, retain_reason)
            VALUES (@id, @knowledge, @commit, @branch, @question, @summary,
                    @confidence, @tags, @now, @createdBy, 0, NULL);
            """,
            ("@id", versionId), ("@knowledge", knowledgeId!), ("@commit", version.CommitHash),
            ("@branch", (object?)version.BranchName ?? DBNull.Value),
            ("@question", version.OriginalQuestion), ("@summary", version.Summary),
            ("@confidence", version.Confidence.ToDbValue()), ("@tags", version.Tags),
            ("@now", now), ("@createdBy", version.CreatedBy));

        foreach (var evidence in version.Evidence)
        {
            Execute(connection, transaction, """
                INSERT INTO evidence
                    (id, knowledge_version_id, file_path, symbol_id, symbol_name, symbol_kind,
                     signature, start_line, end_line, commit_hash, file_hash, symbol_hash, reason)
                VALUES (@id, @version, @path, @symbolId, @symbolName, @symbolKind,
                        @signature, @start, @end, @commit, @fileHash, @symbolHash, @reason);
                """,
                ("@id", evidence.Id), ("@version", versionId), ("@path", evidence.FilePath),
                ("@symbolId", (object?)evidence.SymbolId ?? DBNull.Value),
                ("@symbolName", evidence.SymbolName),
                ("@symbolKind", (object?)evidence.SymbolKind ?? DBNull.Value),
                ("@signature", (object?)evidence.Signature ?? DBNull.Value),
                ("@start", (object?)evidence.StartLine ?? DBNull.Value),
                ("@end", (object?)evidence.EndLine ?? DBNull.Value),
                ("@commit", evidence.CommitHash), ("@fileHash", evidence.FileHash),
                ("@symbolHash", (object?)evidence.SymbolHash ?? DBNull.Value),
                ("@reason", (object?)evidence.Reason ?? DBNull.Value));
        }

        var sortOrder = 0;
        foreach (var fact in version.Facts)
        {
            Execute(connection, transaction,
                "INSERT INTO facts (id, knowledge_version_id, text, sort_order) VALUES (@id, @version, @text, @order);",
                ("@id", fact.Id), ("@version", versionId), ("@text", fact.Text), ("@order", sortOrder++));
            foreach (var evidenceId in fact.EvidenceIds)
                Execute(connection, transaction,
                    "INSERT INTO fact_evidence (fact_id, evidence_id) VALUES (@fact, @evidence);",
                    ("@fact", fact.Id), ("@evidence", evidenceId));
        }

        sortOrder = 0;
        foreach (var inference in version.Inferences)
        {
            Execute(connection, transaction, """
                INSERT INTO inferences (id, knowledge_version_id, text, confidence, reason, sort_order)
                VALUES (@id, @version, @text, @confidence, @reason, @order);
                """,
                ("@id", inference.Id), ("@version", versionId), ("@text", inference.Text),
                ("@confidence", inference.Confidence.ToDbValue()),
                ("@reason", inference.Reason), ("@order", sortOrder++));
            foreach (var evidenceId in inference.EvidenceIds)
                Execute(connection, transaction,
                    "INSERT INTO inference_evidence (inference_id, evidence_id) VALUES (@inference, @evidence);",
                    ("@inference", inference.Id), ("@evidence", evidenceId));
        }

        foreach (var relation in version.Relations)
        {
            Execute(connection, transaction, """
                INSERT INTO relations (id, knowledge_version_id, from_symbol, to_symbol, kind)
                VALUES (@id, @version, @from, @to, @kind);
                """,
                ("@id", relation.Id), ("@version", versionId),
                ("@from", relation.FromSymbol), ("@to", relation.ToSymbol), ("@kind", relation.Kind));
        }

        // 昇格 + FTS同期（要件12.1: 同一トランザクション内の明示delete + insert）
        Execute(connection, transaction,
            "UPDATE knowledge SET current_version_id = @version, title = @title, updated_at = @now WHERE id = @id;",
            ("@version", versionId), ("@title", version.Title), ("@now", now), ("@id", knowledgeId!));
        Execute(connection, transaction,
            "DELETE FROM knowledge_fts WHERE knowledge_id = @id;", ("@id", knowledgeId!));
        Execute(connection, transaction, """
            INSERT INTO knowledge_fts
                (title, original_question, summary, facts, inferences, tags,
                 symbol_names, symbol_ids, file_paths, canonical_key, knowledge_id, project_id)
            VALUES (@title, @question, @summary, @facts, @inferences, @tags,
                    @symbolNames, @symbolIds, @filePaths, @key, @knowledge, @project);
            """,
            ("@title", version.Title), ("@question", version.OriginalQuestion),
            ("@summary", version.Summary),
            ("@facts", string.Join('\n', version.Facts.Select(fact => fact.Text))),
            ("@inferences", string.Join('\n',
                version.Inferences.Select(inference => $"{inference.Text}\n{inference.Reason}"))),
            ("@tags", version.Tags),
            ("@symbolNames", string.Join('\n', version.Evidence.Select(e => e.SymbolName))),
            ("@symbolIds", string.Join('\n',
                version.Evidence.Where(e => e.SymbolId is not null).Select(e => e.SymbolId!))),
            ("@filePaths", string.Join('\n', version.Evidence.Select(e => e.FilePath).Distinct())),
            ("@key", version.CanonicalKey), ("@knowledge", knowledgeId!),
            ("@project", version.ProjectId));

        transaction.Commit();
        return new SaveVersionResult(knowledgeId!, versionId, createdNew);
    }

    public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at
            FROM knowledge k
            JOIN knowledge_versions v ON v.id = k.current_version_id
            WHERE k.project_id = @project;
            """;
        command.Parameters.AddWithValue("@project", projectId);
        using var reader = command.ExecuteReader();
        var summaries = new List<KnowledgeSummary>();
        while (reader.Read())
            summaries.Add(ReadSummary(reader));
        return summaries;
    }

    public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
        => throw new NotSupportedException("Implemented in a later task.");

    public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at,
                   bm25(knowledge_fts) AS score,
                   knowledge_fts.title || char(10) || knowledge_fts.original_question || char(10) ||
                   knowledge_fts.summary || char(10) || knowledge_fts.facts || char(10) ||
                   knowledge_fts.inferences || char(10) || knowledge_fts.tags || char(10) ||
                   knowledge_fts.symbol_names || char(10) || knowledge_fts.symbol_ids || char(10) ||
                   knowledge_fts.file_paths || char(10) || knowledge_fts.canonical_key AS search_text
            FROM knowledge_fts
            JOIN knowledge k ON k.id = knowledge_fts.knowledge_id
            JOIN knowledge_versions v ON v.id = k.current_version_id
            WHERE knowledge_fts MATCH @match AND knowledge_fts.project_id = @project
            ORDER BY score
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@match", matchExpression);
        command.Parameters.AddWithValue("@project", projectId);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var hits = new List<FtsSearchHit>();
        while (reader.Read())
            hits.Add(new FtsSearchHit(ReadSummary(reader), reader.GetDouble(7), reader.GetString(8)));
        return hits;
    }

    public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
    {
        if (likePatterns.Count == 0)
            return [];
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        // 要件8.3: LIKEルートは実テーブルを走査し、ファイルパスは対象外とする
        var conditions = string.Join(" OR ", likePatterns.Select(
            (_, index) => $"search_text LIKE @like{index} ESCAPE '\\'"));
        command.CommandText = $"""
            SELECT * FROM (
                SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at,
                       k.title || char(10) || v.original_question || char(10) || v.summary || char(10) ||
                       v.tags || char(10) || k.canonical_key || char(10) ||
                       IFNULL((SELECT group_concat(f.text, char(10)) FROM facts f
                               WHERE f.knowledge_version_id = v.id), '') || char(10) ||
                       IFNULL((SELECT group_concat(i.text || char(10) || i.reason, char(10)) FROM inferences i
                               WHERE i.knowledge_version_id = v.id), '') || char(10) ||
                       IFNULL((SELECT group_concat(e.symbol_name || char(10) || IFNULL(e.symbol_id, ''), char(10))
                               FROM evidence e WHERE e.knowledge_version_id = v.id), '')
                       AS search_text
                FROM knowledge k
                JOIN knowledge_versions v ON v.id = k.current_version_id
                WHERE k.project_id = @project
            )
            WHERE {conditions};
            """;
        command.Parameters.AddWithValue("@project", projectId);
        for (var index = 0; index < likePatterns.Count; index++)
            command.Parameters.AddWithValue($"@like{index}", likePatterns[index]);
        using var reader = command.ExecuteReader();
        var hits = new List<LikeSearchHit>();
        while (reader.Read())
            hits.Add(new LikeSearchHit(ReadSummary(reader), reader.GetString(7)));
        return hits;
    }

    internal static KnowledgeSummary ReadSummary(SqliteDataReader reader)
    {
        ConfidenceParser.TryParse(reader.GetString(5), out var confidence);
        return new KnowledgeSummary(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), confidence,
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static string? FindKnowledgeId(
        SqliteConnection connection, SqliteTransaction transaction, string projectId, string canonicalKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id FROM knowledge WHERE project_id = @project AND canonical_key = @key;";
        command.Parameters.AddWithValue("@project", projectId);
        command.Parameters.AddWithValue("@key", canonicalKey);
        return command.ExecuteScalar() as string;
    }

    private static void Execute(
        SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
