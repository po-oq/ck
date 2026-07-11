using CodeKnowledge.Core.Errors;
using CodeKnowledge.Infrastructure.Git;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class GitCliRepositoryTests
{
    private readonly GitCliRepository _repository = new();

    [Fact]
    public void ResolveContext_returns_root_head_and_branch()
    {
        using var repo = new TestGitRepo();
        var commit = repo.CommitFile("src/a.txt", "hello");

        var context = _repository.ResolveContext(Path.Combine(repo.Root, "src"));

        Assert.Equal(
            Path.GetFullPath(repo.Root).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(context.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal(commit, context.HeadCommit);
        Assert.Equal("main", context.BranchName);
        Assert.Empty(context.Remotes);
    }

    [Fact]
    public void ResolveContext_reads_remotes_and_codeknowledge_config()
    {
        using var repo = new TestGitRepo();
        repo.CommitFile("a.txt", "x");
        repo.Run("remote", "add", "origin", "https://github.com/company/order-api.git");
        repo.Run("config", "codeknowledge.projectName", "Order API");

        var context = _repository.ResolveContext(repo.Root);

        Assert.Equal("https://github.com/company/order-api.git", context.Remotes["origin"]);
        Assert.Equal("Order API", context.ConfigProjectName);
        Assert.Null(context.ConfigProjectId);
    }

    [Fact]
    public void ResolveContext_returns_null_branch_for_detached_head()
    {
        using var repo = new TestGitRepo();
        var commit = repo.CommitFile("a.txt", "x");
        repo.CommitFile("b.txt", "y");
        repo.Run("checkout", "--detach", commit);

        var context = _repository.ResolveContext(repo.Root);

        Assert.Equal(commit, context.HeadCommit);
        Assert.Null(context.BranchName);
    }

    [Fact]
    public void ResolveContext_throws_outside_git_repository()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"ck-nogit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var exception = Assert.Throws<CodeKnowledgeException>(
                () => _repository.ResolveContext(outside));
            Assert.Equal(CodeKnowledgeException.GitRepositoryRequired, exception.Code);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ReadFileAtCommit_returns_content_at_that_commit()
    {
        using var repo = new TestGitRepo();
        var firstCommit = repo.CommitFile("src/a.txt", "version one");
        repo.CommitFile("src/a.txt", "version two");

        var content = _repository.ReadFileAtCommit(repo.Root, firstCommit, "src/a.txt");

        Assert.Equal("version one", content);
    }
}
