using System.Text.Json;
using CodeKnowledge.Cli;
using CodeKnowledge.Infrastructure.Database;

namespace CodeKnowledge.Cli.Tests;

public sealed class CommandRunnerTests : IDisposable
{
    private readonly TestGitRepo _repo = new();
    private readonly string _dbDirectory =
        Path.Combine(Path.GetTempPath(), $"ck-cli-db-{Guid.NewGuid():N}");

    public CommandRunnerTests()
    {
        Directory.CreateDirectory(_dbDirectory);
        _repo.Run("remote", "add", "origin", "https://github.com/company/order-api.git");
        _repo.CommitFile("src/OrderService.cs",
            "class OrderService\n{\n    void Complete() { }\n}\n");
    }

    private CommandRunner NewRunner()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(_dbDirectory, "knowledge.db"));
        MigrationRunner.Apply(factory, Path.Combine(_dbDirectory, "knowledge.db"));
        return new CommandRunner(CommandRunner.BuildServices(factory));
    }

    [Fact]
    public void Resolve_returns_project_resolution_json()
    {
        var runner = NewRunner();
        var result = runner.Run("resolve", "{}", _repo.Root);
        var json = JsonSerializer.SerializeToElement(result, CliJson.Options);
        Assert.Equal("github.com/company/order-api",
            json.GetProperty("projectId").GetString());
    }

    [Fact]
    public void Unknown_subcommand_throws_invalid_arguments()
    {
        var runner = NewRunner();
        var exception = Assert.Throws<Core.Errors.CodeKnowledgeException>(
            () => runner.Run("bogus", "{}", _repo.Root));
        Assert.Equal(Core.Errors.CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_dbDirectory, recursive: true); } catch { /* temp cleanup */ }
    }
}
