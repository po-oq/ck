using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Tests.Fakes;

namespace CodeKnowledge.Core.Tests;

public sealed class ResolveProjectUseCaseTests
{
    private readonly FakeGitRepository _git = new();
    private readonly FakeProjectStore _store = new();

    private ResolveProjectUseCase UseCase => new(_git, _store);

    private static GitContext RemoteContext(string root = @"C:\work\order-api")
        => new(root, "abc123", "main",
            new Dictionary<string, string> { ["origin"] = "https://github.com/company/order-api.git" },
            null, null);

    [Fact]
    public void Execute_registers_new_project_and_returns_resolution()
    {
        _git.Context = RemoteContext();

        var resolution = UseCase.Execute(@"C:\work\order-api\src");

        Assert.Equal("github.com/company/order-api", resolution.ProjectId);
        Assert.Equal("remote", resolution.ProjectIdSource);
        Assert.Equal("abc123", resolution.CurrentCommit);
        Assert.Empty(resolution.Warnings);
        Assert.True(_store.Projects.ContainsKey("github.com/company/order-api"));
    }

    [Fact]
    public void Execute_warns_when_project_id_changed_for_same_root() // AC-18の警告側
    {
        _store.Upsert(new Project(
            "local:3fa2b8c1d4e5f607", "order-api", @"C:\work\order-api",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _store.KnowledgeCounts["local:3fa2b8c1d4e5f607"] = 12;
        _git.Context = RemoteContext();

        var resolution = UseCase.Execute(@"C:\work\order-api");

        var warning = Assert.Single(resolution.Warnings);
        Assert.Equal("project_id_changed", warning.Code);
        Assert.Equal("local:3fa2b8c1d4e5f607", warning.PreviousProjectId);
        Assert.Equal(12, warning.KnowledgeCount);
        // 自動移行しない: 旧プロジェクトはそのまま残る
        Assert.True(_store.Projects.ContainsKey("local:3fa2b8c1d4e5f607"));
    }

    [Fact]
    public void Execute_updates_repository_root_for_same_project_id() // 要件5.8.3
    {
        _store.Upsert(new Project(
            "github.com/company/order-api", "order-api", @"C:\old\clone",
            "github.com/company/order-api", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _git.Context = RemoteContext(root: @"C:\new\clone");

        UseCase.Execute(@"C:\new\clone");

        Assert.Equal(@"C:\new\clone",
            _store.Projects["github.com/company/order-api"].RepositoryRoot);
    }
}
