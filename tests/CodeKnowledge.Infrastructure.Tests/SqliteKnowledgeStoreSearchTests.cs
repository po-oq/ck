using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreSearchTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreSearchTests()
    {
        var projects = new SqliteProjectStore(_db.Factory);
        projects.Upsert(new Project("github.com/company/order-api", "order-api",
            @"C:\work\order-api", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        projects.Upsert(new Project("github.com/other/repo", "other",
            @"C:\work\other", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SearchFts_matches_japanese_trigram_keyword()
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            title: "注文完了メール仕様"));

        var hits = Store.SearchFts("github.com/company/order-api", "\"メール\"", 10);

        var hit = Assert.Single(hits);
        Assert.Equal("注文完了メール仕様", hit.Summary.Title);
        Assert.Contains("メール", hit.SearchText);
    }

    [Fact]
    public void SearchFts_scopes_to_project() // AC-01
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "注文完了メール仕様"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            projectId: "github.com/other/repo", title: "注文完了メール仕様"));

        var hits = Store.SearchFts("github.com/company/order-api", "\"メール\"", 10);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchFts_only_finds_current_version() // AC-23
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "旧世代キーワードXYZQW"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "新しい概要"));

        Assert.Empty(Store.SearchFts("github.com/company/order-api", "\"XYZQW\"", 10));
    }

    [Fact]
    public void SearchFts_finds_full_width_text_with_half_width_keyword() // 要件8.4
    {
        // 保存時の原文は全角。検索キーワードは半角（KeywordPreparationでNFKC正規化済み）。
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "ｍｅｍｏｒｙ leak note"));

        var prepared = KeywordPreparation.Prepare(["memory"]);
        var hits = Store.SearchFts(
            "github.com/company/order-api", prepared.FtsMatchExpression!, 10);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchLike_finds_full_width_text_with_half_width_keyword() // 要件8.4
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "ＤＢ接続の注意点"));

        var prepared = KeywordPreparation.Prepare(["db"]);
        var hits = Store.SearchLike(
            "github.com/company/order-api",
            prepared.LikeKeywords.Select(KeywordPreparation.EscapeLikePattern).ToList());

        Assert.Single(hits);
    }

    [Fact]
    public void SearchLike_scopes_to_project() // AC-01
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "注文完了メール仕様"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            projectId: "github.com/other/repo", title: "注文完了メール仕様"));

        var hits = Store.SearchLike("github.com/company/order-api", ["%仕様%"]);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchLike_matches_two_char_keyword()
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "注文完了メール仕様"));

        var hits = Store.SearchLike("github.com/company/order-api", ["%仕様%"]);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchLike_does_not_scan_file_paths() // 要件8.3
    {
        // file_pathにだけ「ab」を含むナレッジ（symbol名等には含まない）
        var version = SqliteKnowledgeStoreSaveTests.Sample() with
        {
            Title = "タイトル", Summary = "概要", Tags = "",
            CanonicalKey = "key.one", OriginalQuestion = "質問",
        };
        var evidence = version.Evidence[0] with
        {
            FilePath = "src/ab.cs", SymbolName = "Foo", SymbolId = null,
        };
        var fact = version.Facts[0] with { Text = "事実", EvidenceIds = [evidence.Id] };
        var inference = version.Inferences[0] with { Text = "推論", Reason = "理由", EvidenceIds = [evidence.Id] };
        Store.SaveVersion(version with
        {
            Evidence = [evidence], Facts = [fact], Inferences = [inference],
        });

        Assert.Empty(Store.SearchLike("github.com/company/order-api", ["%ab%"]));
    }

    [Fact]
    public void SearchLike_escapes_metacharacters() // AC-21
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "進捗は50%です"));

        var hits = Store.SearchLike("github.com/company/order-api", ["%50\\%%"]);

        Assert.Single(hits);
        // ワイルドカードとして解釈されるなら「50x」もヒットしてしまう
        Assert.Empty(Store.SearchLike("github.com/company/order-api", ["%5\\_0%"]));
    }

    public void Dispose() => _db.Dispose();
}
