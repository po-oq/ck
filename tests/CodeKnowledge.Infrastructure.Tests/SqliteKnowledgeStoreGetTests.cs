using CodeKnowledge.Core.Domain;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreGetTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreGetTests()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/company/order-api", "order-api", @"C:\work\order-api",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GetDetail_returns_current_version_with_children() // AC-03
    {
        var saved = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample());

        var detail = Store.GetDetail("github.com/company/order-api", saved.KnowledgeId, null);

        Assert.NotNull(detail);
        Assert.Equal(saved.VersionId, detail.VersionId);
        Assert.Equal("注文完了メール仕様", detail.Title);
        var fact = Assert.Single(detail.Facts);
        var evidence = Assert.Single(detail.Evidence);
        Assert.Equal([evidence.Id], fact.EvidenceIds);
        Assert.Equal("src/OrderService.cs", evidence.FilePath);
        Assert.Single(detail.Inferences);
        Assert.Single(detail.Relations);
        Assert.Equal(Confidence.High, detail.Confidence);
    }

    [Fact]
    public void GetDetail_returns_specified_older_version()
    {
        var first = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "旧"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "新"));

        var detail = Store.GetDetail(
            "github.com/company/order-api", first.KnowledgeId, first.VersionId);

        Assert.NotNull(detail);
        Assert.Equal("旧", detail.Summary);
    }

    [Fact]
    public void GetDetail_returns_null_for_unknown_id_or_wrong_project()
    {
        var saved = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample());
        Assert.Null(Store.GetDetail("github.com/company/order-api", "missing", null));
        Assert.Null(Store.GetDetail("github.com/other/repo", saved.KnowledgeId, null));
    }

    [Fact]
    public void GetDetail_returns_null_for_version_of_different_knowledge()
    {
        var savedA = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample());
        var savedB = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            canonicalKey: "domain.mail.order-cancelled", title: "注文キャンセルメール仕様"));

        Assert.Null(Store.GetDetail(
            "github.com/company/order-api", savedB.KnowledgeId, savedA.VersionId));
    }

    [Fact]
    public void GetDetail_returns_empty_lists_when_inferences_and_relations_absent()
    {
        var saved = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample() with
        {
            Inferences = [],
            Relations = [],
        });

        var detail = Store.GetDetail("github.com/company/order-api", saved.KnowledgeId, null);

        Assert.NotNull(detail);
        Assert.Single(detail.Facts);
        Assert.NotNull(detail.Inferences);
        Assert.Empty(detail.Inferences);
        Assert.NotNull(detail.Relations);
        Assert.Empty(detail.Relations);
    }

    public void Dispose() => _db.Dispose();
}
