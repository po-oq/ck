using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Core.Tests.Fakes;

namespace CodeKnowledge.Core.Tests;

public sealed class SearchKnowledgeUseCaseTests
{
    private sealed class SearchFake : IKnowledgeStore
    {
        public List<FtsSearchHit> FtsHits { get; } = [];
        public List<LikeSearchHit> LikeHits { get; } = [];
        public string? LastMatchExpression;
        public IReadOnlyList<string>? LastLikePatterns;

        public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
        {
            LastMatchExpression = matchExpression;
            return FtsHits;
        }

        public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
        {
            LastLikePatterns = likePatterns;
            return LikeHits;
        }

        public SaveVersionResult SaveVersion(VersionToSave version) => throw new NotSupportedException();
        public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId) => [];
        public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId) => null;
    }

    private readonly FakeGitRepository _git = new();
    private readonly SearchFake _store = new();

    private SearchKnowledgeUseCase UseCase => new(
        new ResolveProjectUseCase(_git, new FakeProjectStore()), _store);

    public SearchKnowledgeUseCaseTests()
    {
        _git.Context = new GitContext(
            @"C:\work\order-api", "abc123", "main",
            new Dictionary<string, string> { ["origin"] = "https://h.example/company/order-api" },
            null, null);
    }

    private static KnowledgeSummary Summary(string id, DateTimeOffset? updatedAt = null)
        => new(id, $"key.{id}", $"タイトル{id}", "概要", "abc",
            Confidence.High, updatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Execute_ranks_both_then_fts_then_like() // 要件8.3のマージ表
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("fts-only"), Score: -1.5, "メール通知の仕様"));
        _store.FtsHits.Add(new FtsSearchHit(Summary("both"), Score: -0.5, "注文メールの仕様 確認"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("both"), "注文メールの仕様 確認"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("like-only"), "仕様の確認メモ"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール", "仕様"], limit: 10);

        Assert.Equal(["both", "fts-only", "like-only"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
        Assert.Equal("both", result.Results[0].MatchedRoute);
        Assert.Equal("fts", result.Results[1].MatchedRoute);
        Assert.Equal("like", result.Results[2].MatchedRoute);
        Assert.Equal(3, result.TotalCandidates);
    }

    [Fact]
    public void Execute_orders_fts_hits_by_bm25_ascending()
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("weak"), Score: -0.2, "メール"));
        _store.FtsHits.Add(new FtsSearchHit(Summary("strong"), Score: -3.0, "メール メール"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール"], limit: 10);

        Assert.Equal(["strong", "weak"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
    }

    [Fact]
    public void Execute_orders_like_only_hits_by_matched_count_then_recency()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-1);
        _store.LikeHits.Add(new LikeSearchHit(Summary("one-word", older), "仕様のみ"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("two-words"), "仕様の確認"));

        var result = UseCase.Execute(@"C:\work\order-api", ["仕様", "確認"], limit: 10);

        Assert.Equal(["two-words", "one-word"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
        Assert.Equal(["仕様", "確認"], result.Results[0].MatchedKeywords);
    }

    [Fact]
    public void Execute_reports_matched_keywords_from_search_text() // AC-22
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("hit"), Score: -1.0, "注文完了メールの仕様"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール", "存在しない語"], limit: 10);

        Assert.Equal(["メール"], result.Results[0].MatchedKeywords);
    }

    [Fact]
    public void Execute_matches_keywords_against_full_width_search_text() // 要件8.4
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("hit"), Score: -1.0, "ｍｅｍｏｒｙ leak note"));

        var result = UseCase.Execute(@"C:\work\order-api", ["memory"], limit: 10);

        Assert.Equal(["memory"], result.Results[0].MatchedKeywords);
    }

    [Fact]
    public void Execute_clamps_limit_and_allows_empty_results()
    {
        var result = UseCase.Execute(@"C:\work\order-api", ["メール"], limit: 999);
        Assert.Empty(result.Results); // エラーにしない（要件10.2）
    }

    [Fact]
    public void Execute_skips_fts_route_when_no_long_keywords()
    {
        UseCase.Execute(@"C:\work\order-api", ["仕様"], limit: 10);
        Assert.Null(_store.LastMatchExpression);
        Assert.NotNull(_store.LastLikePatterns);
    }
}
