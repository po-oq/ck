# Code Knowledge Phase 0 実装計画

> **エージェント作業者向け:** 必須サブスキルとして、タスク単位の実装には`superpowers:subagent-driven-development`（推奨）または`superpowers:executing-plans`を使用すること。進捗管理には各手順のチェックボックス（`- [ ]`）を使用する。

**目標:** Code Knowledgeの本実装前に、SQLite FTS5 trigram、ハイブリッド検索、複数プロセス同時アクセス、MCP stdio、Windows向け単一ファイル発行の技術的前提を検証する、再実行可能なPhase 0ハーネスを構築する。

**アーキテクチャ:** `spikes/phase0/CodeKnowledge.Phase0`を単一のprobe実行ファイルとし、`self-check`、`concurrency-worker`、`mcp`の3モードを提供する。`CodeKnowledge.Phase0.Tests`は公開されたprobe操作、実プロセス、発行済みEXEを検証し、手動の3クライアント検証だけを`README.md`の承認ゲートとして残す。

**技術スタック:** Windows 11、.NET SDK 10.0.201、`net10.0`、C#、Microsoft.Data.Sqlite 10.0.9、SQLitePCLRaw.bundle_e_sqlite3 3.0.3、ModelContextProtocol 1.4.1、Microsoft.Extensions.Hosting 10.0.9、xunit.v3 3.2.2、Microsoft.NET.Test.Sdk 18.7.0、SQLite FTS5

## 全体制約

- 対象OSはWindows 11、Target Frameworkは`net10.0`とする。
- NuGetパッケージは安定版のみを使用し、上記バージョンへ固定する。preview版へ変更しない。SQLitePCLRaw bundleは`SQLitePCLRaw.bundle_e_sqlite3` 3.0.3へ固定する。
- SQLiteは`Microsoft.Data.Sqlite`とprobeから直接参照する`SQLitePCLRaw.bundle_e_sqlite3`を使用し、外部SQLite拡張DLLを追加しない。
- MCP transportはstdioのみとし、`mcp`モードのstdoutにはMCPプロトコル以外を一切出力しない。
- テストデータは一時ディレクトリへ隔離し、`%LOCALAPPDATA%\CodeKnowledge\knowledge.db`へ接続しない。
- 同時実行テストと発行スモークテストはモックではなく実プロセスを使用する。
- Phase 0のソースコードをPhase 1の`src/`へコピーまたは直接昇格させない。
- 既存の`docs/code-knowledge-tool-requirements-v2.md`は未追跡のユーザーファイルとして維持し、この計画のコミットへ含めない。

---

## ファイル構成

| パス | 責務 |
|---|---|
| `global.json` | 使用する.NET SDK系統を10.0.201へ固定する |
| `Directory.Build.props` | nullable、暗黙using、警告エラー化を全プロジェクトへ適用する |
| `Directory.Packages.props` | Phase 0で使用するNuGetバージョンを一元管理する |
| `CodeKnowledge.Phase0.slnx` | probeとテストプロジェクトだけを含むPhase 0 solution |
| `spikes/phase0/CodeKnowledge.Phase0/CommandLine.cs` | 実行モードと引数を解析する |
| `spikes/phase0/CodeKnowledge.Phase0/ProbeReport.cs` | JSON診断結果と終了コードの契約を定義する |
| `spikes/phase0/CodeKnowledge.Phase0/SearchProbe.cs` | Unicode文字数によるFTS/LIKE振り分けと安全な検索を行う |
| `spikes/phase0/CodeKnowledge.Phase0/SqliteProbe.cs` | SQLite、FTS5、trigram、PRAGMA、検索前提を自己診断する |
| `spikes/phase0/CodeKnowledge.Phase0/ConcurrencyWorker.cs` | 1 worker分の開始バリア待機、読み書き、JSON結果出力を行う |
| `spikes/phase0/CodeKnowledge.Phase0/McpProbeTool.cs` | `phase0_probe` Toolの固定診断レスポンスを返す |
| `spikes/phase0/CodeKnowledge.Phase0/McpServerRunner.cs` | stderrログとstdio MCP serverを構成する |
| `spikes/phase0/CodeKnowledge.Phase0/Program.cs` | モードを各コンポーネントへルーティングする |
| `spikes/phase0/CodeKnowledge.Phase0.Tests/TestWorkspace.cs` | テストごとの一時ディレクトリを管理する |
| `spikes/phase0/CodeKnowledge.Phase0.Tests/SearchTestDatabase.cs` | 検索probe用の固定スキーマと固定データを構築する |
| `spikes/phase0/CodeKnowledge.Phase0.Tests/ProcessRunner.cs` | 子プロセスを起動し、stdout、stderr、終了コードを収集する |
| `spikes/phase0/CodeKnowledge.Phase0.Tests/*Tests.cs` | 各probeのTDDおよび統合検証を行う |
| `spikes/phase0/README.md` | 再実行手順、生成物、3クライアントの手動結果、Phase完了判定を記録する |

### Task 1: solution基盤とコマンド解析

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.gitignore`
- Create: `CodeKnowledge.Phase0.slnx`
- Create: `spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj`
- Create: `spikes/phase0/CodeKnowledge.Phase0/CommandLine.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/CodeKnowledge.Phase0.Tests.csproj`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/CommandLineTests.cs`

**Interfaces:**
- Consumes: なし
- Produces: `CommandLine.Parse(string[] args) -> CommandSelection`、`ProbeMode`列挙値（`Mcp`、`SelfCheck`、`ConcurrencyWorker`、`Invalid`）

- [ ] **Step 1: solutionとプロジェクトを作成する**

Run:

```powershell
dotnet new sln --format slnx --name CodeKnowledge.Phase0
dotnet new console --framework net10.0 --name CodeKnowledge.Phase0 --output spikes/phase0/CodeKnowledge.Phase0
dotnet new xunit --framework net10.0 --name CodeKnowledge.Phase0.Tests --output spikes/phase0/CodeKnowledge.Phase0.Tests
dotnet sln CodeKnowledge.Phase0.slnx add spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj
dotnet sln CodeKnowledge.Phase0.slnx add spikes/phase0/CodeKnowledge.Phase0.Tests/CodeKnowledge.Phase0.Tests.csproj
dotnet add spikes/phase0/CodeKnowledge.Phase0.Tests/CodeKnowledge.Phase0.Tests.csproj reference spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj
```

Expected: 2プロジェクトがsolutionへ追加される。

- [ ] **Step 2: SDK、共通ビルド設定、パッケージ版を固定する**

`global.json`:

```json
{
  "sdk": {
    "version": "10.0.201",
    "rollForward": "latestPatch"
  }
}
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.9" />
    <PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.3" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
    <PackageVersion Include="ModelContextProtocol" Version="1.4.1" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
  </ItemGroup>
</Project>
```

両`csproj`からテンプレートが追加したバージョン属性を削除し、probeへ`Microsoft.Data.Sqlite`、`SQLitePCLRaw.bundle_e_sqlite3`、`Microsoft.Extensions.Hosting`、`ModelContextProtocol`、テストへ`Microsoft.NET.Test.Sdk`と`xunit.v3`を追加する。`SQLitePCLRaw.bundle_e_sqlite3`はprobeから直接参照し、中央管理した3.0.3を解決させる。probeの`csproj`には以下も追加する。

```xml
<ItemGroup>
  <InternalsVisibleTo Include="CodeKnowledge.Phase0.Tests" />
</ItemGroup>
```

`.gitignore`:

```gitignore
bin/
obj/
artifacts/
TestResults/
*.user
```

- [ ] **Step 3: 失敗するコマンド解析テストを書く**

`CommandLineTests.cs`:

```csharp
namespace CodeKnowledge.Phase0.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_WithNoArguments_DefaultsToMcp() =>
        Assert.Equal(ProbeMode.Mcp, CommandLine.Parse([]).Mode);

    [Theory]
    [InlineData("mcp", ProbeMode.Mcp)]
    [InlineData("self-check", ProbeMode.SelfCheck)]
    [InlineData("concurrency-worker", ProbeMode.ConcurrencyWorker)]
    [InlineData("unknown", ProbeMode.Invalid)]
    public void Parse_SelectsExpectedMode(string argument, ProbeMode expected)
    {
        Assert.Equal(expected, CommandLine.Parse([argument]).Mode);
    }
}
```

- [ ] **Step 4: テストが期待通り失敗することを確認する**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.CommandLineTests`

Expected: FAIL。`CommandLine`または`ProbeMode`が存在しないコンパイルエラー。

- [ ] **Step 5: 最小のコマンド解析を実装する**

`CommandLine.cs`:

```csharp
namespace CodeKnowledge.Phase0;

internal enum ProbeMode { Mcp, SelfCheck, ConcurrencyWorker, Invalid }

internal sealed record CommandSelection(ProbeMode Mode, string[] Arguments);

internal static class CommandLine
{
    public static CommandSelection Parse(string[] args)
    {
        if (args.Length == 0)
            return new(ProbeMode.Mcp, []);

        var mode = args[0] switch
        {
            "mcp" => ProbeMode.Mcp,
            "self-check" => ProbeMode.SelfCheck,
            "concurrency-worker" => ProbeMode.ConcurrencyWorker,
            _ => ProbeMode.Invalid
        };
        return new(mode, args[1..]);
    }
}
```

- [ ] **Step 6: テストとビルドを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.CommandLineTests`

Expected: PASS、失敗0件。

Run: `dotnet build CodeKnowledge.Phase0.slnx`

Expected: Build succeeded、warning 0、error 0。

- [ ] **Step 7: コミットする**

```powershell
git add global.json Directory.Build.props Directory.Packages.props .gitignore CodeKnowledge.Phase0.slnx spikes/phase0
git commit -m "build: scaffold phase 0 probe"
```

### Task 2: trigramとLIKEの検索probe

**Files:**
- Create: `spikes/phase0/CodeKnowledge.Phase0/SearchProbe.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/SearchTestDatabase.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/SearchProbeTests.cs`

**Interfaces:**
- Consumes: `Microsoft.Data.Sqlite.SqliteConnection`
- Produces: `SearchProbe.SelectRoute(string) -> SearchRoute`、`SearchProbe.Search(SqliteConnection, IEnumerable<string>) -> IReadOnlyCollection<long>`

- [ ] **Step 1: 振り分け、エスケープ、OR統合の失敗テストを書く**

テスト用DBに実テーブル`knowledge_records(id, title)`と`tokenize='trigram'`の`knowledge_fts(id UNINDEXED, title)`を作り、`注文完了メール仕様`と`100%_safe sui-memory`を両方へ登録する。次のテストを実装する。

```csharp
[Theory]
[InlineData("仕様", SearchRoute.Like)]
[InlineData("メール", SearchRoute.Fts)]
[InlineData("ｍａｉｌ", SearchRoute.Fts)]
public void SelectRoute_UsesUnicodeCodePointLength(string term, SearchRoute expected) =>
    Assert.Equal(expected, SearchProbe.SelectRoute(term));

[Fact]
public void Search_MergesFtsAndLikeHits()
{
    using var db = SearchTestDatabase.Create();
    var ids = SearchProbe.Search(db, ["メール", "仕様"]);
    Assert.Contains(1, ids);
}

[Theory]
[InlineData("sui-memory", 2)]
[InlineData("%", 2)]
[InlineData("_", 2)]
public void Search_TreatsMetaCharactersAsLiterals(string term, long expectedId)
{
    using var db = SearchTestDatabase.Create();
    Assert.Contains(expectedId, SearchProbe.Search(db, [term]));
}
```

`SearchTestDatabase.cs`は次の完全なfixtureとする。

```csharp
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0.Tests;

internal static class SearchTestDatabase
{
    public static SqliteConnection Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE knowledge_records(id INTEGER PRIMARY KEY, title TEXT NOT NULL);
            CREATE VIRTUAL TABLE knowledge_fts USING fts5(
                id UNINDEXED,
                title,
                tokenize='trigram'
            );
            INSERT INTO knowledge_records(id, title) VALUES
                (1, '注文完了メール仕様'),
                (2, '100%_safe sui-memory');
            INSERT INTO knowledge_fts(id, title) VALUES
                (1, '注文完了メール仕様'),
                (2, '100%_safe sui-memory');
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.SearchProbeTests`

Expected: FAIL。`SearchProbe`が存在しない。

- [ ] **Step 3: 最小の検索実装を書く**

`SearchProbe.cs`の中核実装:

```csharp
using System.Text;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0;

internal enum SearchRoute { Fts, Like }

internal static class SearchProbe
{
    public static SearchRoute SelectRoute(string raw)
    {
        var value = Normalize(raw);
        return value.EnumerateRunes().Count() >= 3 ? SearchRoute.Fts : SearchRoute.Like;
    }

    public static IReadOnlyCollection<long> Search(
        SqliteConnection connection,
        IEnumerable<string> rawTerms)
    {
        var ids = new HashSet<long>();
        foreach (var term in rawTerms.Select(Normalize).Where(static x => x.Length > 0))
        {
            using var command = connection.CreateCommand();
            if (SelectRoute(term) is SearchRoute.Fts)
            {
                command.CommandText =
                    "SELECT CAST(id AS INTEGER) FROM knowledge_fts WHERE knowledge_fts MATCH $term";
                command.Parameters.AddWithValue("$term", $"\"{term.Replace("\"", "\"\"")}\"");
            }
            else
            {
                command.CommandText =
                    "SELECT id FROM knowledge_records WHERE title LIKE $term ESCAPE '\\'";
                var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                command.Parameters.AddWithValue("$term", $"%{escaped}%");
            }

            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
        }
        return ids;
    }

    private static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).Trim();
}
```

- [ ] **Step 4: 検索テストを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.SearchProbeTests`

Expected: PASS、すべてのFTS、LIKE、メタ文字ケースが成功する。

- [ ] **Step 5: コミットする**

```powershell
git add spikes/phase0/CodeKnowledge.Phase0/SearchProbe.cs spikes/phase0/CodeKnowledge.Phase0.Tests/SearchProbeTests.cs
git commit -m "test: verify sqlite hybrid search assumptions"
```

### Task 3: SQLite自己診断とJSON出力契約

**Files:**
- Create: `spikes/phase0/CodeKnowledge.Phase0/ProbeReport.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0/SqliteProbe.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/TestWorkspace.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/SqliteProbeTests.cs`

**Interfaces:**
- Consumes: `SearchProbe.Search`
- Produces: `SqliteProbe.Run(string databasePath) -> ProbeReport`、終了コード定数`ProbeExitCodes`

- [ ] **Step 1: 失敗する自己診断テストを書く**

```csharp
[Fact]
public void Run_VerifiesAllSqliteAssumptions()
{
    using var workspace = new TestWorkspace();
    var report = SqliteProbe.Run(workspace.PathFor("self-check.db"));

    Assert.Equal("ok", report.Status);
    Assert.All(report.Checks, check => Assert.True(check.Passed, check.Message));
    Assert.True(Version.Parse(report.Details["sqliteVersion"]) >= new Version(3, 34, 0));
    Assert.Equal("wal", report.Details["journalMode"]);
    Assert.Equal("5000", report.Details["busyTimeout"]);
    Assert.Equal("1", report.Details["foreignKeys"]);
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.SqliteProbeTests`

Expected: FAIL。`SqliteProbe`と`ProbeReport`が存在しない。

- [ ] **Step 3: 結果契約と終了コードを実装する**

`ProbeReport.cs`:

```csharp
namespace CodeKnowledge.Phase0;

internal static class ProbeExitCodes
{
    public const int Success = 0;
    public const int CheckFailed = 1;
    public const int InvalidArguments = 2;
    public const int UnexpectedError = 3;
}

internal sealed record ProbeCheck(string Id, bool Passed, string Message);

internal sealed record ProbeReport(
    string Mode,
    string Status,
    string ExecutableVersion,
    IReadOnlyList<ProbeCheck> Checks,
    IReadOnlyDictionary<string, string> Details);
```

`TestWorkspace.cs`:

```csharp
namespace CodeKnowledge.Phase0.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ck-phase0-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
```

- [ ] **Step 4: SQLite自己診断を実装する**

`SqliteProbe.Run`はファイルDBを開き、1つの接続上で次を実行する。

```csharp
using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate");
connection.Open();
Execute(connection, "PRAGMA foreign_keys = ON;");
Execute(connection, "PRAGMA busy_timeout = 5000;");
var journalMode = Scalar(connection, "PRAGMA journal_mode = WAL;");
var sqliteVersion = Scalar(connection, "SELECT sqlite_version();");
Execute(connection, "CREATE TABLE knowledge_records(id INTEGER PRIMARY KEY, title TEXT NOT NULL);");
Execute(connection, "CREATE VIRTUAL TABLE knowledge_fts USING fts5(id UNINDEXED, title, tokenize='trigram');");
```

固定データを登録後、`SearchProbe.Search(connection, ["メール"])`、`["仕様"]`、`["確認"]`、`["メール", "仕様"]`を実行する。各検証を`ProbeCheck`へ追加し、全件成功時は`Status = "ok"`、1件でも失敗した場合は`Status = "failed"`とする。SQLite例外も`sqlite.runtime`チェックの失敗へ変換し、例外型とメッセージをstderrへ出す上位層のために再throwしない。

SQL helperは`SqliteProbe`のprivate static methodとして次のシグネチャで実装し、Task 2と同じくSQL値を文字列連結しない。

```csharp
private static void Execute(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

private static string Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"Scalar query returned null: {sql}");
}
```

- [ ] **Step 5: 自己診断テストと全体テストを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.SqliteProbeTests`

Expected: PASS。SQLiteバージョン3.34.0以上、trigram作成、検索、PRAGMAが成功する。

Run: `dotnet test CodeKnowledge.Phase0.slnx`

Expected: PASS、失敗0件。

- [ ] **Step 6: コミットする**

```powershell
git add spikes/phase0/CodeKnowledge.Phase0 spikes/phase0/CodeKnowledge.Phase0.Tests
git commit -m "feat: add sqlite phase 0 self check"
```

### Task 4: 複数プロセス同時実行probe

**Files:**
- Create: `spikes/phase0/CodeKnowledge.Phase0/ConcurrencyWorker.cs`
- Modify: `spikes/phase0/CodeKnowledge.Phase0/Program.cs`（承認済み境界変更: `concurrency-worker`だけを先行して実DLLからルーティングする）
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/ProcessRunner.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/ConcurrencyProbeTests.cs`

**Interfaces:**
- Consumes: worker引数`--database`、`--worker-id`、`--iterations`、`--start-file`
- Produces: `ConcurrencyWorker.RunAsync(string[] args, TextWriter stdout, CancellationToken) -> Task<int>`

`ProcessRunner`の契約は`RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken) -> Task<ProcessResult>`、`ProcessResult(int ExitCode, string StandardOutput, string StandardError)`とする。

- [ ] **Step 1: 4プロセス×50書き込みの失敗テストを書く**

テストは一時DBへ次のテーブルを作成し、同じprobe DLLを`dotnet <assembly> concurrency-worker ...`として4回起動する。

```sql
CREATE TABLE operations(
    operation_id TEXT PRIMARY KEY,
    worker_id INTEGER NOT NULL,
    sequence INTEGER NOT NULL,
    payload TEXT NOT NULL
);
```

全worker起動後に`start.signal`を作成し、各プロセスの終了コードが0、stdoutのJSONが`status = ok`、行数と`COUNT(DISTINCT operation_id)`が200であることを検証する。

`ProcessRunner.cs`:

```csharp
using System.Diagnostics;

namespace CodeKnowledge.Phase0.Tests;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.ConcurrencyProbeTests`

Expected: FAIL。`ConcurrencyWorker`またはCLIルーティングが存在しない。

- [ ] **Step 3: workerを実装する**

workerは開始ファイルを最大15秒待ち、各反復で新しい接続を開いて以下を設定する。

```csharp
connection.Open();
Execute(connection, "PRAGMA foreign_keys = ON;");
Execute(connection, "PRAGMA busy_timeout = 5000;");
Execute(connection, "PRAGMA journal_mode = WAL;");
```

各反復は`BEGIN`、`INSERT`、`SELECT COUNT(*)`、`COMMIT`を実行する。`operation_id`は`$"{workerId:D2}-{sequence:D4}"`とし、SQL値はすべてパラメータで渡す。完了時は以下の形をstdoutへ1件だけ出力する。

```json
{"mode":"concurrency-worker","status":"ok","workerId":1,"writes":50}
```

引数不正は終了コード2、SQLiteまたは予期しない失敗は終了コード3とする。

`Program.cs`は`concurrency-worker`だけを`ConcurrencyWorker.RunAsync`へルーティングする。`self-check`と`mcp`のルーティングはTask 5まで進めない。

- [ ] **Step 4: 同時実行テストを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.ConcurrencyProbeTests`

Expected: 4プロセスすべて終了コード0、`database is locked`なし、200行、重複0。

- [ ] **Step 5: テストを3回繰り返して偶発成功でないことを確認する**

Run:

```powershell
1..3 | ForEach-Object { dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.ConcurrencyProbeTests }
```

Expected: 3回ともPASS。

- [ ] **Step 6: コミットする**

```powershell
git add docs/superpowers/plans/2026-07-11-code-knowledge-phase0.md spikes/phase0/CodeKnowledge.Phase0/Program.cs spikes/phase0/CodeKnowledge.Phase0/ConcurrencyWorker.cs spikes/phase0/CodeKnowledge.Phase0.Tests
git commit -m "test: verify sqlite multi-process concurrency"
```

### Task 5: MCP stdio serverと残りの実行モード統合

**Files:**
- Create: `spikes/phase0/CodeKnowledge.Phase0/McpProbeTool.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0/McpServerRunner.cs`
- Modify: `spikes/phase0/CodeKnowledge.Phase0/Program.cs`（Task 4の`concurrency-worker`ルーティングを維持し、`self-check`と`mcp`を完成させる）
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/McpProbeTests.cs`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/CommandExecutionTests.cs`

**Interfaces:**
- Consumes: `CommandLine.Parse`、`SqliteProbe.Run`、`ConcurrencyWorker.RunAsync`
- Produces: MCP Tool `phase0_probe`、`McpServerRunner.RunAsync(CancellationToken)`、Task 4のworkerルートを含む3モードの実行可能CLI

- [ ] **Step 1: MCP Tool呼び出しとstdout純度の失敗テストを書く**

`McpProbeTests`は`StdioClientTransport`で現在のprobe DLLを起動する。

```csharp
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "phase0-test",
    Command = "dotnet",
    Arguments = [typeof(CommandLine).Assembly.Location, "mcp"]
});
await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
Assert.Contains(tools, tool => tool.Name == "phase0_probe");
var result = await client.CallToolAsync("phase0_probe", cancellationToken: TestContext.Current.CancellationToken);
var json = JsonSerializer.Serialize(result.StructuredContent);
Assert.Contains("\"status\":\"ok\"", json);
```

別テストでは、`self-check`のstdoutが単一JSONとしてparse可能、未知モードが終了コード2、stderrに理由を含むことを確認する。

- [ ] **Step 2: テストが失敗することを確認する**

Run:

```powershell
dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.McpProbeTests
dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.CommandExecutionTests
```

Expected: FAIL。MCP server、Tool、または`self-check`/`mcp`のProgramルーティングが存在しない。`concurrency-worker`のルーティングはTask 4で実装済み。

- [ ] **Step 3: 診断Toolを実装する**

`McpProbeTool.cs`:

```csharp
using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace CodeKnowledge.Phase0;

[McpServerToolType]
public static class McpProbeTool
{
    [McpServerTool(Name = "phase0_probe"), Description("Returns Phase 0 MCP and SQLite diagnostics.")]
    public static object Probe() => new
    {
        status = "ok",
        executableVersion = typeof(McpProbeTool).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
        processId = Environment.ProcessId,
        sqliteVersion = GetSqliteVersion(),
        serverTimestampUtc = DateTimeOffset.UtcNow
    };

    private static string GetSqliteVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return (string)command.ExecuteScalar()!;
    }
}
```

- [ ] **Step 4: stderr loggerとMCP serverを実装する**

`McpServerRunner.RunAsync`は公式SDKのhosting APIを使う。

```csharp
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync(cancellationToken);
return ProbeExitCodes.Success;
```

- [ ] **Step 5: Programで残り2モードを統合し、3モードを完成させる**

Task 4で追加した`concurrency-worker`ルートを維持し、`self-check`と`mcp`のルーティングを追加する。

```csharp
using System.Text.Json;
using CodeKnowledge.Phase0;

var selection = CommandLine.Parse(args);
try
{
    return selection.Mode switch
    {
        ProbeMode.Mcp => await McpServerRunner.RunAsync(CancellationToken.None),
        ProbeMode.SelfCheck => RunSelfCheck(selection.Arguments),
        ProbeMode.ConcurrencyWorker => await ConcurrencyWorker.RunAsync(
            selection.Arguments, Console.Out, CancellationToken.None),
        _ => InvalidMode(args)
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"unexpected_error: {exception.GetType().Name}: {exception.Message}");
    return ProbeExitCodes.UnexpectedError;
}

static int RunSelfCheck(string[] modeArgs)
{
    if (modeArgs.Length != 0) return ProbeExitCodes.InvalidArguments;
    var path = Path.Combine(Path.GetTempPath(), $"ck-phase0-{Guid.NewGuid():N}.db");
    try
    {
        var report = SqliteProbe.Run(path);
        Console.Out.WriteLine(JsonSerializer.Serialize(report));
        return report.Status == "ok" ? ProbeExitCodes.Success : ProbeExitCodes.CheckFailed;
    }
    finally
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
}

static int InvalidMode(string[] modeArgs)
{
    Console.Error.WriteLine($"invalid_arguments: unsupported mode '{string.Join(' ', modeArgs)}'");
    return ProbeExitCodes.InvalidArguments;
}
```

- [ ] **Step 6: MCP、CLI、全体テストを通す**

Run:

```powershell
dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.McpProbeTests
dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.CommandExecutionTests
```

Expected: PASS。Tool一覧と呼び出し成功、stdoutへログ混入なし。

Run: `dotnet test CodeKnowledge.Phase0.slnx`

Expected: PASS、失敗0件。

- [ ] **Step 7: コミットする**

```powershell
git add spikes/phase0/CodeKnowledge.Phase0 spikes/phase0/CodeKnowledge.Phase0.Tests
git commit -m "feat: add phase 0 stdio mcp probe"
```

### Task 6: framework-dependent単一ファイル発行

**Files:**
- Modify: `spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj`
- Create: `spikes/phase0/CodeKnowledge.Phase0.Tests/PublishSmokeTests.cs`

**Interfaces:**
- Consumes: 完成したprobe CLI
- Produces: `artifacts/phase0/win-x64/CodeKnowledge.Phase0.exe`と機械可読な成果物一覧

- [ ] **Step 1: 発行済みEXEを要求する失敗テストを書く**

テストは一意な`artifacts/test-publish/<guid>`へ次を実行する。

```powershell
dotnet publish spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj --configuration Release --runtime win-x64 --self-contained false --output <temp>
```

続けて`CodeKnowledge.Phase0.exe self-check`を実行し、終了コード0、stdoutのJSONが`status = ok`、stderrに通常ログがないことを確認する。出力ディレクトリの全ファイル名をソートしてテスト出力へ記録する。

- [ ] **Step 2: 現在の発行テスト結果を確認する**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.PublishSmokeTests`

Expected: FAIL。単一ファイル発行設定が未定義、または成果物条件が満たされない。

- [ ] **Step 3: 発行設定を追加する**

probeの`csproj`へ追加する。

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>false</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

- [ ] **Step 4: 発行スモークテストを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx -- --filter-class CodeKnowledge.Phase0.Tests.PublishSmokeTests`

Expected: PASS。発行済みEXEの`self-check`が終了コード0。

Run:

```powershell
dotnet publish spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/phase0/win-x64
Get-ChildItem -File artifacts/phase0/win-x64 | Select-Object Name,Length
```

Expected: `CodeKnowledge.Phase0.exe`を含む成果物一覧が表示される。隣接ファイルがある場合は削除せずTask 7で必須配布物として記録する。

- [ ] **Step 5: Release構成を含む全テストを通す**

Run: `dotnet test CodeKnowledge.Phase0.slnx --configuration Release`

Expected: PASS、失敗0件、warning 0。

- [ ] **Step 6: コミットする**

```powershell
git add spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj spikes/phase0/CodeKnowledge.Phase0.Tests/PublishSmokeTests.cs
git commit -m "test: verify phase 0 single-file publish"
```

### Task 7: 再実行手順と3クライアント完了ゲート

**Files:**
- Create: `spikes/phase0/README.md`

**Interfaces:**
- Consumes: 自動テスト結果、発行成果物、各クライアントでの`phase0_probe`結果
- Produces: Phase 0の承認可能な検証記録

- [ ] **Step 1: READMEへ自動検証手順を書く**

以下のコマンドをそのまま掲載する。

```powershell
dotnet --info
dotnet restore CodeKnowledge.Phase0.slnx
dotnet test CodeKnowledge.Phase0.slnx --configuration Release
dotnet publish spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/phase0/win-x64
artifacts/phase0/win-x64/CodeKnowledge.Phase0.exe self-check
```

- [ ] **Step 2: クライアント設定と結果表を書く**

Cursor、GitHub Copilot in VS Code、Claude Codeそれぞれについて、要件定義書10.10の設定例を発行済みEXEの絶対パスへ置き換えて掲載する。結果表は次の列を持つ。

```markdown
| クライアント | バージョン | 検証日 | phase0_probe | EXE版 | SQLite版 | 設定・所見 |
|---|---|---|---|---|---|---|
| Cursor | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |
| GitHub Copilot in VS Code | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |
| Claude Code | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 | 未実施 |
```

この表の`未実施`は手動検証前の明示的な状態であり、Phase 0完了を意味しない。実装担当は自動検証までをコミットし、3クライアントの実機操作が必要な時点でユーザーへ引き渡す。

- [ ] **Step 3: 成果物と技術前提の記録欄を書く**

READMEへ次を追加する。

```markdown
## 発行成果物

`Get-ChildItem`の実測結果をファイル名・サイズ付きで記録する。

## Deviations

要件定義書と異なる実測結果がない場合は「なし」と記録する。差異がある場合は、前提、実測、再現手順、候補となる対応を記録し、要件改訂までPhase 1をブロックする。

## Phase 0完了判定

- [ ] Release構成の全自動テスト成功
- [ ] 発行済みEXEのself-check成功
- [ ] Cursorでphase0_probe成功
- [ ] GitHub Copilot in VS Codeでphase0_probe成功
- [ ] Claude Codeでphase0_probe成功
- [ ] 発行成果物一覧を記録
- [ ] Deviationsが「なし」、または要件改訂が承認済み
- [ ] ユーザーがPhase 0完了とPhase 1移行を承認
```

- [ ] **Step 4: 自動検証を再実行してREADMEへ実測値を記録する**

Run: `dotnet test CodeKnowledge.Phase0.slnx --configuration Release`

Expected: PASS、失敗0件。

Run: `artifacts/phase0/win-x64/CodeKnowledge.Phase0.exe self-check`

Expected: 終了コード0、`status`が`ok`の単一JSON。

自動で確定できるSDK、runtime、SQLite、EXE、成果物の実測値だけをREADMEへ記入する。手動クライアント欄を推測で成功へ変更しない。

- [ ] **Step 5: コミットする**

```powershell
git add spikes/phase0/README.md
git commit -m "docs: add phase 0 verification runbook"
```

- [ ] **Step 6: ユーザーへ手動検証を引き渡す**

Cursor、GitHub Copilot in VS Code、Claude Codeで`phase0_probe`を実行するための発行済みEXE絶対パスと設定箇所を提示する。3結果が揃った後にREADMEを更新し、`docs: record phase 0 client verification`として別コミットする。全完了条件とユーザー承認が揃うまで、Phase 1の設計・実装を開始しない。

---

## 計画完了時の検証コマンド

```powershell
dotnet --version
dotnet restore CodeKnowledge.Phase0.slnx
dotnet build CodeKnowledge.Phase0.slnx --configuration Release --no-restore
dotnet test CodeKnowledge.Phase0.slnx --configuration Release --no-build
dotnet publish spikes/phase0/CodeKnowledge.Phase0/CodeKnowledge.Phase0.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/phase0/win-x64
artifacts/phase0/win-x64/CodeKnowledge.Phase0.exe self-check
git status --short
```

期待結果:

- SDKは10.0.201系
- restore、build、test、publishがすべて終了コード0
- buildはwarning 0、error 0
- testは失敗0件
- 発行済みEXEのJSONは`status = "ok"`
- `git status --short`に要件定義書以外の意図しない変更がない

## 実装時の一次資料

- MCP C# SDK公式リポジトリ: <https://github.com/modelcontextprotocol/csharp-sdk>
- ModelContextProtocol 1.4.1: <https://www.nuget.org/packages/ModelContextProtocol/1.4.1>
- Microsoft.Data.Sqlite 10.0.9: <https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.9>
- SQLitePCLRaw.bundle_e_sqlite3 3.0.3: <https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.3>
- Microsoft.Extensions.Hosting 10.0.9: <https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.9>
- xunit.v3 3.2.2: <https://www.nuget.org/packages/xunit.v3/3.2.2>
- Microsoft.NET.Test.Sdk 18.7.0: <https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.7.0>
- Microsoft.Data.SqliteのWAL説明: <https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/async>
