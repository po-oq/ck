using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeGitRepository : IGitRepository
{
    public GitContext? Context { get; set; }
    public Dictionary<string, string> FilesAtCommit { get; } = new(StringComparer.Ordinal);
    public HashSet<string> UnavailableCommits { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string Commit, string Path), GitFileSnapshot> Snapshots { get; } = [];
    public Dictionary<(string Base, string Target), GitCommitDiff?> Diffs { get; } = [];
    public GitCommitDiff? DefaultCommitDiff { get; set; } = new([]);
    public Dictionary<(string Commit, string Path), int> SnapshotReadCounts { get; } = [];
    public IReadOnlySet<string>? WorkingTreeChangedPaths { get; set; } =
        new HashSet<string>(StringComparer.Ordinal);

    public GitContext ResolveContext(string workingDirectory)
        => Context ?? throw new CodeKnowledgeException(
            CodeKnowledgeException.GitRepositoryRequired,
            "The current directory is not inside a usable Git repository.");

    public string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath)
        => FilesAtCommit.TryGetValue(repoRelativePath, out var content)
            ? content
            : throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Cannot read '{repoRelativePath}' at commit {commitHash}.");

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
}
