using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Tests.Fakes;
using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class ValidateKnowledgeUseCaseTests
{
private const string BaseBody = "class App\n{\n    void Run() { }\n}\n";
private readonly FakeGitRepository _git = new();
private readonly FakeProjectStore _projects = new();
private readonly FakeKnowledgeStore _knowledge = new();

private ValidateKnowledgeUseCase UseCase => new(
    new ResolveProjectUseCase(_git, _projects), _git, _knowledge);

public ValidateKnowledgeUseCaseTests()
{
    _git.Context = new GitContext(
        "/work/order-api", "target456", "main",
        new Dictionary<string, string>
        {
            ["origin"] = "https://github.com/company/order-api.git",
        }, null, null);
    _git.DefaultCommitDiff = new GitCommitDiff([]);
    _git.WorkingTreeChangedPaths = new HashSet<string>(StringComparer.Ordinal);
}

private static EvidenceRecord Evidence(
    string id, string path, string baseContent,
    int? start = 1, int? end = 4, bool includeSymbolHash = true)
    => new(
        id, path, $"App.{id}", id, "method", $"void {id}()",
        start, end, "base123", ContentHasher.ComputeFileHash(baseContent),
        includeSymbolHash && start is { } from && end is { } to
            ? ContentHasher.ComputeSymbolHash(baseContent, from, to) : null,
        "test evidence");

private void SetDetail(params EvidenceRecord[] evidence)
{
    _knowledge.Detail = new KnowledgeDetail(
        "knowledge-1", "domain.order", "Order", "version-1", "base123", "main",
        "question", "summary", Confidence.High, "order", "test", DateTimeOffset.UtcNow,
        [], [], evidence, []);
}

private void SetFile(string commit, string path, string content)
    => _git.Snapshots[(commit, path)] = GitFileSnapshot.Available(content);

private ValidateKnowledgeResult Execute(string? target = null)
    => UseCase.Execute(new("/work/order-api", "knowledge-1", target));

[Fact]
public void Execute_returns_valid_when_every_file_hash_matches()
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    SetFile("target456", "src/one.cs", BaseBody);
    var result = Execute();
    Assert.Equal(ValidationStatus.Valid, result.Status);
    Assert.Equal(["App.one"], result.UnchangedEvidence);
    Assert.False(result.IsWorkingTreeDirty);
    Assert.Equal(RecommendedAction.ReuseKnowledge, result.RecommendedAction);
}

[Fact]
public void Execute_returns_legacy_evidence_unchanged_when_the_file_hash_matches()
{
    SetDetail(Evidence("legacy", "src/legacy.cs", BaseBody, includeSymbolHash: false));
    SetFile("target456", "src/legacy.cs", BaseBody);
    var result = Execute();
    Assert.Equal(ValidationStatus.Valid, result.Status);
    Assert.Equal(ValidationReason.FileHashMatch,
        Assert.Single(result.EvidenceDetails).Reason);
}

[Fact]
public void Execute_uses_the_approved_evidence_label_priority()
{
    var byId = Evidence("id", "src/id.cs", BaseBody) with
        { SymbolId = "App.Id", Signature = "sig-id", SymbolName = "name-id" };
    var bySignature = Evidence("signature", "src/signature.cs", BaseBody) with
        { SymbolId = null, Signature = "void Signature()", SymbolName = "name-signature" };
    var byName = Evidence("name", "src/name.cs", BaseBody) with
        { SymbolId = null, Signature = null, SymbolName = "App.Name" };
    var byPath = Evidence("path", "src/path.cs", BaseBody) with
        { SymbolId = null, Signature = null, SymbolName = "" };
    SetDetail(byId, bySignature, byName, byPath);
    foreach (var path in new[] { "src/id.cs", "src/signature.cs", "src/name.cs", "src/path.cs" })
        SetFile("target456", path, BaseBody);
    var result = Execute();
    Assert.Equal(
        ["App.Id", "void Signature()", "App.Name", "src/path.cs"],
        result.UnchangedEvidence);
}

[Fact]
public void Execute_returns_partially_stale_for_unchanged_and_changed()
{
    SetDetail(
        Evidence("unchanged", "src/unchanged.cs", BaseBody),
        Evidence("changed", "src/changed.cs", BaseBody));
    SetFile("target456", "src/unchanged.cs", BaseBody);
    SetFile("base123", "src/changed.cs", BaseBody);
    SetFile("target456", "src/changed.cs", BaseBody.Replace("Run", "Stop"));
    _git.WorkingTreeChangedPaths =
        new HashSet<string>(["src/changed.cs"], StringComparer.Ordinal);
    var result = Execute();
    Assert.Equal(ValidationStatus.PartiallyStale, result.Status);
    Assert.Equal(["App.unchanged"], result.UnchangedEvidence);
    Assert.Equal(["App.changed"], result.ChangedEvidence);
    Assert.Equal(["App.changed"], result.DirtyEvidence);
    Assert.Empty(result.UnknownEvidence);
    Assert.Equal(RecommendedAction.ReinspectChangedSymbols, result.RecommendedAction);
}

[Fact]
public void Execute_returns_stale_when_every_evidence_is_changed_or_missing()
{
    SetDetail(
        Evidence("changed", "src/changed.cs", BaseBody),
        Evidence("missing", "src/missing.cs", BaseBody));
    SetFile("base123", "src/changed.cs", BaseBody);
    SetFile("target456", "src/changed.cs", BaseBody.Replace("Run", "Stop"));
    _git.Snapshots[("target456", "src/missing.cs")] = GitFileSnapshot.Missing();
    var result = Execute();
    Assert.Equal(ValidationStatus.Stale, result.Status);
    Assert.Equal(["App.changed"], result.ChangedEvidence);
    Assert.Equal(["App.missing"], result.MissingEvidence);
    Assert.Equal(RecommendedAction.ReinvestigateKnowledge, result.RecommendedAction);
}

[Fact]
public void Execute_returns_unknown_when_symbol_hash_is_absent_and_file_changed()
{
    SetDetail(Evidence("legacy", "src/legacy.cs", BaseBody, includeSymbolHash: false));
    SetFile("target456", "src/legacy.cs", BaseBody.Replace("Run", "Stop"));
    var result = Execute();
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Equal(["App.legacy"], result.UnknownEvidence);
    Assert.Equal(ValidationReason.SymbolHashUnavailable,
        Assert.Single(result.EvidenceDetails).Reason);
}

[Theory]
[InlineData(true, ValidationReason.TargetFileUnavailable)]
[InlineData(false, ValidationReason.BaseFileUnavailable)]
public void Execute_returns_unknown_when_required_file_content_is_unavailable(
    bool targetUnavailable, ValidationReason expectedReason)
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    _git.Snapshots[("target456", "src/one.cs")] = targetUnavailable
        ? GitFileSnapshot.Unavailable()
        : GitFileSnapshot.Available(BaseBody.Replace("Run", "Stop"));
    _git.Snapshots[("base123", "src/one.cs")] = targetUnavailable
        ? GitFileSnapshot.Available(BaseBody)
        : GitFileSnapshot.Unavailable();
    var result = Execute();
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Equal(expectedReason, Assert.Single(result.EvidenceDetails).Reason);
}

[Theory]
[InlineData("base123", null, "target456", "base_commit_unavailable")]
[InlineData("gone", "gone", null, "target_commit_unavailable")]
public void Execute_returns_all_unknown_when_a_commit_is_unavailable(
    string unavailable, string? target, string? expectedTarget, string warningPrefix)
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    _git.UnavailableCommits.Add(unavailable);
    var result = Execute(target);
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Equal(expectedTarget, result.TargetCommit);
    Assert.Equal(["App.one"], result.UnknownEvidence);
    Assert.Equal(ValidationReason.CommitUnavailable,
        Assert.Single(result.EvidenceDetails).Reason);
    Assert.StartsWith(warningPrefix, Assert.Single(result.Warnings));
}

[Fact]
public void Execute_returns_all_unknown_when_diff_is_unavailable()
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    _git.Diffs[("base123", "target456")] = null;
    var result = Execute();
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Equal(ValidationReason.DiffUnavailable,
        Assert.Single(result.EvidenceDetails).Reason);
}

[Fact]
public void Execute_tracks_rename_and_finds_an_unchanged_moved_symbol()
{
    SetDetail(Evidence("moved", "src/old.cs", BaseBody));
    _git.DefaultCommitDiff = new GitCommitDiff([
        new(GitChangeKind.Renamed, "src/old.cs", "src/new.cs", []),
    ]);
    SetFile("base123", "src/old.cs", BaseBody);
    SetFile("target456", "src/new.cs", "header\nheader2\n" + BaseBody);
    var result = Execute();
    var detail = Assert.Single(result.EvidenceDetails);
    Assert.Equal(ValidationStatus.Valid, result.Status);
    Assert.Equal("src/new.cs", detail.TargetFilePath);
    Assert.Equal(ValidationReason.SymbolHashMatchAtMovedRange, detail.Reason);
}

[Fact]
public void Execute_clamps_saved_end_line_to_the_base_file()
{
    SetDetail(Evidence("clamped", "src/a.cs", BaseBody, end: 999));
    SetFile("base123", "src/a.cs", BaseBody);
    SetFile("target456", "src/a.cs", "header\n" + BaseBody);
    var result = Execute();
    Assert.Equal(ValidationStatus.Valid, result.Status);
    Assert.Equal(ValidationReason.SymbolHashMatchAtMovedRange,
        Assert.Single(result.EvidenceDetails).Reason);
}

[Fact]
public void Execute_keeps_valid_but_recommends_dirty_inspection()
{
    SetDetail(Evidence("dirty", "src/dirty.cs", BaseBody));
    SetFile("target456", "src/dirty.cs", BaseBody);
    _git.WorkingTreeChangedPaths = new HashSet<string>(["src/dirty.cs"], StringComparer.Ordinal);
    var result = Execute();
    Assert.Equal(ValidationStatus.Valid, result.Status);
    Assert.True(result.IsWorkingTreeDirty);
    Assert.Equal(["App.dirty"], result.DirtyEvidence);
    Assert.Equal(RecommendedAction.InspectDirtyEvidence, result.RecommendedAction);
}

[Fact]
public void Execute_maps_dirty_paths_against_HEAD_for_an_explicit_target()
{
    SetDetail(Evidence("dirty", "src/old.cs", BaseBody));
    SetFile("base123", "src/old.cs", BaseBody);
    _git.Diffs[("base123", "saved999")] = new GitCommitDiff([]);
    _git.Diffs[("base123", "target456")] = new GitCommitDiff([
        new(GitChangeKind.Renamed, "src/old.cs", "src/current.cs", []),
    ]);
    _git.WorkingTreeChangedPaths =
        new HashSet<string>(["src/current.cs"], StringComparer.Ordinal);
    SetFile("saved999", "src/old.cs", BaseBody);
    var result = Execute("saved999");
    Assert.Equal("saved999", result.TargetCommit);
    Assert.True(result.IsWorkingTreeDirty);
}

[Fact]
public void Execute_returns_unknown_when_dirty_check_is_unavailable()
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    SetFile("target456", "src/one.cs", BaseBody);
    _git.WorkingTreeChangedPaths = null;
    var result = Execute();
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Null(result.IsWorkingTreeDirty);
    Assert.Contains("dirty_check_unavailable", result.Warnings);
}

[Fact]
public void Execute_returns_unknown_for_zero_evidence()
{
    SetDetail();
    var result = Execute();
    Assert.Equal(ValidationStatus.Unknown, result.Status);
    Assert.Empty(result.EvidenceDetails);
}

[Fact]
public void Execute_reads_each_commit_file_once_for_duplicate_evidence()
{
    SetDetail(
        Evidence("one", "src/shared.cs", BaseBody),
        Evidence("two", "src/shared.cs", BaseBody));
    SetFile("base123", "src/shared.cs", BaseBody);
    SetFile("target456", "src/shared.cs", "header\n" + BaseBody);
    Execute();
    Assert.Equal(1, _git.SnapshotReadCounts[("base123", "src/shared.cs")]);
    Assert.Equal(1, _git.SnapshotReadCounts[("target456", "src/shared.cs")]);
}

[Fact]
public void Execute_reads_only_the_current_project_current_version()
{
    SetDetail(Evidence("one", "src/one.cs", BaseBody));
    SetFile("target456", "src/one.cs", BaseBody);
    Execute();
    Assert.Equal("github.com/company/order-api", _knowledge.LastGetDetailProjectId);
    Assert.Equal("knowledge-1", _knowledge.LastGetDetailKnowledgeId);
    Assert.Null(_knowledge.LastGetDetailVersionId);
}

[Theory]
[InlineData("", "knowledge-1")]
[InlineData("   ", "knowledge-1")]
[InlineData("/work/order-api", "")]
public void Execute_rejects_blank_required_arguments(
    string workingDirectory, string knowledgeId)
{
    var error = Assert.Throws<CodeKnowledgeException>(() => UseCase.Execute(
        new(workingDirectory, knowledgeId, null)));
    Assert.Equal(CodeKnowledgeException.InvalidArguments, error.Code);
}

[Fact]
public void Execute_rejects_explicit_blank_target_commit()
{
    var error = Assert.Throws<CodeKnowledgeException>(() => UseCase.Execute(
        new("/work/order-api", "knowledge-1", " ")));
    Assert.Equal(CodeKnowledgeException.InvalidArguments, error.Code);
}

[Fact]
public void Execute_throws_knowledge_not_found_in_current_project()
{
    var error = Assert.Throws<CodeKnowledgeException>(() => Execute());
    Assert.Equal(CodeKnowledgeException.KnowledgeNotFound, error.Code);
}
}
