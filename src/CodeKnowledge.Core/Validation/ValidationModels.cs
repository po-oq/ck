using System.Text.Json.Serialization;

namespace CodeKnowledge.Core.Validation;

[JsonConverter(typeof(JsonStringEnumConverter<ValidationStatus>))]
public enum ValidationStatus
{
    [JsonStringEnumMemberName("valid")] Valid,
    [JsonStringEnumMemberName("partially_stale")] PartiallyStale,
    [JsonStringEnumMemberName("stale")] Stale,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceValidationStatus>))]
public enum EvidenceValidationStatus
{
    [JsonStringEnumMemberName("unchanged")] Unchanged,
    [JsonStringEnumMemberName("changed")] Changed,
    [JsonStringEnumMemberName("missing")] Missing,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<ValidationReason>))]
public enum ValidationReason
{
    [JsonStringEnumMemberName("file_hash_match")] FileHashMatch,
    [JsonStringEnumMemberName("symbol_hash_match_at_mapped_range")] SymbolHashMatchAtMappedRange,
    [JsonStringEnumMemberName("symbol_hash_match_at_moved_range")] SymbolHashMatchAtMovedRange,
    [JsonStringEnumMemberName("target_file_missing")] TargetFileMissing,
    [JsonStringEnumMemberName("symbol_hash_not_found")] SymbolHashNotFound,
    [JsonStringEnumMemberName("symbol_hash_unavailable")] SymbolHashUnavailable,
    [JsonStringEnumMemberName("base_file_unavailable")] BaseFileUnavailable,
    [JsonStringEnumMemberName("target_file_unavailable")] TargetFileUnavailable,
    [JsonStringEnumMemberName("commit_unavailable")] CommitUnavailable,
    [JsonStringEnumMemberName("diff_unavailable")] DiffUnavailable,
    [JsonStringEnumMemberName("dirty_check_unavailable")] DirtyCheckUnavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<RecommendedAction>))]
public enum RecommendedAction
{
    [JsonStringEnumMemberName("reuse_knowledge")] ReuseKnowledge,
    [JsonStringEnumMemberName("inspect_dirty_evidence")] InspectDirtyEvidence,
    [JsonStringEnumMemberName("reinspect_changed_symbols")] ReinspectChangedSymbols,
    [JsonStringEnumMemberName("reinvestigate_knowledge")] ReinvestigateKnowledge,
    [JsonStringEnumMemberName("inspect_evidence")] InspectEvidence,
}

public sealed record ValidateKnowledgeRequest(
    string WorkingDirectory, string KnowledgeId, string? TargetCommit);

public sealed record EvidenceValidationResult(
    string EvidenceId, string Label, string OriginalFilePath, string? TargetFilePath,
    EvidenceValidationStatus Status, ValidationReason Reason, bool? IsWorkingTreeDirty);

public sealed record ValidateKnowledgeResult(
    ValidationStatus Status, string BaseCommit, string? TargetCommit,
    bool? IsWorkingTreeDirty,
    IReadOnlyList<string> ChangedEvidence,
    IReadOnlyList<string> UnchangedEvidence,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> UnknownEvidence,
    IReadOnlyList<string> DirtyEvidence,
    IReadOnlyList<EvidenceValidationResult> EvidenceDetails,
    RecommendedAction RecommendedAction,
    IReadOnlyList<string> Warnings);
