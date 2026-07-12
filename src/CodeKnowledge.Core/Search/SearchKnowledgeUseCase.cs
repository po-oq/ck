using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Search;

public sealed record SearchResultItem(
    string KnowledgeId, string CanonicalKey, string Title, string Summary,
    string CommitHash, Confidence Confidence, DateTimeOffset UpdatedAt,
    string MatchedRoute,
    IReadOnlyList<string> MatchedKeywords);

public sealed record SearchKnowledgeResult(
    string ProjectId, IReadOnlyList<SearchResultItem> Results, int TotalCandidates);

public sealed class SearchKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IKnowledgeStore store)
{
    private const int CandidateFetchLimit = 200;

    public SearchKnowledgeResult Execute(
        string workingDirectory, IReadOnlyList<string> keywords, int? limit)
    {
        var resolution = resolveProject.Execute(workingDirectory);
        var effectiveLimit = Math.Clamp(limit ?? 10, 1, 50);
        var prepared = KeywordPreparation.Prepare(keywords);
        var allKeywords = prepared.FtsKeywords.Concat(prepared.LikeKeywords).ToList();

        var ftsHits = prepared.FtsMatchExpression is null
            ? (IReadOnlyList<FtsSearchHit>)[]
            : store.SearchFts(resolution.ProjectId, prepared.FtsMatchExpression, CandidateFetchLimit);
        var likeHits = prepared.LikeKeywords.Count == 0
            ? (IReadOnlyList<LikeSearchHit>)[]
            : store.SearchLike(
                resolution.ProjectId,
                prepared.LikeKeywords.Select(KeywordPreparation.EscapeLikePattern).ToList());

        var likeIds = likeHits.Select(hit => hit.Summary.KnowledgeId).ToHashSet(StringComparer.Ordinal);
        var ftsIds = ftsHits.Select(hit => hit.Summary.KnowledgeId).ToHashSet(StringComparer.Ordinal);

        var ranked = new List<(int Tier, double Primary, double Secondary, SearchResultItem Item)>();
        foreach (var hit in ftsHits)
        {
            var route = likeIds.Contains(hit.Summary.KnowledgeId) ? "both" : "fts";
            ranked.Add((route == "both" ? 0 : 1, hit.Score, 0,
                ToItem(hit.Summary, route, MatchedKeywords(hit.SearchText, allKeywords))));
        }
        foreach (var hit in likeHits.Where(hit => !ftsIds.Contains(hit.Summary.KnowledgeId)))
        {
            var matched = MatchedKeywords(hit.SearchText, allKeywords);
            var likeMatchedCount = prepared.LikeKeywords.Count(matched.Contains);
            ranked.Add((2, -likeMatchedCount, -hit.Summary.UpdatedAt.ToUnixTimeMilliseconds(),
                ToItem(hit.Summary, "like", matched)));
        }

        var ordered = ranked
            .OrderBy(entry => entry.Tier)
            .ThenBy(entry => entry.Primary)
            .ThenBy(entry => entry.Secondary)
            .Select(entry => entry.Item)
            .ToList();

        return new SearchKnowledgeResult(
            resolution.ProjectId,
            ordered.Take(effectiveLimit).ToList(),
            ordered.Count);
    }

    private static SearchResultItem ToItem(
        KnowledgeSummary summary, string route, IReadOnlyList<string> matchedKeywords)
        => new(summary.KnowledgeId, summary.CanonicalKey, summary.Title, summary.Summary,
            summary.CommitHash, summary.Confidence, summary.UpdatedAt, route, matchedKeywords);

    private static IReadOnlyList<string> MatchedKeywords(
        string searchText, IReadOnlyList<string> keywords)
        => keywords
            .Where(keyword => searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
