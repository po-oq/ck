namespace CodeKnowledge.Core.Domain;

public sealed record Project(
    string ProjectId,
    string DisplayName,
    string RepositoryRoot,
    string? RemoteUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeSummary(
    string KnowledgeId,
    string CanonicalKey,
    string Title,
    string Summary,
    string CommitHash,
    Confidence Confidence,
    DateTimeOffset UpdatedAt);

public sealed record EvidenceRecord(
    string Id,
    string FilePath,
    string? SymbolId,
    string SymbolName,
    string? SymbolKind,
    string? Signature,
    int? StartLine,
    int? EndLine,
    string CommitHash,
    string FileHash,
    string? SymbolHash,
    string? Reason);

public sealed record FactRecord(string Id, string Text, IReadOnlyList<string> EvidenceIds);

public sealed record InferenceRecord(
    string Id,
    string Text,
    Confidence Confidence,
    string Reason,
    IReadOnlyList<string> EvidenceIds);

public sealed record RelationRecord(string Id, string FromSymbol, string ToSymbol, string Kind);

public sealed record KnowledgeDetail(
    string KnowledgeId,
    string CanonicalKey,
    string Title,
    string VersionId,
    string CommitHash,
    string? BranchName,
    string OriginalQuestion,
    string Summary,
    Confidence Confidence,
    string Tags,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FactRecord> Facts,
    IReadOnlyList<InferenceRecord> Inferences,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<RelationRecord> Relations);
