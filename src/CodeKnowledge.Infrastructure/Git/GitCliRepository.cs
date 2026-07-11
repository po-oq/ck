using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Infrastructure.Git;

public sealed class GitCliRepository : IGitRepository
{
    public GitContext ResolveContext(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"workingDirectory does not exist: {workingDirectory}");

        var rootResult = GitCommandRunner.Run(workingDirectory, "rev-parse", "--show-toplevel");
        if (rootResult.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "The current directory is not inside a usable Git repository.");
        var repositoryRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());

        var headResult = GitCommandRunner.Run(repositoryRoot, "rev-parse", "HEAD");
        if (headResult.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "Cannot resolve the current commit (repository may have no commits).");
        var headCommit = headResult.StandardOutput.Trim();

        var branchResult = GitCommandRunner.Run(
            repositoryRoot, "symbolic-ref", "--short", "-q", "HEAD");
        var branchName = branchResult.ExitCode == 0
            ? branchResult.StandardOutput.Trim()
            : null;

        var remotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var remoteResult = GitCommandRunner.Run(repositoryRoot, "remote", "-v");
        if (remoteResult.ExitCode == 0)
        {
            foreach (var line in remoteResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && !remotes.ContainsKey(parts[0]))
                    remotes[parts[0]] = parts[1];
            }
        }

        return new GitContext(
            repositoryRoot,
            headCommit,
            branchName,
            remotes,
            ReadConfig(repositoryRoot, "codeknowledge.projectId"),
            ReadConfig(repositoryRoot, "codeknowledge.projectName"));
    }

    public string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath)
    {
        var result = GitCommandRunner.Run(repositoryRoot, "show", $"{commitHash}:{repoRelativePath}");
        if (result.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Cannot read '{repoRelativePath}' at commit {commitHash}.");
        return result.StandardOutput;
    }

    private static string? ReadConfig(string repositoryRoot, string key)
    {
        var result = GitCommandRunner.Run(repositoryRoot, "config", "--get", key);
        return result.ExitCode == 0 && result.StandardOutput.Trim().Length > 0
            ? result.StandardOutput.Trim()
            : null;
    }
}
