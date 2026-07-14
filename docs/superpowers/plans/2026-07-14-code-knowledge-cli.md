# CodeKnowledge CLI Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `CodeKnowledge.Cli` adapter so AI Agents (and humans) can invoke the existing 5 knowledge use cases through a JSON-in/JSON-out command-line tool when MCP is unavailable.

**Architecture:** The CLI is a second thin adapter over the unchanged Core/Application layer, alongside `CodeKnowledge.Mcp`. Each subcommand (`resolve`/`search`/`get`/`save`/`validate`) reads one JSON object from stdin or `--input <file>`, calls the matching `*UseCase`, and writes the result DTO as JSON to stdout. Logs go to stderr; process exit codes encode success/input-error/precondition-error. No business logic, SQLite, or Git access lives in the CLI project.

**Tech Stack:** C# / .NET 10, System.Text.Json (in-box, no new package), xunit.v3, SQLite via existing Infrastructure, hand-rolled argument dispatch (no CLI framework dependency).

## Global Constraints

- Target framework: `net10.0`; `LangVersion` latest (from `Directory.Build.props`).
- `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true — code must build warning-clean.
- Central package management: versions live in `Directory.Packages.props`. **No new PackageVersion is required** (System.Text.Json is in the framework).
- Distribution: framework-dependent single file, `--runtime win-x64 --self-contained false`. Windows 11 x64 only.
- **stdout carries JSON only.** All logs, migration notices, and error text go to stderr (mirror `CodeKnowledge.Mcp/Program.cs`).
- Exit codes: `0` success; `1` input/validation error; `2` precondition/environment error (git unresolved, DB unreachable, internal error).
- Reuse Core use cases and DTOs verbatim. Do not re-implement search/save/validate/git/SQLite in the CLI.
- Test framework: xunit.v3 (`[Fact]`, `TestContext.Current.CancellationToken`), matching existing test projects.

---

## File Structure

**New project `src/CodeKnowledge.Cli/`:**
- `CodeKnowledge.Cli.csproj` — Exe; references Core + Infrastructure; `AssemblyName` = `CodeKnowledge.Cli`.
- `Program.cs` — entry point: parse args → (help? print & exit 0) → run migrations → build service provider → read input JSON → dispatch → serialize result to stdout → map exceptions to exit code.
- `CliOptions.cs` — parses `args` into `{ Subcommand, InputPath, Cwd, ShowHelp }`; pure, no I/O.
- `CliJson.cs` — the single shared `JsonSerializerOptions` (camelCase, case-insensitive read).
- `CliInputReader.cs` — reads raw JSON text from `--input <file>` or stdin.
- `CommandRunner.cs` — resolves effective working directory, deserializes input, calls the matching use case, returns the result object. Throws domain exceptions unchanged.
- `ExitCodes.cs` — pure `Exception → int` mapping.
- `HelpText.cs` — per-subcommand `--help` text (usage + input JSON shape + minimal example).

**New test project `tests/CodeKnowledge.Cli.Tests/`:**
- `CodeKnowledge.Cli.Tests.csproj`
- `CliOptionsTests.cs`, `ExitCodesTests.cs`, `CliInputReaderTests.cs` — pure unit tests.
- `CommandRunnerTests.cs` — in-process command execution against a temp git repo + temp DB.
- `TestGitRepo.cs` — copy of the Mcp.Tests helper (git fixture).
- `PublishedCliFixture.cs`, `CliEndToEndTests.cs` — publish the real exe and drive it as a process.

**Modified:**
- `CodeKnowledge.slnx` — register both new projects.
- `README.md` — CLI build/publish, DB sharing, and Agent rules templates.

---

## Task 1: Scaffold CLI project and argument parsing

**Files:**
- Create: `src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj`
- Create: `src/CodeKnowledge.Cli/CliOptions.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj`
- Create: `tests/CodeKnowledge.Cli.Tests/CliOptionsTests.cs`
- Modify: `CodeKnowledge.slnx`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `record CliOptions(string? Subcommand, string? InputPath, string? Cwd, bool ShowHelp)`
  - `static CliOptions CliOptions.Parse(string[] args)`
  - `static readonly IReadOnlySet<string> CliOptions.KnownSubcommands` = { "resolve", "search", "get", "save", "validate" }

- [ ] **Step 1: Create the CLI project file**

Create `src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CodeKnowledge.Cli</RootNamespace>
    <AssemblyName>CodeKnowledge.Cli</AssemblyName>
    <PublishSingleFile Condition="'$(RuntimeIdentifier)' != ''">true</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeKnowledge.Core\CodeKnowledge.Core.csproj" />
    <ProjectReference Include="..\CodeKnowledge.Infrastructure\CodeKnowledge.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test project file**

Create `tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj` (mirror `tests/CodeKnowledge.Mcp.Tests/CodeKnowledge.Mcp.Tests.csproj`; open that file and copy its structure). It must:
- reference `Microsoft.NET.Test.Sdk` and `xunit.v3` (central-managed versions),
- `ProjectReference` `..\..\src\CodeKnowledge.Cli\CodeKnowledge.Cli.csproj` and `..\..\src\CodeKnowledge.Infrastructure\CodeKnowledge.Infrastructure.csproj`,
- copy the `xunit.runner.json` `<None ... CopyToOutputDirectory>` item if the Mcp test project has one.

Also copy `tests/CodeKnowledge.Mcp.Tests/xunit.runner.json` to `tests/CodeKnowledge.Cli.Tests/xunit.runner.json`.

- [ ] **Step 3: Register both projects in the solution**

Edit `CodeKnowledge.slnx`, adding one line under `/src/` and one under `/tests/`:

```xml
    <Project Path="src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj" />
```
```xml
    <Project Path="tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj" />
```

- [ ] **Step 4: Write the failing test for argument parsing**

Create `tests/CodeKnowledge.Cli.Tests/CliOptionsTests.cs`:

```csharp
using CodeKnowledge.Cli;

namespace CodeKnowledge.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_reads_subcommand_and_options()
    {
        var options = CliOptions.Parse(["save", "--input", "payload.json", "--cwd", "C:/repo"]);
        Assert.Equal("save", options.Subcommand);
        Assert.Equal("payload.json", options.InputPath);
        Assert.Equal("C:/repo", options.Cwd);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_detects_help_after_subcommand()
    {
        var options = CliOptions.Parse(["search", "--help"]);
        Assert.Equal("search", options.Subcommand);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_detects_top_level_help_with_no_subcommand()
    {
        var options = CliOptions.Parse(["--help"]);
        Assert.Null(options.Subcommand);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_treats_empty_args_as_help()
    {
        var options = CliOptions.Parse([]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void KnownSubcommands_are_the_five_use_cases()
    {
        Assert.Equal(
            new HashSet<string> { "resolve", "search", "get", "save", "validate" },
            CliOptions.KnownSubcommands.ToHashSet());
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj`
Expected: FAIL — `CliOptions` does not exist (compile error).

- [ ] **Step 6: Implement `CliOptions`**

Create `src/CodeKnowledge.Cli/CliOptions.cs`:

```csharp
namespace CodeKnowledge.Cli;

public sealed record CliOptions(string? Subcommand, string? InputPath, string? Cwd, bool ShowHelp)
{
    public static readonly IReadOnlySet<string> KnownSubcommands =
        new HashSet<string> { "resolve", "search", "get", "save", "validate" };

    public static CliOptions Parse(string[] args)
    {
        string? subcommand = null;
        string? inputPath = null;
        string? cwd = null;
        var showHelp = args.Length == 0;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--input" when index + 1 < args.Length:
                    inputPath = args[++index];
                    break;
                case "--cwd" when index + 1 < args.Length:
                    cwd = args[++index];
                    break;
                default:
                    // 最初の非オプショントークンをサブコマンドとして扱う
                    subcommand ??= token;
                    break;
            }
        }

        return new CliOptions(subcommand, inputPath, cwd, showHelp);
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj`
Expected: PASS (5 tests).

- [ ] **Step 8: Commit**

```bash
git add src/CodeKnowledge.Cli tests/CodeKnowledge.Cli.Tests CodeKnowledge.slnx
git commit -m "feat: scaffold CodeKnowledge.Cli project with argument parsing"
```

---

## Task 2: Exit-code mapping

**Files:**
- Create: `src/CodeKnowledge.Cli/ExitCodes.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/ExitCodesTests.cs`

**Interfaces:**
- Consumes: `CodeKnowledge.Core.Errors.CodeKnowledgeException` (has `.Code`; constants `InvalidArguments`, `FactRequiresEvidence`, `KnowledgeNotFound`, `GitRepositoryRequired`, `GitNotFound`, `DatabaseBusy`, `SchemaVersionUnsupported`, `InternalError`).
- Produces: `static int ExitCodes.ForException(Exception exception)`.

- [ ] **Step 1: Write the failing test**

Create `tests/CodeKnowledge.Cli.Tests/ExitCodesTests.cs`:

```csharp
using System.Text.Json;
using CodeKnowledge.Cli;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Cli.Tests;

public sealed class ExitCodesTests
{
    [Theory]
    [InlineData(CodeKnowledgeException.InvalidArguments, 1)]
    [InlineData(CodeKnowledgeException.FactRequiresEvidence, 1)]
    [InlineData(CodeKnowledgeException.KnowledgeNotFound, 1)]
    [InlineData(CodeKnowledgeException.GitRepositoryRequired, 2)]
    [InlineData(CodeKnowledgeException.GitNotFound, 2)]
    [InlineData(CodeKnowledgeException.DatabaseBusy, 2)]
    [InlineData(CodeKnowledgeException.SchemaVersionUnsupported, 2)]
    [InlineData(CodeKnowledgeException.InternalError, 2)]
    public void Domain_errors_map_to_documented_exit_codes(string code, int expected)
    {
        var exception = new CodeKnowledgeException(code, "message");
        Assert.Equal(expected, ExitCodes.ForException(exception));
    }

    [Fact]
    public void Json_parse_failure_is_an_input_error()
    {
        var exception = new JsonException("bad json");
        Assert.Equal(1, ExitCodes.ForException(exception));
    }

    [Fact]
    public void Unexpected_error_is_a_precondition_failure()
    {
        Assert.Equal(2, ExitCodes.ForException(new InvalidOperationException("boom")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter ExitCodesTests`
Expected: FAIL — `ExitCodes` not defined.

- [ ] **Step 3: Implement `ExitCodes`**

Create `src/CodeKnowledge.Cli/ExitCodes.cs`:

```csharp
using System.Text.Json;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int InputError = 1;
    public const int PreconditionError = 2;

    // 入力を直せば解消するエラーは1、環境・前提側の実行不能状態は2へ振り分ける。
    private static readonly HashSet<string> InputErrorCodes =
    [
        CodeKnowledgeException.InvalidArguments,
        CodeKnowledgeException.FactRequiresEvidence,
        CodeKnowledgeException.KnowledgeNotFound,
    ];

    public static int ForException(Exception exception) => exception switch
    {
        JsonException => InputError,
        CodeKnowledgeException domain when InputErrorCodes.Contains(domain.Code) => InputError,
        _ => PreconditionError,
    };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter ExitCodesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CodeKnowledge.Cli/ExitCodes.cs tests/CodeKnowledge.Cli.Tests/ExitCodesTests.cs
git commit -m "feat: map domain errors to CLI exit codes"
```

---

## Task 3: JSON options and input reading

**Files:**
- Create: `src/CodeKnowledge.Cli/CliJson.cs`
- Create: `src/CodeKnowledge.Cli/CliInputReader.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/CliInputReaderTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `static readonly JsonSerializerOptions CliJson.Options` (camelCase write, case-insensitive read).
  - `static string CliInputReader.Read(string? inputPath, TextReader standardInput)` — returns raw JSON text from the file when `inputPath` is set, otherwise reads `standardInput` to end.

- [ ] **Step 1: Write the failing test**

Create `tests/CodeKnowledge.Cli.Tests/CliInputReaderTests.cs`:

```csharp
using CodeKnowledge.Cli;

namespace CodeKnowledge.Cli.Tests;

public sealed class CliInputReaderTests
{
    [Fact]
    public void Reads_json_from_stdin_when_no_input_path()
    {
        using var stdin = new StringReader("""{"workingDirectory":"C:/repo"}""");
        var text = CliInputReader.Read(inputPath: null, stdin);
        Assert.Contains("C:/repo", text);
    }

    [Fact]
    public void Reads_json_from_input_file_and_ignores_stdin()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ck-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{"from":"file"}""");
        try
        {
            using var stdin = new StringReader("""{"from":"stdin"}""");
            var text = CliInputReader.Read(file, stdin);
            Assert.Contains("file", text);
            Assert.DoesNotContain("stdin", text);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Preserves_newlines_from_input_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ck-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{\"summary\":\"line1\\nline2\"}");
        try
        {
            using var stdin = new StringReader("");
            var text = CliInputReader.Read(file, stdin);
            Assert.Contains("line1\\nline2", text); // JSONエスケープされた改行がそのまま渡る
        }
        finally
        {
            File.Delete(file);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CliInputReaderTests`
Expected: FAIL — `CliInputReader` not defined.

- [ ] **Step 3: Implement `CliJson` and `CliInputReader`**

Create `src/CodeKnowledge.Cli/CliJson.cs`:

```csharp
using System.Text.Json;

namespace CodeKnowledge.Cli;

public static class CliJson
{
    // Core DTOのenumは各型の[JsonConverter]属性で小文字文字列化されるため、
    // ここではプロパティ命名(camelCase)だけMCPワイヤ形式へ合わせれば出力が一致する。
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
```

Create `src/CodeKnowledge.Cli/CliInputReader.cs`:

```csharp
namespace CodeKnowledge.Cli;

public static class CliInputReader
{
    public static string Read(string? inputPath, TextReader standardInput)
        => string.IsNullOrWhiteSpace(inputPath)
            ? standardInput.ReadToEnd()
            : File.ReadAllText(inputPath);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CliInputReaderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CodeKnowledge.Cli/CliJson.cs src/CodeKnowledge.Cli/CliInputReader.cs tests/CodeKnowledge.Cli.Tests/CliInputReaderTests.cs
git commit -m "feat: add CLI JSON options and input reader"
```

---

## Task 4: CommandRunner with the `resolve` command (DI + dispatch skeleton)

This task establishes the service-provider wiring and the deserialize→use-case→result pattern using the simplest command. Later commands slot into the same switch.

**Files:**
- Create: `src/CodeKnowledge.Cli/CommandRunner.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/TestGitRepo.cs` (copy from `tests/CodeKnowledge.Mcp.Tests/TestGitRepo.cs`)
- Create: `tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs`

**Interfaces:**
- Consumes:
  - `CliJson.Options`
  - `ResolveProjectUseCase.Execute(string workingDirectory) : ProjectResolution` (namespace `CodeKnowledge.Core.Projects`)
  - Infrastructure: `SqliteConnectionFactory(string dbPath)`, `MigrationRunner.Apply(SqliteConnectionFactory, string dbPath)`, `GitCliRepository`, `SqliteProjectStore`, `SqliteKnowledgeStore` (see `CodeKnowledge.Mcp/Program.cs` for exact namespaces).
- Produces:
  - `sealed class CommandRunner(IServiceProvider services)`
  - `object Run(string subcommand, string inputJson, string effectiveWorkingDirectory)` — dispatches on subcommand, returns the result DTO; throws `CodeKnowledgeException` for unknown subcommand (`InvalidArguments`).
  - `static IServiceProvider CommandRunner.BuildServices(SqliteConnectionFactory factory)` — registers use cases (Task 5 relies on this returning all five).

- [ ] **Step 1: Copy the git test helper**

Copy `tests/CodeKnowledge.Mcp.Tests/TestGitRepo.cs` to `tests/CodeKnowledge.Cli.Tests/TestGitRepo.cs`. Change only the namespace line to `namespace CodeKnowledge.Cli.Tests;`. (Open the source file and replicate it exactly; it wraps `git init`/commit against a temp directory.)

- [ ] **Step 2: Write the failing test**

Create `tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CommandRunnerTests`
Expected: FAIL — `CommandRunner` not defined.

- [ ] **Step 4: Implement `CommandRunner` with DI and the `resolve` case**

Create `src/CodeKnowledge.Cli/CommandRunner.cs`. Mirror the service registrations in `CodeKnowledge.Mcp/Program.cs:41-49` exactly:

```csharp
using System.Text.Json;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Core.Validation;
using CodeKnowledge.Infrastructure.Database;
using CodeKnowledge.Infrastructure.Git;
using CodeKnowledge.Infrastructure.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CodeKnowledge.Cli;

public sealed class CommandRunner(IServiceProvider services)
{
    public static IServiceProvider BuildServices(SqliteConnectionFactory factory)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(factory);
        collection.AddSingleton<IGitRepository, GitCliRepository>();
        collection.AddSingleton<IProjectStore, SqliteProjectStore>();
        collection.AddSingleton<IKnowledgeStore, SqliteKnowledgeStore>();
        collection.AddSingleton<ResolveProjectUseCase>();
        collection.AddSingleton<SearchKnowledgeUseCase>();
        collection.AddSingleton<GetKnowledgeUseCase>();
        collection.AddSingleton<SaveKnowledgeUseCase>();
        collection.AddSingleton<ValidateKnowledgeUseCase>();
        return collection.BuildServiceProvider();
    }

    public object Run(string subcommand, string inputJson, string effectiveWorkingDirectory)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        var input = document.RootElement;

        return subcommand switch
        {
            "resolve" => services.GetRequiredService<ResolveProjectUseCase>()
                .Execute(effectiveWorkingDirectory),
            _ => throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Unknown subcommand: {subcommand}"),
        };
    }

    private static string? OptionalString(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

Note: `effectiveWorkingDirectory` is passed in (Task 6 wires `--cwd`/`input.workingDirectory`/cwd default in `Program`). `OptionalString` is used by later commands (Task 5); keep it.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CommandRunnerTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/CodeKnowledge.Cli/CommandRunner.cs tests/CodeKnowledge.Cli.Tests/TestGitRepo.cs tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs
git commit -m "feat: add CommandRunner with DI wiring and resolve command"
```

---

## Task 5: Remaining commands (search, get, save, validate)

**Files:**
- Modify: `src/CodeKnowledge.Cli/CommandRunner.cs`
- Modify: `tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs`

**Interfaces:**
- Consumes (exact use-case signatures):
  - `SearchKnowledgeUseCase.Execute(string workingDirectory, IReadOnlyList<string> keywords, int? limit) : SearchKnowledgeResult`
  - `GetKnowledgeUseCase.Execute(string workingDirectory, string knowledgeId, string? versionId) : KnowledgeDetail`
  - `SaveKnowledgeUseCase.Execute(SaveKnowledgeRequest) : SaveKnowledgeResult`
  - `ValidateKnowledgeUseCase.Execute(ValidateKnowledgeRequest) : ValidateKnowledgeResult`
  - `SaveKnowledgeRequest(string WorkingDirectory, string CanonicalKey, string Title, string OriginalQuestion, string Summary, string Confidence, string? Tags, string? CreatedBy, string? CommitHash, IReadOnlyList<SaveEvidenceInput> Evidence, IReadOnlyList<SaveFactInput> Facts, IReadOnlyList<SaveInferenceInput> Inferences, IReadOnlyList<SaveRelationInput> Relations)` (namespace `CodeKnowledge.Core.Knowledge`)
  - `ValidateKnowledgeRequest(string WorkingDirectory, string KnowledgeId, string? TargetCommit)` (namespace `CodeKnowledge.Core.Validation`)
- Produces: the four remaining switch cases in `CommandRunner.Run`.

Input JSON per command (camelCase fields, matching the MCP tool parameters):
- `search`: `{ "keywords": ["..."], "limit": 10 }` (`workingDirectory` optional; the effective value is supplied by `Program`)
- `get`: `{ "knowledgeId": "...", "versionId": "..."? }`
- `save`: same fields as the `save_knowledge` MCP tool (`canonicalKey`, `title`, `originalQuestion`, `summary`, `confidence`, `evidence[]`, `facts[]`, `inferences[]`, `relations[]`, optional `tags`, `createdBy`, `commitHash`)
- `validate`: `{ "knowledgeId": "...", "targetCommit": "..."? }`

- [ ] **Step 1: Write the failing tests (roundtrip + multiline requirement)**

Append to `tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CommandRunnerTests`
Expected: FAIL — save/search/get/validate throw "Unknown subcommand".

- [ ] **Step 3: Add the four cases to `CommandRunner.Run`**

In `src/CodeKnowledge.Cli/CommandRunner.cs`, replace the `switch` in `Run` with:

```csharp
        return subcommand switch
        {
            "resolve" => services.GetRequiredService<ResolveProjectUseCase>()
                .Execute(effectiveWorkingDirectory),
            "search" => services.GetRequiredService<SearchKnowledgeUseCase>()
                .Execute(
                    effectiveWorkingDirectory,
                    StringArray(input, "keywords"),
                    OptionalInt(input, "limit")),
            "get" => services.GetRequiredService<GetKnowledgeUseCase>()
                .Execute(
                    effectiveWorkingDirectory,
                    RequiredString(input, "knowledgeId"),
                    OptionalString(input, "versionId")),
            "save" => services.GetRequiredService<SaveKnowledgeUseCase>()
                .Execute(DeserializeSaveRequest(input, effectiveWorkingDirectory)),
            "validate" => services.GetRequiredService<ValidateKnowledgeUseCase>()
                .Execute(new ValidateKnowledgeRequest(
                    effectiveWorkingDirectory,
                    RequiredString(input, "knowledgeId"),
                    OptionalString(input, "targetCommit"))),
            _ => throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Unknown subcommand: {subcommand}"),
        };
```

Add these helpers to the class (below `OptionalString`):

```csharp
    private static string RequiredString(JsonElement input, string name)
        => OptionalString(input, name)
           ?? throw new CodeKnowledgeException(
               CodeKnowledgeException.InvalidArguments, $"'{name}' is required.");

    private static int? OptionalInt(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static IReadOnlyList<string> StringArray(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments, $"'{name}' must be a JSON array.");
        return value.EnumerateArray().Select(item => item.GetString() ?? "").ToList();
    }

    private static SaveKnowledgeRequest DeserializeSaveRequest(
        JsonElement input, string effectiveWorkingDirectory)
    {
        // save入力はSaveKnowledgeRequestの形と同じ(camelCase)なので直接デシリアライズし、
        // workingDirectoryだけ実効値で上書きする。改行はJSON文字列の\nとして復元される。
        var partial = input.Deserialize<SaveKnowledgeRequest>(CliJson.Options)
            ?? throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments, "save input must be a JSON object.");
        return partial with { WorkingDirectory = effectiveWorkingDirectory };
    }
```

Note: `SaveKnowledgeRequest.WorkingDirectory` is non-null in the record, but the CLI supplies it from the effective working directory, so callers may omit it in JSON (it deserializes to null, then `with` overwrites it). All nested `SaveEvidenceInput`/`SaveFactInput`/`SaveInferenceInput`/`SaveRelationInput` records deserialize by camelCase automatically.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CommandRunnerTests`
Expected: PASS (4 tests total in the class).

- [ ] **Step 5: Run the full solution build to confirm warning-clean**

Run: `dotnet build CodeKnowledge.slnx --configuration Release`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 6: Commit**

```bash
git add src/CodeKnowledge.Cli/CommandRunner.cs tests/CodeKnowledge.Cli.Tests/CommandRunnerTests.cs
git commit -m "feat: implement search, get, save, validate CLI commands"
```

---

## Task 6: Program entry point, help text, and end-to-end published-exe tests

**Files:**
- Create: `src/CodeKnowledge.Cli/HelpText.cs`
- Create: `src/CodeKnowledge.Cli/Program.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/PublishedCliFixture.cs`
- Create: `tests/CodeKnowledge.Cli.Tests/CliEndToEndTests.cs`

**Interfaces:**
- Consumes: `CliOptions`, `CliInputReader`, `CliJson`, `CommandRunner`, `ExitCodes`, `DatabasePathResolver.Resolve()`, `SqliteConnectionFactory`, `MigrationRunner.Apply`.
- Produces: the `CodeKnowledge.Cli.exe` process contract (stdin/stdout JSON, exit codes, `--help`).

- [ ] **Step 1: Implement help text**

Create `src/CodeKnowledge.Cli/HelpText.cs`:

```csharp
namespace CodeKnowledge.Cli;

public static class HelpText
{
    public const string Top = """
        code-knowledge <command> [--input <file.json>] [--cwd <dir>]

        Reads one JSON object from stdin (or --input <file>) and writes a JSON
        result to stdout. Logs go to stderr. Exit: 0 ok, 1 input error, 2 precondition.

        Commands:
          resolve   Resolve the current Git repository into a project
          search    Search saved knowledge (input: {"keywords":[...],"limit":10})
          get       Get a knowledge entry (input: {"knowledgeId":"..."})
          save      Save an investigation result (input: full knowledge JSON)
          validate  Validate knowledge against HEAD or a commit (input: {"knowledgeId":"..."})

        Run `code-knowledge <command> --help` for the input JSON shape.
        """;

    public static string For(string subcommand) => subcommand switch
    {
        "resolve" => """resolve — input: {} (working dir from --cwd or current directory)""",
        "search" => """search — input: {"keywords":["mail","spec"],"limit":10}""",
        "get" => """get — input: {"knowledgeId":"<id>","versionId":"<optional>"}""",
        "save" => """
            save — input: {
              "canonicalKey":"domain.x","title":"...","originalQuestion":"...",
              "summary":"multi\nline ok","confidence":"high|medium|low",
              "evidence":[{"filePath":"src/x.cs","symbolName":"X.Y","symbolKind":"method","startLine":1,"endLine":4}],
              "facts":[{"text":"...","evidenceIndexes":[0]}],
              "inferences":[],"relations":[]
            }
            """,
        "validate" => """validate — input: {"knowledgeId":"<id>","targetCommit":"<optional>"}""",
        _ => Top,
    };
}
```

- [ ] **Step 2: Implement `Program.cs`**

Create `src/CodeKnowledge.Cli/Program.cs`. Migration-on-startup and stderr-notice mirror `CodeKnowledge.Mcp/Program.cs:15-34`:

```csharp
using System.Text.Json;
using CodeKnowledge.Cli;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Infrastructure.Database;

var options = CliOptions.Parse(args);

if (options.ShowHelp || options.Subcommand is null)
{
    Console.Out.WriteLine(
        options.Subcommand is null ? HelpText.Top : HelpText.For(options.Subcommand));
    return ExitCodes.Success;
}

if (!CliOptions.KnownSubcommands.Contains(options.Subcommand))
{
    await Console.Error.WriteLineAsync(
        $"{CodeKnowledgeException.InvalidArguments}: unknown command '{options.Subcommand}'.");
    return ExitCodes.InputError;
}

var databasePath = DatabasePathResolver.Resolve();
var factory = new SqliteConnectionFactory(databasePath);
await Console.Error.WriteLineAsync($"codeknowledge: applying migrations to {databasePath}");
try
{
    MigrationRunner.Apply(factory, databasePath);
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync(FormatError(exception));
    return ExitCodes.ForException(exception);
}

try
{
    var inputJson = CliInputReader.Read(options.InputPath, Console.In);
    var effectiveWorkingDirectory = ResolveWorkingDirectory(options.Cwd, inputJson);
    var runner = new CommandRunner(CommandRunner.BuildServices(factory));
    var result = runner.Run(options.Subcommand, inputJson, effectiveWorkingDirectory);
    Console.Out.WriteLine(JsonSerializer.Serialize(result, CliJson.Options));
    return ExitCodes.Success;
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync(FormatError(exception));
    return ExitCodes.ForException(exception);
}

static string FormatError(Exception exception) => exception switch
{
    CodeKnowledgeException domain => $"{domain.Code}: {domain.Message}",
    JsonException => $"{CodeKnowledgeException.InvalidArguments}: {exception.Message}",
    _ => $"{CodeKnowledgeException.InternalError}: {exception.GetType().Name}: {exception.Message}",
};

// 実効ワーキングディレクトリ: --cwd > 入力JSONのworkingDirectory > プロセスのカレント
static string ResolveWorkingDirectory(string? cwd, string inputJson)
{
    if (!string.IsNullOrWhiteSpace(cwd)) return cwd;
    try
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        if (document.RootElement.TryGetProperty("workingDirectory", out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!;
    }
    catch (JsonException)
    {
        // 不正JSONはCommandRunner側で改めてパースされ、入力エラーとして報告される
    }
    return Directory.GetCurrentDirectory();
}
```

- [ ] **Step 3: Write the failing E2E fixture and tests**

Create `tests/CodeKnowledge.Cli.Tests/PublishedCliFixture.cs` by adapting `tests/CodeKnowledge.Mcp.Tests/PublishedServerFixture.cs` and `PublishedServerTarget.cs`: publish `src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj` for the current RID (`win-x64` → `CodeKnowledge.Cli.exe`) and expose `ExePath`. Keep the same `PlatformNotSupportedException` behavior for unsupported RIDs — do not skip.

Create `tests/CodeKnowledge.Cli.Tests/CliEndToEndTests.cs`:

```csharp
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
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);
            Assert.Equal(2, process.ExitCode);
            Assert.Contains("git_repository_required: ", stderr);
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
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test tests/CodeKnowledge.Cli.Tests/CodeKnowledge.Cli.Tests.csproj --filter CliEndToEndTests`
Expected: FAIL — `Program`/`HelpText`/`PublishedCliFixture` missing (compile), then publish wiring.

- [ ] **Step 5: Run the full suite to verify everything passes**

Run: `dotnet test CodeKnowledge.slnx --configuration Release`
Expected: PASS — existing MCP/Core/Infrastructure tests plus the new CLI unit and E2E tests all green.

- [ ] **Step 6: Commit**

```bash
git add src/CodeKnowledge.Cli/Program.cs src/CodeKnowledge.Cli/HelpText.cs tests/CodeKnowledge.Cli.Tests/PublishedCliFixture.cs tests/CodeKnowledge.Cli.Tests/CliEndToEndTests.cs
git commit -m "feat: add CLI entry point, help, and published-exe E2E tests"
```

---

## Task 7: Distribution, DB sharing, and Agent rules templates (README)

**Files:**
- Modify: `README.md`
- Create: `docs/agent-rules/code-knowledge-cli.md` (the shared rules template body)

**Interfaces:**
- Consumes: nothing (documentation).
- Produces: user-facing publish command, DB-sharing guidance, and copy-paste rules templates for the three rule-file locations.

- [ ] **Step 1: Add the CLI publish section to README**

In `README.md`, after the existing MCP "ビルドと発行" section, add a CLI subsection documenting:

````markdown
### CLIの発行（Windows 11 x64）

```bash
dotnet publish src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj \
  --configuration Release --runtime win-x64 --self-contained false \
  --output artifacts/cli/win-x64
# → artifacts\cli\win-x64\CodeKnowledge.Cli.exe
```

CLIとMCPで同じナレッジを共有するには、両方に同じ `CODEKNOWLEDGE_DB_PATH` を設定する。
未設定の場合、DBは各実行ファイル隣の `knowledge.db` になり、MCPとCLIで別々になる。
````

- [ ] **Step 2: Create the shared Agent rules template**

Create `docs/agent-rules/code-knowledge-cli.md`:

```markdown
## コード知識ツール（CLI）

既存機能について調べるときは、まず保存済みナレッジを検索してから回答する。

    echo '{"keywords":["認証","トークン"]}' | "C:\Tools\CodeKnowledge\CodeKnowledge.Cli.exe" search

調査が済んだら、ユーザーの明示指示または保存提案への同意後にのみ保存する。
本文が長い・改行を含む場合は、一時ファイルにJSONを書いて --input で渡す（シェルのクォート事故を避ける）。

    "C:\Tools\CodeKnowledge\CodeKnowledge.Cli.exe" save --input C:\Temp\ck-save.json

入力JSONの形が不明なときは `<exe> <command> --help` で確認する。
stdoutはJSON、ログはstderr、終了コードは 0=成功 / 1=入力エラー / 2=前提エラー。
```

- [ ] **Step 3: Document where to place the template per Agent**

In `README.md`, add a table telling users to copy `docs/agent-rules/code-knowledge-cli.md` into:

````markdown
### CLIをAgentに使わせる設定

`docs/agent-rules/code-knowledge-cli.md` の内容を、利用先リポジトリの次のファイルへ転記する
（実体は3種。Copilotの2環境は同じファイルを読む）。

| 環境 | ルールファイル |
|---|---|
| Cursor | `.cursor/rules/code-knowledge.mdc` |
| Claude Code | `CLAUDE.md` |
| GitHub Copilot（VS Code / Visual Studio） | `.github/copilot-instructions.md` |

`CodeKnowledge.Cli.exe` の絶対パスは各環境の配置に合わせて置き換える。
````

- [ ] **Step 4: Verify the docs reference real paths**

Run: `dotnet publish src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/cli/win-x64`
Expected: publish succeeds and `artifacts/cli/win-x64/CodeKnowledge.Cli.exe` exists (confirms the documented command).

- [ ] **Step 5: Commit**

```bash
git add README.md docs/agent-rules/code-knowledge-cli.md
git commit -m "docs: document CLI publish, DB sharing, and Agent rules templates"
```

---

## Self-Review Notes

- **Spec coverage:** §1 purpose → Tasks 4–5 (reuse of Core use cases). §2 scope (5 commands, win-x64, no Core change) → Tasks 1,4,5,7. §3 thin adapter → Task 4 (`BuildServices` mirrors MCP; no logic added). §4 command surface + `--cwd` + `--help` → Tasks 1,5,6. §5 stdin/`--input`/precedence, stdout-JSON/stderr-logs, exit codes, **multiline requirement** → Tasks 2,3,5(multiline unit),6(multiline E2E). §6 rules files (3 locations) → Task 7. §7 publish + DB sharing → Task 7. §8 tests (adapter parse, both input paths, DTO-shape parity, exit codes, git-outside failure, multiline) → Tasks 1–6.
- **Parity:** §8's "same output JSON as MCP" is structurally guaranteed because both adapters serialize the *same Core DTOs*; the guard is that `CliJson.Options` reproduces the MCP wire format (camelCase + enum attributes), verified by the roundtrip tests asserting lowercase `confidence` and preserved field names.
- **Placeholder scan:** no TBD/TODO; every code step contains complete code.
- **Type consistency:** `CommandRunner.Run`, `BuildServices`, `CliOptions.Parse/KnownSubcommands`, `CliInputReader.Read`, `CliJson.Options`, `ExitCodes.ForException` names are used identically across tasks. Use-case signatures and `SaveKnowledgeRequest`/`ValidateKnowledgeRequest` shapes are copied verbatim from the source.
