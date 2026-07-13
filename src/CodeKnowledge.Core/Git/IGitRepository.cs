namespace CodeKnowledge.Core.Git;

public sealed record GitContext(
    string RepositoryRoot,
    string HeadCommit,
    string? BranchName,
    IReadOnlyDictionary<string, string> Remotes,
    string? ConfigProjectId,
    string? ConfigProjectName);

public interface IGitRepository
{
    GitContext ResolveContext(string workingDirectory);
    string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath);
    string? ResolveCommit(string repositoryRoot, string commitish);
    GitCommitDiff? CompareCommits(string repositoryRoot, string baseCommit, string targetCommit);
    GitFileSnapshot TryReadFileAtCommit(
        string repositoryRoot, string commitHash, string repoRelativePath);
    IReadOnlySet<string>? GetWorkingTreeChangedPaths(string repositoryRoot);
}
