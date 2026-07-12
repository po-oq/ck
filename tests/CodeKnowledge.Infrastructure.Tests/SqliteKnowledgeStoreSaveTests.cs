using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreSaveTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreSaveTests()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/company/order-api", "order-api", @"C:\work\order-api",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    internal static VersionToSave Sample(
        string canonicalKey = "domain.mail.order-completed",
        string title = "注文完了メール仕様",
        string summary = "OrderServiceがメールを送る",
        string projectId = "github.com/company/order-api")
    {
        var evidence = new EvidenceRecord(
            Guid.CreateVersion7().ToString("N"), "src/OrderService.cs", null,
            "OrderService.Complete", "method", null, 1, 4,
            "abc123", "filehash", "symbolhash", null);
        var fact = new FactRecord(
            Guid.CreateVersion7().ToString("N"), "メールはOrderServiceが送信する", [evidence.Id]);
        var inference = new InferenceRecord(
            Guid.CreateVersion7().ToString("N"), "リトライはなさそう",
            Confidence.Low, "呼び出し元にリトライ処理が見当たらない", [evidence.Id]);
        var relation = new RelationRecord(
            Guid.CreateVersion7().ToString("N"),
            "OrderService.Complete", "SmtpEmailSender.SendAsync", "calls");
        return new VersionToSave(
            projectId, canonicalKey, title, "abc123", "main",
            "注文完了メールの処理は？", summary, Confidence.High,
            "mail order", "test-agent", [evidence], [fact], [inference], [relation]);
    }

    private long Scalar(string sql)
    {
        using var connection = _db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void SaveVersion_creates_knowledge_version_and_children()
    {
        var result = Store.SaveVersion(Sample());

        Assert.True(result.CreatedNewKnowledge);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_versions;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM facts;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM fact_evidence;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM inferences;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM evidence;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM relations;"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM knowledge WHERE current_version_id IS NOT NULL;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_fts;"));
    }

    [Fact]
    public void SaveVersion_adds_version_to_existing_canonical_key() // 要件6.1
    {
        var first = Store.SaveVersion(Sample(summary: "旧サマリー"));
        var second = Store.SaveVersion(Sample(summary: "新サマリー"));

        Assert.False(second.CreatedNewKnowledge);
        Assert.Equal(first.KnowledgeId, second.KnowledgeId);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge;"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM knowledge_versions;"));
        // FTSは最新確定バージョンのみ（AC-23）
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_fts;"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM knowledge_fts WHERE summary = '新サマリー';"));
    }

    [Fact]
    public void ListSummaries_returns_current_versions_for_project_only()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/other/repo", "other", @"C:\work\other",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Store.SaveVersion(Sample());
        Store.SaveVersion(Sample(projectId: "github.com/other/repo", canonicalKey: "other.key"));

        var summaries = Store.ListSummaries("github.com/company/order-api");

        var summary = Assert.Single(summaries);
        Assert.Equal("domain.mail.order-completed", summary.CanonicalKey);
    }

    public void Dispose() => _db.Dispose();
}
