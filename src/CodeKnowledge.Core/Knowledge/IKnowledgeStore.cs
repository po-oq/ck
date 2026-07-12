using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Knowledge;

public sealed record VersionToSave(
    string ProjectId,
    string CanonicalKey,
    string Title,
    string CommitHash,
    string? BranchName,
    string OriginalQuestion,
    string Summary,
    Confidence Confidence,
    string Tags,
    string CreatedBy,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<FactRecord> Facts,
    IReadOnlyList<InferenceRecord> Inferences,
    IReadOnlyList<RelationRecord> Relations);

public sealed record SaveVersionResult(string KnowledgeId, string VersionId, bool CreatedNewKnowledge);

public sealed record FtsSearchHit(KnowledgeSummary Summary, double Score, string SearchText);

public sealed record LikeSearchHit(KnowledgeSummary Summary, string SearchText);

public interface IKnowledgeStore
{
    SaveVersionResult SaveVersion(VersionToSave version);
    IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId);
    KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId);
    IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit);
    IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns);
}
