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

    [Fact]
    public void Save_search_get_roundtrip_preserves_multiline_summary()
    {
        var runner = NewRunner();
        var multilineSummary = "OrderService.Complete sends mail.\nSecond line.\n\tIndented third.";
        var saveInput = JsonSerializer.Serialize(new
        {
            canonicalKey = "domain.mail.order-completed",
            title = "注文完了メール仕様",
            originalQuestion = "注文完了メールの処理は？",
            summary = multilineSummary,
            confidence = "high",
            evidence = new[]
            {
                new { filePath = "src/OrderService.cs", symbolName = "OrderService.Complete",
                      symbolKind = "method", startLine = 1, endLine = 4 },
            },
            facts = new[]
            {
                new { text = "Completeがメール送信を行う", evidenceIndexes = new[] { 0 } },
            },
            inferences = Array.Empty<object>(),
            relations = Array.Empty<object>(),
        });

        var save = runner.Run("save", saveInput, _repo.Root);
        var saveJson = JsonSerializer.SerializeToElement(save, CliJson.Options);
        var knowledgeId = saveJson.GetProperty("knowledgeId").GetString()!;

        var searchInput = JsonSerializer.Serialize(new { keywords = new[] { "メール", "仕様" } });
        var search = runner.Run("search", searchInput, _repo.Root);
        var searchJson = JsonSerializer.SerializeToElement(search, CliJson.Options);
        Assert.Equal("注文完了メール仕様",
            searchJson.GetProperty("results")[0].GetProperty("title").GetString());

        var getInput = JsonSerializer.Serialize(new { knowledgeId });
        var get = runner.Run("get", getInput, _repo.Root);
        var getJson = JsonSerializer.SerializeToElement(get, CliJson.Options);
        // 改行・タブがDB往復後もそのまま保持されている（設計書§5 必須要件）
        Assert.Equal(multilineSummary, getJson.GetProperty("summary").GetString());
        Assert.Equal("high", getJson.GetProperty("confidence").GetString()); // enumは小文字文字列
    }

    [Fact]
    public void Validate_returns_status_for_saved_knowledge()
    {
        var runner = NewRunner();
        var saveInput = JsonSerializer.Serialize(new
        {
            canonicalKey = "domain.mail.order-completed",
            title = "注文完了メール仕様",
            originalQuestion = "q",
            summary = "s",
            confidence = "high",
            evidence = new[]
            {
                new { filePath = "src/OrderService.cs", symbolName = "OrderService.Complete",
                      symbolKind = "method", startLine = 1, endLine = 4 },
            },
            facts = new[] { new { text = "f", evidenceIndexes = new[] { 0 } } },
            inferences = Array.Empty<object>(),
            relations = Array.Empty<object>(),
        });
        var knowledgeId = JsonSerializer.SerializeToElement(
            runner.Run("save", saveInput, _repo.Root), CliJson.Options)
            .GetProperty("knowledgeId").GetString()!;

        var validate = runner.Run("validate",
            JsonSerializer.Serialize(new { knowledgeId }), _repo.Root);
        var json = JsonSerializer.SerializeToElement(validate, CliJson.Options);
        Assert.Equal("valid", json.GetProperty("status").GetString());
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_dbDirectory, recursive: true); } catch { /* temp cleanup */ }
    }
}
