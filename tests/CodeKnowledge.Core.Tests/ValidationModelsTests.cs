using System.Text.Json;
using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class ValidationModelsTests
{
    [Theory]
    [InlineData(ValidationStatus.Valid, "\"valid\"")]
    [InlineData(ValidationStatus.PartiallyStale, "\"partially_stale\"")]
    [InlineData(ValidationStatus.Stale, "\"stale\"")]
    [InlineData(ValidationStatus.Unknown, "\"unknown\"")]
    public void ValidationStatus_has_stable_wire_names(
        ValidationStatus value, string expected)
        => Assert.Equal(expected, JsonSerializer.Serialize(value));

    [Theory]
    [InlineData(EvidenceValidationStatus.Unchanged, "\"unchanged\"")]
    [InlineData(EvidenceValidationStatus.Changed, "\"changed\"")]
    [InlineData(EvidenceValidationStatus.Missing, "\"missing\"")]
    [InlineData(EvidenceValidationStatus.Unknown, "\"unknown\"")]
    public void Evidence_status_has_stable_wire_names(
        EvidenceValidationStatus value, string expected)
        => Assert.Equal(expected, JsonSerializer.Serialize(value));

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Validation_reason_has_stable_wire_names(
        ValidationReason value, string expected)
        => Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value));

    public static TheoryData<ValidationReason, string> Reasons => new()
    {
        { ValidationReason.FileHashMatch, "file_hash_match" },
        { ValidationReason.SymbolHashMatchAtMappedRange, "symbol_hash_match_at_mapped_range" },
        { ValidationReason.SymbolHashMatchAtMovedRange, "symbol_hash_match_at_moved_range" },
        { ValidationReason.TargetFileMissing, "target_file_missing" },
        { ValidationReason.SymbolHashNotFound, "symbol_hash_not_found" },
        { ValidationReason.SymbolHashUnavailable, "symbol_hash_unavailable" },
        { ValidationReason.BaseFileUnavailable, "base_file_unavailable" },
        { ValidationReason.TargetFileUnavailable, "target_file_unavailable" },
        { ValidationReason.CommitUnavailable, "commit_unavailable" },
        { ValidationReason.DiffUnavailable, "diff_unavailable" },
        { ValidationReason.DirtyCheckUnavailable, "dirty_check_unavailable" },
    };

    [Theory]
    [InlineData(RecommendedAction.ReuseKnowledge, "\"reuse_knowledge\"")]
    [InlineData(RecommendedAction.InspectDirtyEvidence, "\"inspect_dirty_evidence\"")]
    [InlineData(RecommendedAction.ReinspectChangedSymbols, "\"reinspect_changed_symbols\"")]
    [InlineData(RecommendedAction.ReinvestigateKnowledge, "\"reinvestigate_knowledge\"")]
    [InlineData(RecommendedAction.InspectEvidence, "\"inspect_evidence\"")]
    public void Recommended_action_has_stable_wire_names(
        RecommendedAction value, string expected)
        => Assert.Equal(expected, JsonSerializer.Serialize(value));
}
