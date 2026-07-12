using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Tests;

public sealed class ProjectIdResolverTests
{
    private static GitContext Context(
        IReadOnlyDictionary<string, string>? remotes = null,
        string? configProjectId = null,
        string? configProjectName = null,
        string root = @"C:\work\my-tool")
        => new(root, "abc123", "main",
            remotes ?? new Dictionary<string, string>(),
            configProjectId, configProjectName);

    [Fact]
    public void Config_project_id_wins_over_remote() // AC-19
    {
        var identity = ProjectIdResolver.Resolve(Context(
            remotes: new Dictionary<string, string> { ["origin"] = "https://github.com/x/y.git" },
            configProjectId: "github.com/company/order-api"));
        Assert.Equal("github.com/company/order-api", identity.ProjectId);
        Assert.Equal("config", identity.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://user:pw@github.com/a/b")]
    public void Invalid_config_project_id_is_rejected(string configured)
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            ProjectIdResolver.Resolve(Context(configProjectId: configured)));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Origin_wins_over_upstream_and_others() // 要件5.3.3
    {
        var identity = ProjectIdResolver.Resolve(Context(remotes: new Dictionary<string, string>
        {
            ["zeta"] = "https://h.example/z/z",
            ["upstream"] = "https://h.example/u/u",
            ["origin"] = "https://h.example/o/o",
        }));
        Assert.Equal("h.example/o/o", identity.ProjectId);
        Assert.Equal("remote", identity.Source);
    }

    [Fact]
    public void Upstream_wins_when_no_origin()
    {
        var identity = ProjectIdResolver.Resolve(Context(remotes: new Dictionary<string, string>
        {
            ["zeta"] = "https://h.example/z/z",
            ["upstream"] = "https://h.example/u/u",
        }));
        Assert.Equal("h.example/u/u", identity.ProjectId);
    }

    [Fact]
    public void First_alphabetical_remote_when_no_origin_or_upstream()
    {
        var identity = ProjectIdResolver.Resolve(Context(remotes: new Dictionary<string, string>
        {
            ["zeta"] = "https://h.example/z/z",
            ["alpha"] = "https://h.example/a/a",
        }));
        Assert.Equal("h.example/a/a", identity.ProjectId);
    }

    [Fact]
    public void Local_fallback_is_deterministic_hash_of_normalized_root() // AC-17
    {
        var first = ProjectIdResolver.Resolve(Context(root: @"C:\Work\My-Tool"));
        var second = ProjectIdResolver.Resolve(Context(root: @"c:/work/my-tool"));
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.StartsWith("local:", first.ProjectId);
        Assert.Equal("local:".Length + 16, first.ProjectId.Length);
        Assert.Equal("local", first.Source);
    }

    [Fact]
    public void Display_name_prefers_config_then_remote_then_directory()
    {
        Assert.Equal("Order API", ProjectIdResolver.Resolve(Context(
            configProjectName: "Order API")).DisplayName);
        Assert.Equal("order-api", ProjectIdResolver.Resolve(Context(
            remotes: new Dictionary<string, string> { ["origin"] = "https://h.example/team/order-api.git" })).DisplayName);
        Assert.Equal("my-tool", ProjectIdResolver.Resolve(Context()).DisplayName);
    }
}
