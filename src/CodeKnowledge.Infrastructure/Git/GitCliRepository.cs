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
            // 各行は "<name>\t<url> (fetch|push)" 形式。URLに空白が含まれ得るため
            // 最初のタブでのみ分割し、末尾の " (fetch)" / " (push)" を取り除く。
            // 同名の最初の行（fetch URL）を採用する。
            foreach (var line in remoteResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedLine = line.TrimEnd('\r');
                var tabIndex = trimmedLine.IndexOf('\t');
                if (tabIndex <= 0)
                    continue;
                var name = trimmedLine[..tabIndex];
                var rest = trimmedLine[(tabIndex + 1)..];
                var suffixIndex = rest.LastIndexOf(" (", StringComparison.Ordinal);
                var url = suffixIndex >= 0 ? rest[..suffixIndex] : rest;
                if (url.Length > 0 && !remotes.ContainsKey(name))
                    remotes[name] = url;
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

    /// <summary>
    /// Reads a file's content at the given commit. Raw bytes are decoded as UTF-8,
    /// so a leading UTF-8 BOM is preserved as U+FEFF (not stripped). Non-UTF-8
    /// encoded files are decoded with replacement characters — deterministic, so
    /// content saved and validated through this same path stays consistent.
    /// Known Phase 1 limitation.
    /// </summary>
    public string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath)
    {
        var result = GitCommandRunner.RunBytes(repositoryRoot, "show", $"{commitHash}:{repoRelativePath}");
        if (result.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Cannot read '{repoRelativePath}' at commit {commitHash}.");
        return System.Text.Encoding.UTF8.GetString(result.StandardOutput);
    }

    private static string? ReadConfig(string repositoryRoot, string key)
    {
        var result = GitCommandRunner.Run(repositoryRoot, "config", "--get", key);
        return result.ExitCode == 0 && result.StandardOutput.Trim().Length > 0
            ? result.StandardOutput.Trim()
            : null;
    }
}
