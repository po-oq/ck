using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeGitRepository : IGitRepository
{
    public GitContext? Context { get; set; }
    public Dictionary<string, string> FilesAtCommit { get; } = new(StringComparer.Ordinal);

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
}
