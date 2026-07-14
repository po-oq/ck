using System.Diagnostics;
using System.Text.Json;

namespace CodeKnowledge.Cli.Tests;

public sealed class CliEndToEndTests : IClassFixture<PublishedCliFixture>, IDisposable
{
    private readonly PublishedCliFixture _cli;
    private readonly TestGitRepo _repo = new();
    private readonly string _dbDirectory =
        Path.Combine(Path.GetTempPath(), $"ck-cli-e2e-{Guid.NewGuid():N}");

    public CliEndToEndTests(PublishedCliFixture cli)
    {
        _cli = cli;
        Directory.CreateDirectory(_dbDirectory);
        _repo.Run("remote", "add", "origin", "https://github.com/company/order-api.git");
        _repo.CommitFile("src/OrderService.cs",
            "class OrderService\n{\n    void Complete() { }\n}\n");
    }

    private (int ExitCode, string Stdout, string Stderr) Invoke(
        string subcommand, string stdinJson, string? inputFile = null)
    {
        var startInfo = new ProcessStartInfo(_cli.ExePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(subcommand);
        startInfo.ArgumentList.Add("--cwd");
        startInfo.ArgumentList.Add(_repo.Root);
        if (inputFile is not null)
        {
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(inputFile);
        }
        startInfo.Environment["CODEKNOWLEDGE_DB_PATH"] =
            Path.Combine(_dbDirectory, "knowledge.db");

        using var process = Process.Start(startInfo)!;
        if (inputFile is null) process.StandardInput.Write(stdinJson);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout, stderr);
    }

    private static string SaveJson(string summary) => JsonSerializer.Serialize(new
    {
        canonicalKey = "domain.mail.order-completed",
        title = "注文完了メール仕様",
        originalQuestion = "q",
        summary,
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

    [Fact]
    public void Multiline_save_via_input_file_roundtrips_through_get()
    {
        var multiline = "line1\nline2\n\tline3";
        var file = Path.Combine(_dbDirectory, "save.json");
        File.WriteAllText(file, SaveJson(multiline));

        var save = Invoke("save", "", file);
        Assert.Equal(0, save.ExitCode);
        var knowledgeId = JsonDocument.Parse(save.Stdout)
            .RootElement.GetProperty("knowledgeId").GetString()!;

        var get = Invoke("get", JsonSerializer.Serialize(new { knowledgeId }));
        Assert.Equal(0, get.ExitCode);
        Assert.Equal(multiline, JsonDocument.Parse(get.Stdout)
            .RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public void Save_via_stdin_returns_json_on_stdout_only()
    {
        var result = Invoke("save", SaveJson("single line"));
        Assert.Equal(0, result.ExitCode);
        // stdoutはJSONのみ（先頭が '{'）、ログはstderrへ
        Assert.StartsWith("{", result.Stdout.TrimStart());
        Assert.Contains("applying migrations", result.Stderr);
    }

    [Fact]
    public void Outside_git_repository_exits_with_precondition_code()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"ck-cli-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var startInfo = new ProcessStartInfo(_cli.ExePath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("resolve");
            startInfo.ArgumentList.Add("--cwd");
            startInfo.ArgumentList.Add(outside);
            startInfo.Environment["CODEKNOWLEDGE_DB_PATH"] =
                Path.Combine(_dbDirectory, "knowledge.db");
            using var process = Process.Start(startInfo)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);
            Assert.Equal(2, process.ExitCode);
            Assert.Contains("git_repository_required: ", stderr);
            // stdoutはJSON専用の契約: 失敗パスでも何も書き込まれない
            Assert.True(string.IsNullOrWhiteSpace(stdout));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Help_prints_usage_and_exits_zero()
    {
        var startInfo = new ProcessStartInfo(_cli.ExePath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--help");
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("code-knowledge <command>", stdout);
    }

    public void Dispose()
    {
        _repo.Dispose();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { Directory.Delete(_dbDirectory, recursive: true); return; }
            catch when (attempt < 3) { Thread.Sleep(500); }
            catch { /* temp領域なので諦める */ }
        }
    }
}
