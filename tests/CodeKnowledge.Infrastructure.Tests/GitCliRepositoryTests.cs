using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
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
}
