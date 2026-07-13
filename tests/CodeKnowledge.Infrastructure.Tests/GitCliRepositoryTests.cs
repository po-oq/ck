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
        var expectedRoot = Path.GetFullPath(
            repo.Run("rev-parse", "--show-toplevel").Trim());

        var context = _repository.ResolveContext(Path.Combine(repo.Root, "src"));

        Assert.Equal(expectedRoot, context.RepositoryRoot);
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

    [Fact]
    public void ResolveContext_preserves_remote_url_containing_spaces()
    {
        using var repo = new TestGitRepo();
        repo.CommitFile("a.txt", "x");
        repo.Run("remote", "add", "spaced", "C:/tmp/repo with space");

        var context = _repository.ResolveContext(repo.Root);

        Assert.Equal("C:/tmp/repo with space", context.Remotes["spaced"]);
    }

    [Fact]
    public void ReadFileAtCommit_preserves_utf8_bom_as_feff()
    {
        using var repo = new TestGitRepo();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("hello"u8.ToArray())
            .ToArray();
        File.WriteAllBytes(Path.Combine(repo.Root, "bom.txt"), bytes);
        repo.Run("add", "bom.txt");
        repo.Run("commit", "-m", "add bom file");
        var commit = repo.Run("rev-parse", "HEAD").Trim();

        var content = _repository.ReadFileAtCommit(repo.Root, commit, "bom.txt");

        Assert.Equal("\uFEFFhello", content);
    }

    [Fact]
    public void ResolveContext_resolves_worktree_root_and_branch()
    {
        using var repo = new TestGitRepo();
        repo.CommitFile("a.txt", "x");
        var worktreePath = Path.Combine(Path.GetTempPath(), $"ck-wt-{Guid.NewGuid():N}");
        repo.Run("worktree", "add", worktreePath, "-b", "wt-branch");
        try
        {
            var expectedRoot = Path.GetFullPath(
                repo.RunAt(
                    worktreePath,
                    "rev-parse",
                    "--show-toplevel").Trim());
            var context = _repository.ResolveContext(worktreePath);

            Assert.Equal(expectedRoot, context.RepositoryRoot);
            Assert.Equal("wt-branch", context.BranchName);
        }
        finally
        {
            repo.Run("worktree", "remove", "--force", worktreePath);
            if (Directory.Exists(worktreePath))
                Directory.Delete(worktreePath, recursive: true);
        }
    }

    [Fact]
    public void ReadFileAtCommit_throws_invalid_arguments_for_missing_path()
    {
        using var repo = new TestGitRepo();
        var commit = repo.CommitFile("a.txt", "x");

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => _repository.ReadFileAtCommit(repo.Root, commit, "does/not/exist.txt"));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }
}
