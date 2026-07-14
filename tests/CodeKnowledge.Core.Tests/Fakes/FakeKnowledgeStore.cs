using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeKnowledgeStore : IKnowledgeStore
{
    public List<VersionToSave> SavedVersions { get; } = [];
    public List<KnowledgeSummary> Summaries { get; } = [];
    public KnowledgeDetail? Detail { get; set; }
    public string? LastGetDetailProjectId { get; private set; }
    public string? LastGetDetailKnowledgeId { get; private set; }
    public string? LastGetDetailVersionId { get; private set; }

    public SaveVersionResult SaveVersion(VersionToSave version)
    {
        SavedVersions.Add(version);
        return new SaveVersionResult("knowledge-1", "version-1", CreatedNewKnowledge: true);
    }

    public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId) => Summaries;

    public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
    {
        LastGetDetailProjectId = projectId;
        LastGetDetailKnowledgeId = knowledgeId;
        LastGetDetailVersionId = versionId;
        return Detail;
    }

    public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
        => [];

    public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
        => [];
}
