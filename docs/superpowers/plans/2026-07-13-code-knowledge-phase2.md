# Code Knowledge Phase 2 Freshness Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `validate_knowledge` so an Agent can distinguish reusable, changed, missing, and indeterminate evidence against HEAD or a requested commit, while separately reporting uncommitted evidence changes.

**Architecture:** Preserve `CodeKnowledge.Mcp → CodeKnowledge.Core ← CodeKnowledge.Infrastructure`. Core owns evidence classification and aggregate decisions; Infrastructure exposes structured Git commit/diff/file/status facts; MCP only converts inputs and calls the use case. Reuse the Phase 1 evidence hashes and schema, and never persist validation results.

**Tech Stack:** .NET SDK 10.0.201, C# with nullable reference types and warnings-as-errors, xUnit v3, Git CLI, SQLite, ModelContextProtocol C# SDK

## Global Constraints

- Design authority: `docs/superpowers/specs/2026-07-13-code-knowledge-phase2-design.md`.
- Target runtime remains .NET 10; supported release RIDs remain `win-x64` and `osx-arm64`.
- Keep `Mcp → Core ← Infrastructure`; Core must not reference SQLite, Git process APIs, or MCP SDK types.
- Use Git CLI through `ProcessStartInfo.ArgumentList`; never invoke a shell or interpolate Agent input into a command string.
- Phase 2 starts at normalized symbol-range hashing; file-hash-only classification must not mark a changed file's unknown symbol as stale.
- Overall status order is: any unknown → `unknown`; all unchanged → `valid`; all changed/missing → `stale`; otherwise → `partially_stale`.
- Dirty working-tree state never changes an otherwise known freshness status; inability to determine dirty state makes the overall result `unknown`.
- `symbol_hash` absent + file hash match → unchanged; `symbol_hash` absent + file changed → unknown; file absent → missing.
- Preserve rename and same-file line movement when the saved symbol hash still exists.
- No schema migration, `compare_knowledge`, temporary comparison, diff persistence, Roslyn dependency, version selection, or automatic knowledge update.
- `ResolveProjectUseCase` keeps its existing project-row upsert behavior; “read-only validation” means no knowledge/version/evidence/FTS or validation-result writes.
- Preserve the user's unrelated `.mcp.json` and `.DS_Store` working-tree changes.

## File Structure

### Create

- `src/CodeKnowledge.Core/Validation/ValidationModels.cs` — wire-stable status/reason/action enums and validation request/result records.
- `src/CodeKnowledge.Core/Validation/ValidationDecision.cs` — pure aggregate status and recommended-action decision.
- `src/CodeKnowledge.Core/Validation/SymbolRangeLocator.cs` — mapped-range check and whole-file normalized-hash search.
- `src/CodeKnowledge.Core/Git/GitComparison.cs` — structured Git snapshot, diff, rename, and hunk contracts.
- `src/CodeKnowledge.Infrastructure/Git/GitDiffParser.cs` — converts Git output into Core contracts.
- `src/CodeKnowledge.Core/Validation/EvidenceValidator.cs` — classifies one Evidence with per-run caches.
- `src/CodeKnowledge.Core/Validation/ValidateKnowledgeUseCase.cs` — project-scoped orchestration, dirty correlation, and aggregate response.
- `tests/CodeKnowledge.Core.Tests/ValidationModelsTests.cs`
- `tests/CodeKnowledge.Core.Tests/ValidationDecisionTests.cs`
- `tests/CodeKnowledge.Core.Tests/SymbolRangeLocatorTests.cs`
- `tests/CodeKnowledge.Core.Tests/ValidateKnowledgeUseCaseTests.cs`

### Modify

- `src/CodeKnowledge.Core/Hashing/ContentHasher.cs` — expose shared line-count semantics.
- `src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs` — remove duplicate line counting.
- `src/CodeKnowledge.Core/Git/IGitRepository.cs` — add read-only comparison operations.
- `src/CodeKnowledge.Infrastructure/Git/GitCliRepository.cs` — implement commit/diff/snapshot/status operations.
- `tests/CodeKnowledge.Core.Tests/Fakes/FakeGitRepository.cs` — support deterministic validation tests.
- `tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs` — real-Git integration coverage.
- `src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs` — expose the fifth Tool.
- `src/CodeKnowledge.Mcp/Program.cs` — register the use case.
- `tests/CodeKnowledge.Mcp.Tests/McpEndToEndTests.cs` — pin the wire contract and save/validate flow.
- `README.md` — document Phase 2 usage and Agent rules.

---

### Task 1: Structured Git comparison and dirty-state adapter

**Files:**
- Create: `src/CodeKnowledge.Core/Git/GitComparison.cs`
- Create: `src/CodeKnowledge.Infrastructure/Git/GitDiffParser.cs`
- Modify: `src/CodeKnowledge.Core/Git/IGitRepository.cs`
- Modify: `src/CodeKnowledge.Infrastructure/Git/GitCliRepository.cs`
- Modify: `tests/CodeKnowledge.Core.Tests/Fakes/FakeGitRepository.cs`
- Modify: `tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs`

**Interfaces:**
- Consumes: existing `GitCommandRunner.Run` and `RunBytes`.
- Produces: `ResolveCommit`, `CompareCommits`, `TryReadFileAtCommit`, `GetWorkingTreeChangedPaths`, `GitCommitDiff.ResolveTargetPath`, and `GitCommitDiff.MapOldLineToNew`.

- [x] **Step 1: Add failing real-Git integration tests**

Add `using CodeKnowledge.Core.Git;` and append these tests to `GitCliRepositoryTests.cs`:

```csharp
[Fact]
public void ResolveCommit_accepts_HEAD_short_hash_and_tag_and_rejects_unknown()
{
    using var repo = new TestGitRepo();
    var commit = repo.CommitFile("src/a.txt", "one\n");
    repo.Run("tag", "saved");
    Assert.Equal(commit, _repository.ResolveCommit(repo.Root, "HEAD"));
    Assert.Equal(commit, _repository.ResolveCommit(repo.Root, commit));
    Assert.Equal(commit, _repository.ResolveCommit(repo.Root, commit[..8]));
    Assert.Equal(commit, _repository.ResolveCommit(repo.Root, "saved"));
    Assert.Null(_repository.ResolveCommit(repo.Root, "does-not-exist"));
    Assert.Null(_repository.ResolveCommit(repo.Root, "HEAD;touch injected"));
    Assert.False(File.Exists(Path.Combine(repo.Root, "injected")));
}

[Fact]
public void CompareCommits_returns_rename_and_line_mapping()
{
    using var repo = new TestGitRepo();
    var before = repo.CommitFile("src/Old Name.cs", "one\ntwo\nthree\n");
    repo.Run("mv", "src/Old Name.cs", "src/新 Name.cs");
    File.WriteAllText(Path.Combine(repo.Root, "src", "新 Name.cs"),
        "zero\none\nTWO\nthree\n");
    repo.Run("add", "-A");
    repo.Run("commit", "-m", "rename and change");
    var after = repo.Run("rev-parse", "HEAD").Trim();

    var diff = _repository.CompareCommits(repo.Root, before, after);

    Assert.NotNull(diff);
    Assert.Equal("src/新 Name.cs", diff.ResolveTargetPath("src/Old Name.cs"));
    Assert.Equal(2, diff.MapOldLineToNew("src/Old Name.cs", 1));
}

[Fact]
public void CompareCommits_returns_content_preserving_rename()
{
    using var repo = new TestGitRepo();
    var before = repo.CommitFile("src/old.cs", "same\ncontent\n");
    repo.Run("mv", "src/old.cs", "src/moved.cs");
    repo.Run("commit", "-m", "move only");
    var diff = _repository.CompareCommits(
        repo.Root, before, repo.Run("rev-parse", "HEAD").Trim());
    var change = Assert.Single(diff!.Files);
    Assert.Equal(GitChangeKind.Renamed, change.Kind);
    Assert.Equal("src/old.cs", change.OldPath);
    Assert.Equal("src/moved.cs", change.NewPath);
}

[Fact]
public void CompareCommits_maps_lines_after_a_deletion()
{
    using var repo = new TestGitRepo();
    var before = repo.CommitFile("src/a.cs", "one\nremoved\nthree\n");
    repo.CommitFile("src/a.cs", "one\nthree\n", "delete middle line");
    var diff = _repository.CompareCommits(
        repo.Root, before, repo.Run("rev-parse", "HEAD").Trim());
    Assert.Equal(2, diff!.MapOldLineToNew("src/a.cs", 3));
}

[Fact]
public void CompareCommits_maps_deleted_file_to_null()
{
    using var repo = new TestGitRepo();
    var before = repo.CommitFile("src/deleted.cs", "x\n");
    File.Delete(Path.Combine(repo.Root, "src", "deleted.cs"));
    repo.Run("add", "-A");
    repo.Run("commit", "-m", "delete");
    var diff = _repository.CompareCommits(
        repo.Root, before, repo.Run("rev-parse", "HEAD").Trim());
    Assert.NotNull(diff);
    Assert.Null(diff.ResolveTargetPath("src/deleted.cs"));
}

[Fact]
public void CompareCommits_reports_added_modified_and_deleted_files()
{
    using var repo = new TestGitRepo();
    repo.CommitFile("modified.cs", "before\n");
    var before = repo.CommitFile("deleted.cs", "delete\n");
    File.WriteAllText(Path.Combine(repo.Root, "modified.cs"), "after\n");
    File.Delete(Path.Combine(repo.Root, "deleted.cs"));
    File.WriteAllText(Path.Combine(repo.Root, "added.cs"), "add\n");
    repo.Run("add", "-A");
    repo.Run("commit", "-m", "all change kinds");
    var diff = _repository.CompareCommits(
        repo.Root, before, repo.Run("rev-parse", "HEAD").Trim());
    Assert.NotNull(diff);
    Assert.Contains(diff.Files, value => value.Kind == GitChangeKind.Added);
    Assert.Contains(diff.Files, value => value.Kind == GitChangeKind.Modified);
    Assert.Contains(diff.Files, value => value.Kind == GitChangeKind.Deleted);
}

[Fact]
public void TryReadFileAtCommit_distinguishes_available_and_missing()
{
    using var repo = new TestGitRepo();
    var commit = repo.CommitFile("src/a.txt", "hello");
    var present = _repository.TryReadFileAtCommit(repo.Root, commit, "src/a.txt");
    var missing = _repository.TryReadFileAtCommit(repo.Root, commit, "src/missing.txt");
    Assert.Equal(GitFileSnapshotStatus.Available, present.Status);
    Assert.Equal("hello", present.Content);
    Assert.Equal(GitFileSnapshotStatus.Missing, missing.Status);
}

[Fact]
public void TryReadFileAtCommit_treats_pathspec_characters_as_literal()
{
    using var repo = new TestGitRepo();
    Directory.CreateDirectory(Path.Combine(repo.Root, "src"));
    File.WriteAllText(Path.Combine(repo.Root, "src", "[literal].txt"), "literal");
    repo.Run("add", "--", ":(literal)src/[literal].txt");
    repo.Run("commit", "-m", "literal pathspec name");
    var commit = repo.Run("rev-parse", "HEAD").Trim();
    var snapshot = _repository.TryReadFileAtCommit(
        repo.Root, commit, "src/[literal].txt");
    Assert.Equal(GitFileSnapshotStatus.Available, snapshot.Status);
    Assert.Equal("literal", snapshot.Content);
}

[Fact]
public void GetWorkingTreeChangedPaths_includes_modified_deleted_and_rename_paths()
{
    using var repo = new TestGitRepo();
    repo.CommitFile("a.txt", "a");
    repo.CommitFile("b.txt", "b");
    repo.CommitFile("c.txt", "c");
    File.WriteAllText(Path.Combine(repo.Root, "a.txt"), "changed");
    File.Delete(Path.Combine(repo.Root, "b.txt"));
    repo.Run("mv", "c.txt", "renamed c.txt");
    var paths = _repository.GetWorkingTreeChangedPaths(repo.Root);
    Assert.NotNull(paths);
    Assert.Contains("a.txt", paths);
    Assert.Contains("b.txt", paths);
    Assert.Contains("c.txt", paths);
    Assert.Contains("renamed c.txt", paths);
}
```

- [x] **Step 2: Run the focused integration tests and verify RED**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests --configuration Release --filter FullyQualifiedName~GitCliRepositoryTests`

Expected: compilation fails because the structured Git contracts are absent.

- [x] **Step 3: Add the Core Git contracts and interface methods**

Create `GitComparison.cs`:

```csharp
namespace CodeKnowledge.Core.Git;

public enum GitFileSnapshotStatus { Available, Missing, Unavailable }
public enum GitChangeKind { Added, Modified, Deleted, Renamed, Copied }

public sealed record GitFileSnapshot(GitFileSnapshotStatus Status, string? Content)
{
    public static GitFileSnapshot Available(string content) =>
        new(GitFileSnapshotStatus.Available, content);
    public static GitFileSnapshot Missing() => new(GitFileSnapshotStatus.Missing, null);
    public static GitFileSnapshot Unavailable() => new(GitFileSnapshotStatus.Unavailable, null);
}

public sealed record GitDiffHunk(int OldStart, int OldCount, int NewStart, int NewCount);

public sealed record GitFileChange(
    GitChangeKind Kind, string OldPath, string? NewPath, IReadOnlyList<GitDiffHunk> Hunks)
{
    public string? TargetPath => Kind == GitChangeKind.Deleted ? null : NewPath;

    public int MapOldLineToNew(int oldLine)
    {
        var delta = 0;
        foreach (var hunk in Hunks.OrderBy(value => value.OldStart))
        {
            if (oldLine < hunk.OldStart) break;
            if (hunk.OldCount == 0) { delta += hunk.NewCount; continue; }
            if (oldLine <= hunk.OldStart + hunk.OldCount - 1)
                return Math.Max(1, hunk.NewStart);
            delta += hunk.NewCount - hunk.OldCount;
        }
        return Math.Max(1, oldLine + delta);
    }
}

public sealed record GitCommitDiff(IReadOnlyList<GitFileChange> Files)
{
    public string? ResolveTargetPath(string originalPath)
    {
        var change = Find(originalPath);
        return change is null ? originalPath : change.TargetPath;
    }
    public int MapOldLineToNew(string originalPath, int oldLine)
        => Find(originalPath)?.MapOldLineToNew(oldLine) ?? oldLine;
    private GitFileChange? Find(string path) => Files.FirstOrDefault(change =>
        string.Equals(change.OldPath, path, StringComparison.Ordinal));
}
```

Extend `IGitRepository` with:

```csharp
string? ResolveCommit(string repositoryRoot, string commitish);
GitCommitDiff? CompareCommits(string repositoryRoot, string baseCommit, string targetCommit);
GitFileSnapshot TryReadFileAtCommit(
    string repositoryRoot, string commitHash, string repoRelativePath);
IReadOnlySet<string>? GetWorkingTreeChangedPaths(string repositoryRoot);
```

- [x] **Step 4: Implement the Git output parser**

Create `GitDiffParser.cs`. Parse `git diff --name-status -z` by status token, keep rename old/new paths, then attach `@@ -old,count +new,count @@` hunks to the same ordered file entry from the patch. Parse `git status --porcelain=v1 -z` by adding both paths for rename/copy entries. Use this implementation:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Infrastructure.Git;

internal static partial class GitDiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeader();

    public static IReadOnlyList<GitFileChange> ParseChanges(byte[] raw, string patch)
    {
        var fields = Encoding.UTF8.GetString(raw).Split('\0');
        var changes = new List<GitFileChange>();
        for (var i = 0; i < fields.Length && fields[i].Length > 0;)
        {
            var status = fields[i++];
            var code = status[0];
            if (code is 'R' or 'C')
                changes.Add(new(code == 'R' ? GitChangeKind.Renamed : GitChangeKind.Copied,
                    fields[i++], fields[i++], []));
            else
            {
                var path = fields[i++];
                var kind = code switch
                {
                    'A' => GitChangeKind.Added,
                    'D' => GitChangeKind.Deleted,
                    _ => GitChangeKind.Modified,
                };
                changes.Add(new(kind, path, kind == GitChangeKind.Deleted ? null : path, []));
            }
        }

        var hunks = changes.Select(_ => new List<GitDiffHunk>()).ToList();
        var file = -1;
        foreach (var line in patch.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) { file++; continue; }
            var match = HunkHeader().Match(line);
            if (!match.Success || file < 0 || file >= hunks.Count) continue;
            static int Count(Group value) => value.Success ? int.Parse(value.Value) : 1;
            hunks[file].Add(new(
                int.Parse(match.Groups[1].Value), Count(match.Groups[2]),
                int.Parse(match.Groups[3].Value), Count(match.Groups[4])));
        }
        return changes.Select((change, index) => change with { Hunks = hunks[index] }).ToList();
    }

    public static IReadOnlySet<string> ParseWorkingTreePaths(byte[] raw)
    {
        var fields = Encoding.UTF8.GetString(raw).Split('\0');
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length && fields[i].Length > 0; i++)
        {
            var field = fields[i];
            if (field.Length < 4) continue;
            var status = field[..2];
            paths.Add(field[3..]);
            if ((status.Contains('R') || status.Contains('C')) && i + 1 < fields.Length)
                paths.Add(fields[++i]);
        }
        return paths;
    }
}
```

- [x] **Step 5: Implement `GitCliRepository` comparison methods**

Add these methods; keep the existing throwing `ReadFileAtCommit` for `save_knowledge`:

```csharp
public string? ResolveCommit(string root, string commitish)
{
    var result = GitCommandRunner.Run(
        root, "rev-parse", "--verify", "--end-of-options", $"{commitish}^{{commit}}");
    return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
}

public GitCommitDiff? CompareCommits(string root, string baseCommit, string targetCommit)
{
    var names = GitCommandRunner.RunBytes(root, "diff", "--find-renames",
        "--no-textconv", "--no-ext-diff", "--name-status", "-z", baseCommit, targetCommit, "--");
    var patch = GitCommandRunner.Run(root, "diff", "--find-renames",
        "--no-textconv", "--no-ext-diff", "--unified=0", "--no-color", baseCommit, targetCommit, "--");
    return names.ExitCode == 0 && patch.ExitCode == 0
        ? new GitCommitDiff(GitDiffParser.ParseChanges(names.StandardOutput, patch.StandardOutput))
        : null;
}

public GitFileSnapshot TryReadFileAtCommit(string root, string commit, string path)
{
    var tree = GitCommandRunner.RunBytes(
        root, "ls-tree", "-z", "--full-tree", commit, "--", $":(literal){path}");
    if (tree.ExitCode != 0) return GitFileSnapshot.Unavailable();
    if (tree.StandardOutput.Length == 0) return GitFileSnapshot.Missing();
    var result = GitCommandRunner.RunBytes(root, "show", $"{commit}:{path}");
    return result.ExitCode == 0
        ? GitFileSnapshot.Available(System.Text.Encoding.UTF8.GetString(result.StandardOutput))
        : GitFileSnapshot.Unavailable();
}

public IReadOnlySet<string>? GetWorkingTreeChangedPaths(string root)
{
    var result = GitCommandRunner.RunBytes(
        root, "status", "--porcelain=v1", "-z", "--untracked-files=all");
    return result.ExitCode == 0
        ? GitDiffParser.ParseWorkingTreePaths(result.StandardOutput)
        : null;
}
```

- [x] **Step 6: Update the Core fake for the expanded interface**

Add commit-aware snapshots, unavailable commits, configurable diff, and nullable dirty paths. Preserve `FilesAtCommit` as the fallback used by Phase 1 save tests:

```csharp
public HashSet<string> UnavailableCommits { get; } = new(StringComparer.Ordinal);
public Dictionary<(string Commit, string Path), GitFileSnapshot> Snapshots { get; } = [];
public Dictionary<(string Base, string Target), GitCommitDiff?> Diffs { get; } = [];
public GitCommitDiff? DefaultCommitDiff { get; set; } = new([]);
public Dictionary<(string Commit, string Path), int> SnapshotReadCounts { get; } = [];
public IReadOnlySet<string>? WorkingTreeChangedPaths { get; set; } =
    new HashSet<string>(StringComparer.Ordinal);

public string? ResolveCommit(string repositoryRoot, string commitish)
    => UnavailableCommits.Contains(commitish)
        ? null : commitish == "HEAD" ? Context?.HeadCommit : commitish;

public GitCommitDiff? CompareCommits(string repositoryRoot, string baseCommit, string targetCommit)
    => Diffs.TryGetValue((baseCommit, targetCommit), out var diff)
        ? diff : DefaultCommitDiff;

public GitFileSnapshot TryReadFileAtCommit(string repositoryRoot, string commit, string path)
{
    var key = (commit, path);
    SnapshotReadCounts[key] = SnapshotReadCounts.GetValueOrDefault(key) + 1;
    return Snapshots.TryGetValue(key, out var snapshot)
        ? snapshot
        : FilesAtCommit.TryGetValue(path, out var content)
            ? GitFileSnapshot.Available(content) : GitFileSnapshot.Missing();
}

public IReadOnlySet<string>? GetWorkingTreeChangedPaths(string repositoryRoot)
    => WorkingTreeChangedPaths;
```

- [x] **Step 7: Run Core and Infrastructure tests and verify GREEN**

Run:

```bash
dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --maxcpucount:1 --disable-build-servers
dotnet test tests/CodeKnowledge.Infrastructure.Tests --configuration Release --maxcpucount:1 --disable-build-servers
```

Expected: both projects pass; no existing Phase 1 Git or save test regresses.

- [x] **Step 8: Commit the Git comparison adapter**

```bash
git add src/CodeKnowledge.Core/Git \
  src/CodeKnowledge.Infrastructure/Git \
  tests/CodeKnowledge.Core.Tests/Fakes/FakeGitRepository.cs \
  tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs
git commit -m "feat: expose structured git freshness data"
```

---

### Task 2: Pure validation contracts and hash-range decisions

**Files:**
- Create: `src/CodeKnowledge.Core/Validation/ValidationModels.cs`
- Create: `src/CodeKnowledge.Core/Validation/ValidationDecision.cs`
- Create: `src/CodeKnowledge.Core/Validation/SymbolRangeLocator.cs`
- Modify: `src/CodeKnowledge.Core/Hashing/ContentHasher.cs`
- Modify: `src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ValidationModelsTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ValidationDecisionTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/SymbolRangeLocatorTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ContentHasherTests.cs`

**Interfaces:**
- Consumes: `ContentHasher.ComputeFileHash(string)` and `ContentHasher.ComputeSymbolHash(string, int, int)`.
- Produces: `ValidationStatus`, `EvidenceValidationStatus`, `ValidationReason`, `RecommendedAction`, result records, `ValidationDecision.Decide`, `SymbolRangeLocator.Find`, and `ContentHasher.CountLines`.

- [x] **Step 1: Write failing wire-name and aggregate-decision tests**

Create `ValidationModelsTests.cs` and `ValidationDecisionTests.cs`:

```csharp
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
```

```csharp
using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class ValidationDecisionTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_applies_the_approved_priority(
        EvidenceValidationStatus[] evidence, bool? dirty,
        ValidationStatus expectedStatus, RecommendedAction expectedAction)
    {
        var result = ValidationDecision.Decide(evidence, dirty);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedAction, result.RecommendedAction);
    }

    public static TheoryData<EvidenceValidationStatus[], bool?, ValidationStatus, RecommendedAction>
        Cases => new()
        {
            { [], false, ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
            { [EvidenceValidationStatus.Unchanged], false,
                ValidationStatus.Valid, RecommendedAction.ReuseKnowledge },
            { [EvidenceValidationStatus.Unchanged], true,
                ValidationStatus.Valid, RecommendedAction.InspectDirtyEvidence },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Changed], false,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Changed], true,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Missing], false,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Changed, EvidenceValidationStatus.Missing], false,
                ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge },
            { [EvidenceValidationStatus.Changed, EvidenceValidationStatus.Missing], true,
                ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Unknown], false,
                ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
            { [EvidenceValidationStatus.Unchanged], null,
                ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
        };
}
```

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test tests/CodeKnowledge.Core.Tests --configuration Release \
  --filter 'FullyQualifiedName~ValidationModelsTests|FullyQualifiedName~ValidationDecisionTests'
```

Expected: compilation fails because `CodeKnowledge.Core.Validation` does not exist.

- [x] **Step 3: Add the complete validation wire models and decision function**

Create `ValidationModels.cs`:

```csharp
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
```

Create `ValidationDecision.cs`:

```csharp
namespace CodeKnowledge.Core.Validation;

public sealed record ValidationDecisionResult(
    ValidationStatus Status, RecommendedAction RecommendedAction);

public static class ValidationDecision
{
    public static ValidationDecisionResult Decide(
        IReadOnlyList<EvidenceValidationStatus> evidence, bool? dirty)
    {
        if (dirty is null || evidence.Count == 0 ||
            evidence.Contains(EvidenceValidationStatus.Unknown))
            return new(ValidationStatus.Unknown, RecommendedAction.InspectEvidence);
        if (evidence.All(value => value == EvidenceValidationStatus.Unchanged))
            return new(ValidationStatus.Valid,
                dirty.Value ? RecommendedAction.InspectDirtyEvidence : RecommendedAction.ReuseKnowledge);
        if (evidence.All(value =>
                value is EvidenceValidationStatus.Changed or EvidenceValidationStatus.Missing))
            return new(ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge);
        return new(ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols);
    }
}
```

- [x] **Step 4: Add failing shared line-count and symbol-location tests**

Add to `ContentHasherTests.cs`:

```csharp
[Theory]
[InlineData("", 0)]
[InlineData("one", 1)]
[InlineData("one\n", 1)]
[InlineData("one\r\ntwo\r\n", 2)]
public void CountLines_matches_symbol_hash_semantics(string content, int expected)
    => Assert.Equal(expected, ContentHasher.CountLines(content));
```

Create `SymbolRangeLocatorTests.cs`:

```csharp
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class SymbolRangeLocatorTests
{
    private const string Symbol = "void Send()\n{\n    Mail();\n}\n";

    [Fact]
    public void Find_prefers_the_diff_mapped_range()
    {
        var content = "header\n" + Symbol + "footer\n";
        var hash = ContentHasher.ComputeSymbolHash(content, 2, 5);
        var match = new SymbolRangeLocator(content).Find(hash, 4, 2);
        Assert.NotNull(match);
        Assert.Equal(2, match.StartLine);
        Assert.Equal(ValidationReason.SymbolHashMatchAtMappedRange, match.Reason);
    }

    [Fact]
    public void Find_scans_the_file_when_the_symbol_moved()
    {
        var target = "line1\nline2\n" + Symbol;
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var match = new SymbolRangeLocator(target).Find(hash, 4, 1);
        Assert.NotNull(match);
        Assert.Equal(3, match.StartLine);
        Assert.Equal(ValidationReason.SymbolHashMatchAtMovedRange, match.Reason);
    }

    [Fact]
    public void Find_returns_null_when_the_symbol_changed()
    {
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var changed = Symbol.Replace("Mail();", "Queue();", StringComparison.Ordinal);
        Assert.Null(new SymbolRangeLocator(changed).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_ignores_the_approved_whitespace_differences()
    {
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var whitespaceOnly = Symbol.Replace("    Mail();", "\tMail();   ",
            StringComparison.Ordinal);
        Assert.NotNull(new SymbolRangeLocator(whitespaceOnly).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_treats_comment_changes_as_content_changes()
    {
        const string withComment = "void Send()\n{\n    Mail(); // old\n}\n";
        var hash = ContentHasher.ComputeSymbolHash(withComment, 1, 4);
        var changed = withComment.Replace("// old", "// new", StringComparison.Ordinal);
        Assert.Null(new SymbolRangeLocator(changed).Find(hash, 4, 1));
    }

    [Fact]
    public void Find_accepts_duplicate_windows_and_reuses_the_window_index()
    {
        var target = Symbol + "separator\n" + Symbol;
        var hash = ContentHasher.ComputeSymbolHash(Symbol, 1, 4);
        var locator = new SymbolRangeLocator(target);
        Assert.Equal(1, locator.Find(hash, 4, 99)!.StartLine);
        Assert.Equal(1, locator.Find(hash, 4, 99)!.StartLine);
    }
}
```

- [x] **Step 5: Run the focused tests and verify RED**

Run: `dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --filter 'FullyQualifiedName~ContentHasherTests|FullyQualifiedName~SymbolRangeLocatorTests'`

Expected: compilation fails because `CountLines` and `SymbolRangeLocator` do not exist.

- [x] **Step 6: Implement shared line counting and symbol location**

Refactor `ContentHasher` so the public string overload delegates to one normalization path. The internal normalized-line overload lets one `SymbolRangeLocator` reuse normalized lines for every window:

```csharp
public static string ComputeSymbolHash(string fileContent, int startLine, int endLine)
    => ComputeSymbolHash(NormalizeSymbolLines(fileContent), startLine, endLine);

public static int CountLines(string content) => SplitLines(content).Length;

internal static string[] NormalizeSymbolLines(string content)
{
    var lines = SplitLines(content);
    for (var index = 0; index < lines.Length; index++)
        lines[index] = ConsecutiveSpaces().Replace(lines[index], " ").TrimEnd();
    return lines;
}

internal static string ComputeSymbolHash(
    IReadOnlyList<string> normalizedLines, int startLine, int endLine)
{
    var from = Math.Max(1, startLine);
    var to = Math.Min(normalizedLines.Count, endLine);
    var normalized = new StringBuilder();
    for (var lineNumber = from; lineNumber <= to; lineNumber++)
        normalized.Append(normalizedLines[lineNumber - 1]).Append('\n');
    return ComputeFileHash(normalized.ToString());
}

private static string[] SplitLines(string content)
{
    var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    return lines.Length > 0 && lines[^1] == "" ? lines[..^1] : lines;
}
```

In `SaveKnowledgeUseCase.BuildEvidence`, replace `CountLines(content)` with `ContentHasher.CountLines(content)` and delete the private duplicate.

Create `SymbolRangeLocator.cs`:

```csharp
using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Validation;

public sealed record SymbolRangeMatch(int StartLine, ValidationReason Reason);

public sealed class SymbolRangeLocator
{
    private readonly IReadOnlyList<string> _normalizedLines;
    private readonly Dictionary<int, IReadOnlyDictionary<string, int>> _windowIndexes = [];

    public SymbolRangeLocator(string content)
        => _normalizedLines = ContentHasher.NormalizeSymbolLines(content);

    public SymbolRangeMatch? Find(
        string expectedHash, int windowLength, int mappedStartLine)
    {
        if (windowLength < 1 || windowLength > _normalizedLines.Count) return null;
        if (Matches(expectedHash, windowLength, mappedStartLine))
            return new(mappedStartLine, ValidationReason.SymbolHashMatchAtMappedRange);
        return WindowIndex(windowLength).TryGetValue(expectedHash, out var start)
            ? new(start, ValidationReason.SymbolHashMatchAtMovedRange) : null;
    }

    private bool Matches(string expectedHash, int length, int start)
        => start >= 1 && start + length - 1 <= _normalizedLines.Count &&
           string.Equals(expectedHash,
               ContentHasher.ComputeSymbolHash(_normalizedLines, start, start + length - 1),
               StringComparison.Ordinal);

    private IReadOnlyDictionary<string, int> WindowIndex(int length)
    {
        if (_windowIndexes.TryGetValue(length, out var cached)) return cached;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var start = 1; start <= _normalizedLines.Count - length + 1; start++)
        {
            var hash = ContentHasher.ComputeSymbolHash(
                _normalizedLines, start, start + length - 1);
            index.TryAdd(hash, start);
        }
        return _windowIndexes[length] = index;
    }
}
```

- [x] **Step 7: Run all Core tests and verify GREEN**

Run: `dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --maxcpucount:1 --disable-build-servers`

Expected: zero failures and zero warnings.

- [x] **Step 8: Commit the pure validation layer**

```bash
git add src/CodeKnowledge.Core/Validation \
  src/CodeKnowledge.Core/Hashing/ContentHasher.cs \
  src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs \
  tests/CodeKnowledge.Core.Tests/ValidationModelsTests.cs \
  tests/CodeKnowledge.Core.Tests/ValidationDecisionTests.cs \
  tests/CodeKnowledge.Core.Tests/SymbolRangeLocatorTests.cs \
  tests/CodeKnowledge.Core.Tests/ContentHasherTests.cs
git commit -m "feat: add freshness validation decisions"
```

---

### Task 3: Project-scoped validation use case

**Files:**
- Create: `src/CodeKnowledge.Core/Validation/EvidenceValidator.cs`
- Create: `src/CodeKnowledge.Core/Validation/ValidateKnowledgeUseCase.cs`
- Create: `tests/CodeKnowledge.Core.Tests/ValidateKnowledgeUseCaseTests.cs`
- Modify: `tests/CodeKnowledge.Core.Tests/Fakes/FakeKnowledgeStore.cs`

**Interfaces:**
- Consumes: `ResolveProjectUseCase`, `IKnowledgeStore.GetDetail(projectId, knowledgeId, null)`, Task 1 Git comparison methods, and Task 2 validation decisions.
- Produces: `ValidateKnowledgeUseCase.Execute(ValidateKnowledgeRequest) : ValidateKnowledgeResult`.

- [x] **Step 1: Make project scoping observable in `FakeKnowledgeStore`**

Add these properties and assignments without changing return behavior:

```csharp
public string? LastGetDetailProjectId { get; private set; }
public string? LastGetDetailKnowledgeId { get; private set; }
public string? LastGetDetailVersionId { get; private set; }

public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
{
    LastGetDetailProjectId = projectId;
    LastGetDetailKnowledgeId = knowledgeId;
    LastGetDetailVersionId = versionId;
    return Detail;
}
```

- [x] **Step 2: Write failing use-case tests for all approved classifications**

Create `ValidateKnowledgeUseCaseTests.cs` with the imports, fixture, and helpers below:

```csharp
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
```

- [x] **Step 3: Run the use-case tests and verify RED**

Run: `dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --filter FullyQualifiedName~ValidateKnowledgeUseCaseTests`

Expected: compilation fails because `ValidateKnowledgeUseCase` does not exist.

- [x] **Step 4: Implement the single-Evidence validator with per-run snapshot caching**

Create `EvidenceValidator.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Validation;

internal sealed class EvidenceValidator(
    IGitRepository git, string repositoryRoot,
    string baseCommit, string targetCommit, GitCommitDiff diff)
{
    private readonly Dictionary<(string Commit, string Path), CachedFile> _files = [];

    public EvidenceValidationResult Validate(EvidenceRecord evidence, bool? dirty)
    {
        var label = Label(evidence);
        var targetPath = diff.ResolveTargetPath(evidence.FilePath);
        if (targetPath is null)
            return Result(EvidenceValidationStatus.Missing,
                ValidationReason.TargetFileMissing, null);

        var target = File(targetCommit, targetPath);
        if (target.Snapshot.Status == GitFileSnapshotStatus.Missing)
            return Result(EvidenceValidationStatus.Missing,
                ValidationReason.TargetFileMissing, targetPath);
        if (target.Snapshot.Status != GitFileSnapshotStatus.Available ||
            target.Snapshot.Content is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.TargetFileUnavailable, targetPath);
        if (string.Equals(target.FileHash, evidence.FileHash, StringComparison.Ordinal))
            return Result(EvidenceValidationStatus.Unchanged,
                ValidationReason.FileHashMatch, targetPath);
        if (evidence.SymbolHash is null || evidence.StartLine is null || evidence.EndLine is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.SymbolHashUnavailable, targetPath);

        var source = File(baseCommit, evidence.FilePath);
        if (source.Snapshot.Status != GitFileSnapshotStatus.Available ||
            source.Snapshot.Content is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.BaseFileUnavailable, targetPath);
        var effectiveEnd = Math.Min(source.LineCount, evidence.EndLine.Value);
        var windowLength = effectiveEnd - evidence.StartLine.Value + 1;
        if (windowLength < 1)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.SymbolHashUnavailable, targetPath);

        var mappedStart = diff.MapOldLineToNew(evidence.FilePath, evidence.StartLine.Value);
        var match = target.Locator!.Find(
            evidence.SymbolHash, windowLength, mappedStart);
        return match is null
            ? Result(EvidenceValidationStatus.Changed,
                ValidationReason.SymbolHashNotFound, targetPath)
            : Result(EvidenceValidationStatus.Unchanged, match.Reason, targetPath);

        EvidenceValidationResult Result(
            EvidenceValidationStatus status, ValidationReason reason, string? path)
            => new(evidence.Id, label, evidence.FilePath, path, status, reason, dirty);
    }

    private CachedFile File(string commit, string path)
    {
        var key = (commit, path);
        if (!_files.TryGetValue(key, out var value))
            _files[key] = value = new CachedFile(
                git.TryReadFileAtCommit(repositoryRoot, commit, path));
        return value;
    }

    internal static string Label(EvidenceRecord evidence)
    {
        if (!string.IsNullOrWhiteSpace(evidence.SymbolId)) return evidence.SymbolId;
        if (!string.IsNullOrWhiteSpace(evidence.Signature)) return evidence.Signature;
        return !string.IsNullOrWhiteSpace(evidence.SymbolName)
            ? evidence.SymbolName : evidence.FilePath;
    }

    private sealed class CachedFile
    {
        public CachedFile(GitFileSnapshot snapshot)
        {
            Snapshot = snapshot;
            if (snapshot.Content is null) return;
            FileHash = ContentHasher.ComputeFileHash(snapshot.Content);
            LineCount = ContentHasher.CountLines(snapshot.Content);
            Locator = new SymbolRangeLocator(snapshot.Content);
        }

        public GitFileSnapshot Snapshot { get; }
        public string? FileHash { get; }
        public int LineCount { get; }
        public SymbolRangeLocator? Locator { get; }
    }
}
```

- [x] **Step 5: Implement orchestration, early unknown results, dirty correlation, and list projection**

Create `ValidateKnowledgeUseCase.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Validation;

public sealed class ValidateKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IGitRepository git, IKnowledgeStore store)
{
    public ValidateKnowledgeResult Execute(ValidateKnowledgeRequest request)
    {
        ValidateArguments(request);
        var project = resolveProject.Execute(request.WorkingDirectory);
        var detail = store.GetDetail(project.ProjectId, request.KnowledgeId, versionId: null)
            ?? throw new CodeKnowledgeException(CodeKnowledgeException.KnowledgeNotFound,
                $"Knowledge '{request.KnowledgeId}' was not found in project '{project.ProjectId}'.");
        var requestedTarget = request.TargetCommit?.Trim();
        var baseCommit = git.ResolveCommit(project.RepositoryRoot, detail.CommitHash);
        var targetCommit = requestedTarget is null
            ? project.CurrentCommit
            : git.ResolveCommit(project.RepositoryRoot, requestedTarget);
        if (baseCommit is null)
            return Unavailable(detail, targetCommit, ValidationReason.CommitUnavailable,
                $"base_commit_unavailable: {detail.CommitHash}");
        if (targetCommit is null)
            return Unavailable(detail, null, ValidationReason.CommitUnavailable,
                $"target_commit_unavailable: {requestedTarget}");

        var targetDiff = git.CompareCommits(project.RepositoryRoot, baseCommit, targetCommit);
        if (targetDiff is null)
            return Unavailable(detail, targetCommit, ValidationReason.DiffUnavailable,
                "diff_unavailable");
        var headDiff = string.Equals(targetCommit, project.CurrentCommit, StringComparison.Ordinal)
            ? targetDiff
            : git.CompareCommits(project.RepositoryRoot, baseCommit, project.CurrentCommit);
        var changedPaths = git.GetWorkingTreeChangedPaths(project.RepositoryRoot);
        var dirtyAvailable = headDiff is not null && changedPaths is not null;

        var validator = new EvidenceValidator(
            git, project.RepositoryRoot, baseCommit, targetCommit, targetDiff);
        var details = detail.Evidence.Select(evidence =>
        {
            bool? dirty = null;
            if (dirtyAvailable)
            {
                var currentPath = headDiff!.ResolveTargetPath(evidence.FilePath);
                dirty = changedPaths!.Contains(evidence.FilePath) ||
                    currentPath is not null && changedPaths.Contains(currentPath);
            }
            return validator.Validate(evidence, dirty);
        }).ToList();

        bool? anyDirty = dirtyAvailable
            ? details.Any(value => value.IsWorkingTreeDirty == true)
            : null;
        var decision = ValidationDecision.Decide(
            details.Select(value => value.Status).ToList(), anyDirty);
        IReadOnlyList<string> warnings = dirtyAvailable ? [] : ["dirty_check_unavailable"];
        return Build(baseCommit, targetCommit, anyDirty, details, decision, warnings);
    }

    private static void ValidateArguments(ValidateKnowledgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(request.KnowledgeId) ||
            request.TargetCommit is not null && string.IsNullOrWhiteSpace(request.TargetCommit))
            throw new CodeKnowledgeException(CodeKnowledgeException.InvalidArguments,
                "workingDirectory and knowledgeId are required; targetCommit cannot be blank.");
    }

    private static ValidateKnowledgeResult Unavailable(
        KnowledgeDetail detail, string? targetCommit, ValidationReason reason, string warning)
    {
        var details = detail.Evidence.Select(evidence => new EvidenceValidationResult(
            evidence.Id, EvidenceValidator.Label(evidence), evidence.FilePath, null,
            EvidenceValidationStatus.Unknown, reason, null)).ToList();
        return Build(detail.CommitHash, targetCommit, null, details,
            new(ValidationStatus.Unknown, RecommendedAction.InspectEvidence), [warning]);
    }

    private static ValidateKnowledgeResult Build(
        string baseCommit, string? targetCommit, bool? dirty,
        IReadOnlyList<EvidenceValidationResult> details,
        ValidationDecisionResult decision, IReadOnlyList<string> warnings)
    {
        IReadOnlyList<string> Labels(EvidenceValidationStatus status) =>
            details.Where(value => value.Status == status).Select(value => value.Label).ToList();
        return new(
            decision.Status, baseCommit, targetCommit, dirty,
            Labels(EvidenceValidationStatus.Changed),
            Labels(EvidenceValidationStatus.Unchanged),
            Labels(EvidenceValidationStatus.Missing),
            Labels(EvidenceValidationStatus.Unknown),
            details.Where(value => value.IsWorkingTreeDirty == true)
                .Select(value => value.Label).ToList(),
            details, decision.RecommendedAction, warnings);
    }
}
```

- [x] **Step 6: Run the focused and complete Core suites**

Run:

```bash
dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --filter FullyQualifiedName~ValidateKnowledgeUseCaseTests
dotnet test tests/CodeKnowledge.Core.Tests --configuration Release --maxcpucount:1 --disable-build-servers
```

Expected: all classification, dirty, rename, project-scope, argument, and not-found tests pass; the complete Core suite has zero failures.

- [x] **Step 7: Commit the validation use case**

```bash
git add src/CodeKnowledge.Core/Validation/EvidenceValidator.cs \
  src/CodeKnowledge.Core/Validation/ValidateKnowledgeUseCase.cs \
  tests/CodeKnowledge.Core.Tests/ValidateKnowledgeUseCaseTests.cs \
  tests/CodeKnowledge.Core.Tests/Fakes/FakeKnowledgeStore.cs
git commit -m "feat: validate knowledge freshness"
```

---

### Task 4: Expose `validate_knowledge` through MCP and prove the published-server flow

**Files:**
- Modify: `src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs`
- Modify: `src/CodeKnowledge.Mcp/Program.cs`
- Modify: `tests/CodeKnowledge.Mcp.Tests/McpEndToEndTests.cs`

**Interfaces:**
- Consumes: `ValidateKnowledgeUseCase.Execute` from Task 3 and the existing `ToolGuard` error mapping.
- Produces: MCP Tool `validate_knowledge(workingDirectory, knowledgeId, targetCommit?)` with structured `ValidateKnowledgeResult` content.

- [x] **Step 1: Add failing published-server E2E tests**

In `McpEndToEndTests.cs`, rename `Lists_all_four_tools` to `Lists_all_five_tools` and pin the complete set:

```csharp
[Fact]
public async Task Lists_all_five_tools()
{
    using var cts = new CancellationTokenSource(CallTimeout);
    await using var client = await ConnectAsync(cts.Token);
    var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
    var names = tools.Select(tool => tool.Name).ToHashSet();
    Assert.Superset(new HashSet<string>
    {
        "resolve_project", "search_knowledge", "get_knowledge", "save_knowledge",
        "validate_knowledge",
    }, names);
}
```

Add these helpers below `SaveArguments`:

```csharp
private static Dictionary<string, object?> ValidateArguments(
    string repositoryRoot, string knowledgeId, string? targetCommit = null)
{
    var arguments = new Dictionary<string, object?>
    {
        ["workingDirectory"] = repositoryRoot,
        ["knowledgeId"] = knowledgeId,
    };
    if (targetCommit is not null) arguments["targetCommit"] = targetCommit;
    return arguments;
}

private static Dictionary<string, object?> SaveTwoEvidenceArguments(string repositoryRoot)
{
    var arguments = SaveArguments(repositoryRoot);
    arguments["evidence"] = new object[]
    {
        new Dictionary<string, object?>
        {
            ["filePath"] = "src/OrderService.cs",
            ["symbolName"] = "OrderService.Complete",
            ["symbolKind"] = "method",
            ["startLine"] = 1,
            ["endLine"] = 4,
        },
        new Dictionary<string, object?>
        {
            ["filePath"] = "src/MailSender.cs",
            ["symbolName"] = "MailSender.Send",
            ["symbolKind"] = "method",
            ["startLine"] = 1,
            ["endLine"] = 4,
        },
    };
    arguments["facts"] = new object[]
    {
        new Dictionary<string, object?>
        {
            ["text"] = "CompleteがMailSenderを利用する",
            ["evidenceIndexes"] = new[] { 0, 1 },
        },
    };
    return arguments;
}

private async Task<long> ReadVersionCountAsync(CancellationToken cancellationToken)
{
    await using var connection = new SqliteConnection(
        $"Data Source={DatabasePath};Pooling=False");
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM knowledge_versions;";
    return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
}
```

Add the complete save/validate status progression test:

```csharp
[Fact]
public async Task Validate_knowledge_reports_valid_partial_and_stale_for_real_commits()
{
    var baseCommit = _repo.CommitFile("src/MailSender.cs",
        "class MailSender\n{\n    void Send() { }\n}\n");
    using var cts = new CancellationTokenSource(CallTimeout);
    await using var client = await ConnectAsync(cts.Token);
    var save = await client.CallToolAsync(
        "save_knowledge", SaveTwoEvidenceArguments(_repo.Root),
        cancellationToken: cts.Token);
    Assert.NotEqual(true, save.IsError);
    var knowledgeId = JsonSerializer.SerializeToElement(save.StructuredContent)
        .GetProperty("knowledgeId").GetString()!;

    var valid = await client.CallToolAsync(
        "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId, baseCommit),
        cancellationToken: cts.Token);
    Assert.NotEqual(true, valid.IsError);
    var validJson = JsonSerializer.SerializeToElement(valid.StructuredContent);
    Assert.Equal("valid", validJson.GetProperty("status").GetString());
    Assert.Equal(baseCommit, validJson.GetProperty("targetCommit").GetString());
    Assert.False(validJson.GetProperty("isWorkingTreeDirty").GetBoolean());

    _repo.CommitFile("src/OrderService.cs",
        "class OrderService\n{\n    void Changed() { }\n}\n", "change first evidence");
    var partial = await client.CallToolAsync(
        "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId),
        cancellationToken: cts.Token);
    Assert.NotEqual(true, partial.IsError);
    var partialJson = JsonSerializer.SerializeToElement(partial.StructuredContent);
    Assert.Equal("partially_stale", partialJson.GetProperty("status").GetString());
    Assert.Equal(1, partialJson.GetProperty("changedEvidence").GetArrayLength());
    Assert.Equal(1, partialJson.GetProperty("unchangedEvidence").GetArrayLength());

    _repo.CommitFile("src/MailSender.cs",
        "class MailSender\n{\n    void Changed() { }\n}\n", "change second evidence");
    var stale = await client.CallToolAsync(
        "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId),
        cancellationToken: cts.Token);
    Assert.NotEqual(true, stale.IsError);
    var staleJson = JsonSerializer.SerializeToElement(stale.StructuredContent);
    Assert.Equal("stale", staleJson.GetProperty("status").GetString());
    Assert.Equal(2, staleJson.GetProperty("changedEvidence").GetArrayLength());
    Assert.Equal("reinvestigate_knowledge",
        staleJson.GetProperty("recommendedAction").GetString());
}
```

Add the AC-25 dirty test and the no-validation-persistence guard:

```csharp
[Fact]
public async Task Validate_knowledge_reports_dirty_without_persisting_a_version() // AC-25
{
    var baseCommit = _repo.Run("rev-parse", "HEAD").Trim();
    using var cts = new CancellationTokenSource(CallTimeout);
    await using var client = await ConnectAsync(cts.Token);
    var save = await client.CallToolAsync(
        "save_knowledge", SaveArguments(_repo.Root), cancellationToken: cts.Token);
    Assert.NotEqual(true, save.IsError);
    var knowledgeId = JsonSerializer.SerializeToElement(save.StructuredContent)
        .GetProperty("knowledgeId").GetString()!;
    var before = await ReadVersionCountAsync(cts.Token);

    File.WriteAllText(Path.Combine(_repo.Root, "src", "OrderService.cs"),
        "class OrderService\n{\n    void Dirty() { }\n}\n");
    var validate = await client.CallToolAsync(
        "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId, baseCommit),
        cancellationToken: cts.Token);
    Assert.NotEqual(true, validate.IsError);
    var json = JsonSerializer.SerializeToElement(validate.StructuredContent);
    Assert.Equal("valid", json.GetProperty("status").GetString());
    Assert.True(json.GetProperty("isWorkingTreeDirty").GetBoolean());
    Assert.Equal("inspect_dirty_evidence",
        json.GetProperty("recommendedAction").GetString());
    Assert.Equal(before, await ReadVersionCountAsync(cts.Token));
}
```

Add a Tool-specific repository-boundary test. It complements the existing `resolve_project` test and proves validation does not reach a knowledge write outside Git:

```csharp
[Fact]
public async Task Validate_knowledge_fails_outside_git_without_persisting()
{
    var outside = Path.Combine(Path.GetTempPath(), $"ck-validate-outside-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outside);
    try
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);
        var result = await client.CallToolAsync(
            "validate_knowledge", ValidateArguments(outside, "knowledge-1"),
            cancellationToken: cts.Token);
        Assert.True(result.IsError);
        var text = string.Join('\n', result.Content.Select(value => value.ToString()));
        Assert.Contains("git_repository_required: ", text);
        await using var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(cts.Token);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM projects;";
        Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync(cts.Token)));
    }
    finally
    {
        Directory.Delete(outside, recursive: true);
    }
}
```

- [x] **Step 2: Run the focused MCP tests and verify RED**

Run:

```bash
dotnet test tests/CodeKnowledge.Mcp.Tests --configuration Release \
  --filter 'FullyQualifiedName~Lists_all_five_tools|FullyQualifiedName~Validate_knowledge' \
  --maxcpucount:1 --disable-build-servers
```

Expected: the published server starts, but the Tool-list and calls fail because `validate_knowledge` is not registered.

- [x] **Step 3: Add the thin MCP Tool and DI registration**

Add `using CodeKnowledge.Core.Validation;` to `CodeKnowledgeTools.cs`, inject `ValidateKnowledgeUseCase validateKnowledge` after `saveKnowledge`, and add:

```csharp
[McpServerTool(Name = "validate_knowledge", UseStructuredContent = true),
    Description("Validates the current version of saved knowledge against HEAD or an optional " +
        "target commit. Returns per-evidence freshness and separately reports uncommitted " +
        "working-tree changes. Unknown results require direct code inspection.")]
public ValidateKnowledgeResult ValidateKnowledge(
    [Description("Absolute path of the current working directory.")] string workingDirectory,
    [Description("Knowledge id from search results.")] string knowledgeId,
    [Description("Optional local commit-ish to validate against. Omit to use current HEAD.")]
        string? targetCommit = null)
    => ToolGuard.Execute(() => validateKnowledge.Execute(
        new ValidateKnowledgeRequest(workingDirectory, knowledgeId, targetCommit)));
```

Add `using CodeKnowledge.Core.Validation;` to `Program.cs` and register the use case beside the other Core use cases:

```csharp
builder.Services.AddSingleton<ValidateKnowledgeUseCase>();
```

- [x] **Step 4: Run focused and complete MCP suites and verify GREEN**

Run:

```bash
dotnet test tests/CodeKnowledge.Mcp.Tests --configuration Release \
  --filter 'FullyQualifiedName~Lists_all_five_tools|FullyQualifiedName~Validate_knowledge' \
  --maxcpucount:1 --disable-build-servers
dotnet test tests/CodeKnowledge.Mcp.Tests --configuration Release \
  --maxcpucount:1 --disable-build-servers
```

Expected: the fifth Tool is listed; valid/partial/stale, explicit target, dirty, repository boundary, and no-version-write assertions pass through the published executable. Existing stdout-sensitive MCP calls continue to pass.

- [x] **Step 5: Commit the MCP surface and E2E coverage**

```bash
git add src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs \
  src/CodeKnowledge.Mcp/Program.cs \
  tests/CodeKnowledge.Mcp.Tests/McpEndToEndTests.cs
git commit -m "feat: expose knowledge freshness validation tool"
```

---

### Task 5: Document the Phase 2 Agent workflow and run the completion gate

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the exact Tool contract and behavior proven in Tasks 2–4.
- Produces: operator instructions and Agent rules that make validation mandatory before reusing matching knowledge.

- [x] **Step 1: Update the current-product overview without rewriting historical Phase 1 records**

Replace the current Phase 1-only opening and four-row Tool table with:

```markdown
# Code Knowledge

Code Knowledgeは、コード調査の結果をGitプロジェクト単位でSQLiteへ保存し、別の調査や別セッションから再利用するためのMCP stdioサーバーである。

Phase 2では、最新の確定ナレッジを保存・検索・取得・検証する5 Toolを提供する。すべてのToolは`workingDirectory`から対象Gitリポジトリを解決し、別プロジェクトのナレッジが混ざらないようにする。

| Tool | 用途 |
|---|---|
| `resolve_project` | 現在のGitリポジトリをCode Knowledge上のプロジェクトへ解決する |
| `search_knowledge` | FTS5と部分一致を組み合わせ、現在のプロジェクトに保存されたナレッジを検索する |
| `get_knowledge` | ナレッジの本文、事実、推論、根拠、関連を取得する |
| `save_knowledge` | ユーザーの明示指示または保存提案への同意後に、調査結果を保存する |
| `validate_knowledge` | 最新確定ナレッジをHEADまたは指定コミットに対して検証し、根拠ごとの鮮度と未コミット変更を返す |

Phase 3以降で追加予定の差分比較、プロジェクト移行などのToolは、Phase 2には含まれない。
```

Replace the live setup sentence with:

```markdown
プロジェクト単位の登録では、初回利用時に承認を求められる場合がある。承認後、新しいClaude Codeセッションから5 Toolの実機検証を行う。
```

Do not alter the dated Phase 1 measurements, client results, artifact hashes/sizes, or Phase 1 completion gate; those are historical observations rather than current Phase 2 verification claims.

- [x] **Step 2: Add exact usage and decision guidance**

Add this complete section immediately before `## Agent行動ルール`:

````markdown
## ナレッジの鮮度検証

`search_knowledge`で関連ナレッジを見つけた後、保存内容を回答へ再利用する前に`validate_knowledge`を呼ぶ。`targetCommit`を省略すると現在のHEADを検証し、指定するとローカルで解決できるそのcommit-ishを検証する。空白だけの`targetCommit`は`invalid_arguments`になる。

入力例:

```json
{
  "workingDirectory": "C:\\work\\order-system",
  "knowledgeId": "knowledge-001",
  "targetCommit": "HEAD"
}
```

出力例:

```json
{
  "status": "partially_stale",
  "baseCommit": "abc1234567890abc1234567890abc1234567890a",
  "targetCommit": "def4567890abcdef4567890abcdef4567890abcd",
  "isWorkingTreeDirty": true,
  "changedEvidence": ["SmtpEmailSender.SendAsync"],
  "unchangedEvidence": ["OrderService.CompleteAsync"],
  "missingEvidence": [],
  "unknownEvidence": [],
  "dirtyEvidence": ["SmtpEmailSender.SendAsync"],
  "evidenceDetails": [
    {
      "evidenceId": "evidence-001",
      "label": "SmtpEmailSender.SendAsync",
      "originalFilePath": "src/Mail/SmtpEmailSender.cs",
      "targetFilePath": "src/Mail/SmtpEmailSender.cs",
      "status": "changed",
      "reason": "symbol_hash_not_found",
      "isWorkingTreeDirty": true
    }
  ],
  "recommendedAction": "reinspect_changed_symbols",
  "warnings": []
}
```

全体の`status`は次の意味を持つ。

| status | 意味 |
|---|---|
| `valid` | 全Evidenceが再利用可能 |
| `partially_stale` | 再利用可能なEvidenceと変更・削除されたEvidenceが混在 |
| `stale` | 全Evidenceが変更または削除済み |
| `unknown` | Evidence 0件、判定不能Evidenceあり、またはdirty確認不能 |

`evidenceDetails[].status`は次の意味を持つ。

| status | 意味 |
|---|---|
| `unchanged` | 保存時と同じファイルまたはシンボル範囲ハッシュを確認できた |
| `changed` | ファイルは存在するが保存済みシンボル範囲ハッシュを再特定できない |
| `missing` | rename対応後の対象ファイルが存在しない |
| `unknown` | 必要なcommit、内容、diff、またはsymbol hashを確定できない |

`recommendedAction`は次の行動を示す。

| recommendedAction | 行動 |
|---|---|
| `reuse_knowledge` | 保存済みナレッジを主に利用する |
| `inspect_dirty_evidence` | dirtyなEvidenceファイルを直接確認する |
| `reinspect_changed_symbols` | changed、missing、dirtyなEvidenceを再確認する |
| `reinvestigate_knowledge` | ナレッジ全体を再調査する |
| `inspect_evidence` | unknown、warning、dirtyなEvidenceを直接確認する |

`isWorkingTreeDirty = true`なら、コミット間の鮮度が`valid`でも`dirtyEvidence`のファイルを直接確認する。`isWorkingTreeDirty = null`はdirty検出失敗を表し、全体の`status`は`unknown`になる。`unknownEvidence`、`warnings`、`evidenceDetails[].reason`を使って確認対象と理由を特定する。

検証は新しいナレッジバージョンや検証結果を保存せず、ナレッジを自動更新しない。Phase 2はGit renameと、対応付けたファイル内で正規化後のハッシュが一致するシンボル範囲移動を追跡する。Roslynによる構文解析、意味的なシンボル名変更の追跡、類似ファイルのリポジトリ全体探索は行わない。
````

- [x] **Step 3: Activate the Phase 2 Agent rules**

Replace the prose immediately below `## Agent行動ルール` with:

```markdown
次のブロックを`AGENTS.md`、`CLAUDE.md`など、利用するAgentの指示ファイルへコピーする。

Phase 2で有効なルールは1〜9、12、13である。10、11はPhase 3以降のToolに依存するため、全文を将来用に記載しているが、Phase 2では実行しない。
```

Then replace the complete fenced copyable rule block with:

````markdown
```markdown
## Code Knowledge MCP利用ルール

既存コードの仕様、構造、処理フロー、過去の調査内容について質問された場合、
コード全体を調査する前にCode Knowledge MCPを検索すること。

Phase 2で有効なルールは1〜9、12、13である。
「Phase 3以降」と記載されたルールは、必要なToolが提供されるまで実行しないこと。

手順:

1. 現在のプロジェクトを`resolve_project`で解決する。
2. `search_knowledge`で関連ナレッジを検索する。
   keywordsは網羅的に展開する: 質問中の名詞・複合語、英語表記、
   推測されるシンボル名を含め、3文字以上の複合語を優先する。
   2文字の重要語も含めてよい（サーバー側で部分一致検索される）。
3. 関連ナレッジが存在する場合は`validate_knowledge`を実行する。
4. `valid`の場合、保存済みナレッジを主に利用し、必要最小限のコード確認のみ行う。
   ただし`isWorkingTreeDirty`がtrueの場合、`dirtyEvidence`のファイルは直接確認する。
5. `partially_stale`の場合、`changedEvidence`、`missingEvidence`、`dirtyEvidence`の
   該当箇所と関連箇所だけを直接確認する。
6. `stale`の場合、ナレッジ全体を再調査し、`dirtyEvidence`も直接確認する。
7. `unknown`の場合、`unknownEvidence`、`warnings`、`evidenceDetails[].reason`から
   判定不能理由を確認し、該当コードと`dirtyEvidence`を直接確認する。
8. 保存済みナレッジなしで新規のコード調査を完了した場合、調査結果を
   ナレッジとして保存するかユーザーへ提案する。ユーザーの明示指示または
   提案への同意がある場合のみ`save_knowledge`を呼び出す。
   同意なしに自動保存しない。
9. 新しい調査結果を保存する前に、既存の類似ナレッジを検索で確認する。
   保存時は事実と推論を分離し、事実には必ず根拠を付ける。
10. [Phase 3以降] 差分調査結果は、ユーザーが保存を指示した場合のみ永続保存する。
    保存には`compare_knowledge`が返した`temporaryComparisonId`を指定する。
11. [Phase 3以降] `resolve_project`が`project_id_changed`警告を返した場合、ユーザーへ通知し、
    ユーザーの明示指示がある場合のみ`migrate_project`を実行する。
12. 確信度が`low`の推論を回答の根拠にする場合、該当コードを直接確認してから使用する。
13. 別プロジェクトのナレッジを検索または回答に使用しない。
```
````

- [x] **Step 4: Run documentation consistency checks**

Run:

```bash
rg -n '4 Tool|Phase 2以降で追加予定|\[Phase 2以降\]|validate_knowledge|partially_stale|isWorkingTreeDirty' README.md
git diff --check
```

Expected: “4 Tool” remains only inside dated Phase 1 historical records; no active Agent rule still marks Phase 2 behavior as future; `validate_knowledge`, dirty-null behavior, and the Phase 2 limitations are documented; whitespace checks pass.

- [x] **Step 5: Run the full automated and supported-publish completion gate**

Run from the repository root:

```bash
dotnet test CodeKnowledge.slnx --configuration Release \
  --maxcpucount:1 --disable-build-servers
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj \
  --configuration Release --runtime osx-arm64 --self-contained false \
  --output /tmp/ck-phase2-publish/osx-arm64 --disable-build-servers
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj \
  --configuration Release --runtime win-x64 --self-contained false \
  --output /tmp/ck-phase2-publish/win-x64 --disable-build-servers
test -x /tmp/ck-phase2-publish/osx-arm64/CodeKnowledge.Mcp
test -f /tmp/ck-phase2-publish/win-x64/CodeKnowledge.Mcp.exe
git diff --check
git status --short --branch
```

Expected: Core, Infrastructure, and MCP tests all pass; both supported RID publishes exit 0 and contain their executable entry point; no whitespace errors exist. Confirm that `.mcp.json` and `.DS_Store` entries remain untouched and outside every Phase 2 commit.

- [x] **Step 6: Commit current Phase 2 documentation**

```bash
git add README.md
git commit -m "docs: document phase 2 validation workflow"
```

- [x] **Step 7: Perform the final scope audit**

Run:

```bash
git diff HEAD~4..HEAD --stat
git log -5 --oneline
git status --short --branch
```

Verify against the approved design that there is no DB migration, no validation-result write, no version selector, no `compare_knowledge`, no Roslyn dependency, and no automatic `save_knowledge`. Report exact test and publish results; do not add new measurements to the historical README tables unless they were actually observed and the user asks to record them.
