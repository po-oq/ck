# Code Knowledge Phase 1 実装計画

> **エージェント作業者向け:** 必須サブスキルとして、タスク単位の実装には`superpowers:subagent-driven-development`（推奨）または`superpowers:executing-plans`を使用すること。進捗管理には各手順のチェックボックス（`- [ ]`）を使用する。

**目標:** Code Knowledgeの最小実用版として、`resolve_project`・`search_knowledge`・`get_knowledge`・`save_knowledge`の4 MCP Toolを持つ`CodeKnowledge.Mcp.exe`を、層分離された本番コードとして`src/`配下へ実装する。

**アーキテクチャ:** 3プロジェクト構成（`Core` = Domain + Application、`Infrastructure` = SQLite / Git CLI / ハッシュ、`Mcp` = stdioアダプター）。依存方向は`Mcp → Core ← Infrastructure`で、CoreはMCP SDK・SQLite・Gitに依存しない。DBはEXE隣接`knowledge.db`（環境変数`CODEKNOWLEDGE_DB_PATH`で上書き）、`PRAGMA user_version`によるマイグレーションv1で10テーブルを作成する。設計書: `docs/superpowers/specs/2026-07-11-code-knowledge-phase1-design.md`

**技術スタック:** Windows 11、.NET SDK 10.0.201、`net10.0`、C#、Microsoft.Data.Sqlite 10.0.9、SQLitePCLRaw.bundle_e_sqlite3 3.0.3、ModelContextProtocol 1.4.1、Microsoft.Extensions.Hosting 10.0.9、xunit.v3 3.2.2、SQLite FTS5（trigram）、git CLI

## 全体制約

- 要件定義書は`docs/code-knowledge-tool-requirements-v2.md`、設計書は`docs/superpowers/specs/2026-07-11-code-knowledge-phase1-design.md`。乖離が出た場合は実装を止めて記録・相談する。
- Phase 0のソースコード（`spikes/phase0/`）をコピー・参照しない。パッケージバージョンはPhase 0で実証済みの`Directory.Packages.props`の版を使用する。
- CoreプロジェクトはMCP SDK・Microsoft.Data.Sqlite・プロセス起動APIへの参照を持たない。
- テストは一時ディレクトリの一時DB・一時Gitリポジトリのみを使用する。EXE隣接の実DBへ接続しない。
- すべてのSQLはパラメータ化する。検索には必ず`project_id`条件を付ける。
- stdoutはMCP通信専用。ログはstderrのみ。
- 各タスクはTDD（失敗テスト → 最小実装 → 成功確認 → コミット）で進める。
- テスト実行は`dotnet test CodeKnowledge.slnx`を基本とし、タスク内では対象プロジェクトのみに絞ってよい（例: `dotnet test tests/CodeKnowledge.Core.Tests`）。

## ファイル構成

| パス | 責務 |
|---|---|
| `CodeKnowledge.slnx` | Phase 1本番ソリューション（src 3 + tests 3） |
| `src/CodeKnowledge.Core/Domain/*.cs` | Project / Knowledge / KnowledgeVersion / Fact / Inference / Evidence / Relation / Confidence / RelationKind |
| `src/CodeKnowledge.Core/Errors/CodeKnowledgeException.cs` | エラーコード付き例外（Tool共通エラー契約の源） |
| `src/CodeKnowledge.Core/Git/IGitRepository.cs` | Git操作の抽象（コンテキスト解決・コミット時点ファイル読取） |
| `src/CodeKnowledge.Core/Projects/RemoteUrlNormalizer.cs` | remote URL正規化8ルール（要件5.3.2） |
| `src/CodeKnowledge.Core/Projects/ProjectIdResolver.cs` | config > remote > localの`project_id`決定 |
| `src/CodeKnowledge.Core/Projects/IProjectStore.cs` | projectsテーブル抽象 |
| `src/CodeKnowledge.Core/Projects/ResolveProjectUseCase.cs` | プロジェクト解決・upsert・`project_id_changed`警告 |
| `src/CodeKnowledge.Core/Search/KeywordPreparation.cs` | NFKC正規化・FTS/LIKE振り分け・エスケープ |
| `src/CodeKnowledge.Core/Search/SearchKnowledgeUseCase.cs` | ハイブリッド検索のマージ・ランキング |
| `src/CodeKnowledge.Core/Knowledge/IKnowledgeStore.cs` | knowledge系テーブル抽象（保存・取得・検索） |
| `src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs` | 保存バリデーション・正規化・ハッシュ付与 |
| `src/CodeKnowledge.Core/Knowledge/GetKnowledgeUseCase.cs` | ナレッジ取得 |
| `src/CodeKnowledge.Core/Hashing/ContentHasher.cs` | file_hash / symbol_hash計算（空白正規化） |
| `src/CodeKnowledge.Infrastructure/Database/DatabasePathResolver.cs` | 環境変数 > EXE隣接のDBパス解決 |
| `src/CodeKnowledge.Infrastructure/Database/SqliteConnectionFactory.cs` | PRAGMA付き接続生成 |
| `src/CodeKnowledge.Infrastructure/Database/MigrationRunner.cs` | user_version管理・バックアップ・v1スキーマ |
| `src/CodeKnowledge.Infrastructure/Git/GitCommandRunner.cs` | git CLI実行（引数配列・タイムアウト・UTF-8） |
| `src/CodeKnowledge.Infrastructure/Git/GitCliRepository.cs` | `IGitRepository`実装 |
| `src/CodeKnowledge.Infrastructure/Stores/SqliteProjectStore.cs` | `IProjectStore`実装 |
| `src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs` | `IKnowledgeStore`実装（トランザクション・FTS同期） |
| `src/CodeKnowledge.Mcp/Program.cs` | 起動シーケンスとDI・stderrログ構成 |
| `src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs` | 4 Toolのアダプター |
| `tests/CodeKnowledge.Core.Tests/*.cs` | 純粋ロジックとフェイクによるユースケース単体テスト |
| `tests/CodeKnowledge.Infrastructure.Tests/*.cs` | 実SQLite・実Gitリポジトリによる統合テスト |
| `tests/CodeKnowledge.Mcp.Tests/*.cs` | 発行EXEのE2E（プロトコルクライアント） |
| `README.md` | 配置・DB運用ルール・3クライアント設定・Agent行動ルール・検証記録 |

---

### Task 1: ソリューション基盤

**Files:**
- Create: `CodeKnowledge.slnx`
- Create: `src/CodeKnowledge.Core/CodeKnowledge.Core.csproj`
- Create: `src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj`
- Create: `src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj`
- Create: `tests/CodeKnowledge.Core.Tests/CodeKnowledge.Core.Tests.csproj`
- Create: `tests/CodeKnowledge.Infrastructure.Tests/CodeKnowledge.Infrastructure.Tests.csproj`
- Create: `tests/CodeKnowledge.Mcp.Tests/CodeKnowledge.Mcp.Tests.csproj`

**Interfaces:**
- Consumes: `Directory.Build.props`、`Directory.Packages.props`（既存）
- Produces: ビルド可能な空の3+3プロジェクト

- [x] **Step 1: プロジェクトを作成し参照を張る**

Run:

```powershell
dotnet new sln --format slnx --name CodeKnowledge
dotnet new classlib --framework net10.0 --name CodeKnowledge.Core --output src/CodeKnowledge.Core
dotnet new classlib --framework net10.0 --name CodeKnowledge.Infrastructure --output src/CodeKnowledge.Infrastructure
dotnet new console --framework net10.0 --name CodeKnowledge.Mcp --output src/CodeKnowledge.Mcp
dotnet new xunit --framework net10.0 --name CodeKnowledge.Core.Tests --output tests/CodeKnowledge.Core.Tests
dotnet new xunit --framework net10.0 --name CodeKnowledge.Infrastructure.Tests --output tests/CodeKnowledge.Infrastructure.Tests
dotnet new xunit --framework net10.0 --name CodeKnowledge.Mcp.Tests --output tests/CodeKnowledge.Mcp.Tests
dotnet sln CodeKnowledge.slnx add src/CodeKnowledge.Core/CodeKnowledge.Core.csproj src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj tests/CodeKnowledge.Core.Tests/CodeKnowledge.Core.Tests.csproj tests/CodeKnowledge.Infrastructure.Tests/CodeKnowledge.Infrastructure.Tests.csproj tests/CodeKnowledge.Mcp.Tests/CodeKnowledge.Mcp.Tests.csproj
dotnet add src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj reference src/CodeKnowledge.Core/CodeKnowledge.Core.csproj
dotnet add src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj reference src/CodeKnowledge.Core/CodeKnowledge.Core.csproj src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj
dotnet add tests/CodeKnowledge.Core.Tests/CodeKnowledge.Core.Tests.csproj reference src/CodeKnowledge.Core/CodeKnowledge.Core.csproj
dotnet add tests/CodeKnowledge.Infrastructure.Tests/CodeKnowledge.Infrastructure.Tests.csproj reference src/CodeKnowledge.Core/CodeKnowledge.Core.csproj src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj
dotnet add tests/CodeKnowledge.Mcp.Tests/CodeKnowledge.Mcp.Tests.csproj reference src/CodeKnowledge.Core/CodeKnowledge.Core.csproj src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj
```

Expected: 6プロジェクトがソリューションへ追加される。テンプレートが生成した`Class1.cs`は削除する。

- [x] **Step 2: csprojへパッケージ参照と発行設定を書く**

`src/CodeKnowledge.Infrastructure/CodeKnowledge.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeKnowledge.Core\CodeKnowledge.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CodeKnowledge.Mcp</RootNamespace>
    <AssemblyName>CodeKnowledge.Mcp</AssemblyName>
    <PublishSingleFile Condition="'$(RuntimeIdentifier)' != ''">true</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeKnowledge.Core\CodeKnowledge.Core.csproj" />
    <ProjectReference Include="..\CodeKnowledge.Infrastructure\CodeKnowledge.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

テストプロジェクト3つには`Microsoft.NET.Test.Sdk`と`xunit.v3`の`PackageReference`を設定する（バージョンは`Directory.Packages.props`が供給。テンプレート生成csprojにバージョン属性が付いていたら削除する）。`CodeKnowledge.Core.csproj`はパッケージ参照なしのclasslibのままとする。

- [x] **Step 3: ビルドとテストが通ることを確認する**

Run: `dotnet build CodeKnowledge.slnx && dotnet test CodeKnowledge.slnx`
Expected: ビルド成功、テンプレートのプレースホルダーテストが成功（または0件で成功）。

- [x] **Step 4: コミット**

```bash
git add CodeKnowledge.slnx src/ tests/
git commit -m "chore: scaffold phase 1 solution with core, infrastructure, mcp projects"
```

---

### Task 2: Coreドメインモデルとエラー契約

**Files:**
- Create: `src/CodeKnowledge.Core/Domain/Confidence.cs`
- Create: `src/CodeKnowledge.Core/Domain/RelationKind.cs`
- Create: `src/CodeKnowledge.Core/Domain/Models.cs`
- Create: `src/CodeKnowledge.Core/Errors/CodeKnowledgeException.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ConfidenceTests.cs`

**Interfaces:**
- Consumes: なし
- Produces: `Confidence.TryParse(string?, out Confidence)`、`ConfidenceExtensions.ToDbValue(this Confidence)`、`RelationKind.All`（9種別の文字列集合）、`CodeKnowledgeException(string code, string message)`、ドメインrecord群

- [x] **Step 1: 失敗するテストを書く**

`tests/CodeKnowledge.Core.Tests/ConfidenceTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Tests;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData("high", Confidence.High)]
    [InlineData("medium", Confidence.Medium)]
    [InlineData("low", Confidence.Low)]
    public void TryParse_accepts_defined_values(string input, Confidence expected)
    {
        Assert.True(ConfidenceParser.TryParse(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HIGH")]
    [InlineData("0.9")]
    [InlineData("certain")]
    public void TryParse_rejects_undefined_values(string? input)
    {
        Assert.False(ConfidenceParser.TryParse(input, out _));
    }

    [Fact]
    public void ToDbValue_roundtrips()
    {
        Assert.Equal("high", Confidence.High.ToDbValue());
        Assert.Equal("medium", Confidence.Medium.ToDbValue());
        Assert.Equal("low", Confidence.Low.ToDbValue());
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラー（`Confidence`未定義）で失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Core/Domain/Confidence.cs`:

```csharp
namespace CodeKnowledge.Core.Domain;

public enum Confidence
{
    High,
    Medium,
    Low,
}

public static class ConfidenceParser
{
    public static bool TryParse(string? value, out Confidence confidence)
    {
        confidence = value switch
        {
            "high" => Confidence.High,
            "medium" => Confidence.Medium,
            "low" => Confidence.Low,
            _ => (Confidence)(-1),
        };
        return (int)confidence >= 0;
    }
}

public static class ConfidenceExtensions
{
    public static string ToDbValue(this Confidence confidence) => confidence switch
    {
        Confidence.High => "high",
        Confidence.Medium => "medium",
        Confidence.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(confidence)),
    };
}
```

`src/CodeKnowledge.Core/Domain/RelationKind.cs`:

```csharp
namespace CodeKnowledge.Core.Domain;

public static class RelationKind
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "calls", "implements", "inherits", "reads", "writes",
        "publishes", "subscribes", "configured-by", "tested-by",
    };
}
```

`src/CodeKnowledge.Core/Domain/Models.cs`:

```csharp
namespace CodeKnowledge.Core.Domain;

public sealed record Project(
    string ProjectId,
    string DisplayName,
    string RepositoryRoot,
    string? RemoteUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeSummary(
    string KnowledgeId,
    string CanonicalKey,
    string Title,
    string Summary,
    string CommitHash,
    Confidence Confidence,
    DateTimeOffset UpdatedAt);

public sealed record EvidenceRecord(
    string Id,
    string FilePath,
    string? SymbolId,
    string SymbolName,
    string? SymbolKind,
    string? Signature,
    int? StartLine,
    int? EndLine,
    string CommitHash,
    string FileHash,
    string? SymbolHash,
    string? Reason);

public sealed record FactRecord(string Id, string Text, IReadOnlyList<string> EvidenceIds);

public sealed record InferenceRecord(
    string Id,
    string Text,
    Confidence Confidence,
    string Reason,
    IReadOnlyList<string> EvidenceIds);

public sealed record RelationRecord(string Id, string FromSymbol, string ToSymbol, string Kind);

public sealed record KnowledgeDetail(
    string KnowledgeId,
    string CanonicalKey,
    string Title,
    string VersionId,
    string CommitHash,
    string? BranchName,
    string OriginalQuestion,
    string Summary,
    Confidence Confidence,
    string Tags,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FactRecord> Facts,
    IReadOnlyList<InferenceRecord> Inferences,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<RelationRecord> Relations);
```

`src/CodeKnowledge.Core/Errors/CodeKnowledgeException.cs`:

```csharp
namespace CodeKnowledge.Core.Errors;

public sealed class CodeKnowledgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public const string GitRepositoryRequired = "git_repository_required";
    public const string GitNotFound = "git_not_found";
    public const string InvalidArguments = "invalid_arguments";
    public const string FactRequiresEvidence = "fact_requires_evidence";
    public const string KnowledgeNotFound = "knowledge_not_found";
    public const string SchemaVersionUnsupported = "schema_version_unsupported";
    public const string DatabaseBusy = "database_busy";
    public const string InternalError = "internal_error";
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core tests/CodeKnowledge.Core.Tests
git commit -m "feat: add core domain model and error contract"
```

---

### Task 3: マイグレーション基盤とv1スキーマ

**Files:**
- Create: `src/CodeKnowledge.Infrastructure/Database/DatabasePathResolver.cs`
- Create: `src/CodeKnowledge.Infrastructure/Database/SqliteConnectionFactory.cs`
- Create: `src/CodeKnowledge.Infrastructure/Database/MigrationRunner.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/MigrationRunnerTests.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/TestDatabase.cs`

**Interfaces:**
- Consumes: `CodeKnowledgeException`
- Produces: `DatabasePathResolver.Resolve() -> string`、`SqliteConnectionFactory(string dbPath).Open() -> SqliteConnection`（PRAGMA適用済み）、`MigrationRunner.Apply(SqliteConnectionFactory factory, string dbPath)`

- [x] **Step 1: 失敗するテストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/TestDatabase.cs`:

```csharp
using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class TestDatabase : IDisposable
{
    public string Directory { get; } =
        Path.Combine(Path.GetTempPath(), $"ck-p1-{Guid.NewGuid():N}");

    public string DbPath { get; }
    public SqliteConnectionFactory Factory { get; }

    public TestDatabase()
    {
        System.IO.Directory.CreateDirectory(Directory);
        DbPath = Path.Combine(Directory, "knowledge.db");
        Factory = new SqliteConnectionFactory(DbPath);
    }

    public TestDatabase Migrated()
    {
        MigrationRunner.Apply(Factory, DbPath);
        return this;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
    }
}
```

`tests/CodeKnowledge.Infrastructure.Tests/MigrationRunnerTests.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Infrastructure.Database;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class MigrationRunnerTests
{
    private static long Scalar(TestDatabase db, string sql)
    {
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void Apply_creates_v1_schema_on_fresh_database()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);

        Assert.Equal(1, Scalar(db, "PRAGMA user_version;"));
        Assert.Equal(1, Scalar(db,
            "SELECT COUNT(*) FROM sqlite_master WHERE name = 'knowledge_fts';"));
        foreach (var table in new[]
        {
            "projects", "knowledge", "knowledge_versions", "facts", "fact_evidence",
            "inferences", "inference_evidence", "evidence", "relations",
        })
        {
            Assert.Equal(1, Scalar(db,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
        }
    }

    [Fact]
    public void Apply_is_idempotent()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);
        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.Equal(1, Scalar(db, "PRAGMA user_version;"));
    }

    [Fact]
    public void Apply_creates_backup_before_migrating_existing_database()
    {
        using var db = new TestDatabase();
        using (var connection = db.Factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE pre_existing (id INTEGER);";
            command.ExecuteNonQuery();
        }

        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.True(File.Exists(db.DbPath + ".bak-0"));
    }

    [Fact]
    public void Apply_skips_backup_for_fresh_database()
    {
        using var db = new TestDatabase();
        MigrationRunner.Apply(db.Factory, db.DbPath);
        Assert.False(File.Exists(db.DbPath + ".bak-0"));
    }

    [Fact]
    public void Apply_rejects_newer_schema_version()
    {
        using var db = new TestDatabase();
        using (var connection = db.Factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => MigrationRunner.Apply(db.Factory, db.DbPath));
        Assert.Equal(CodeKnowledgeException.SchemaVersionUnsupported, exception.Code);
    }

    [Fact]
    public void Open_applies_required_pragmas()
    {
        using var db = new TestDatabase().Migrated();
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string)command.ExecuteScalar()!);
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }
}
```

`tests/CodeKnowledge.Infrastructure.Tests/DatabasePathResolverTests.cs`:

```csharp
using CodeKnowledge.Infrastructure.Database;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class DatabasePathResolverTests
{
    [Fact]
    public void Resolve_prefers_environment_variable()
    {
        var expected = Path.Combine(Path.GetTempPath(), "override.db");
        var actual = DatabasePathResolver.Resolve(
            name => name == "CODEKNOWLEDGE_DB_PATH" ? expected : null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_defaults_to_base_directory()
    {
        var actual = DatabasePathResolver.Resolve(_ => null);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "knowledge.db"), actual);
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Infrastructure/Database/DatabasePathResolver.cs`:

```csharp
namespace CodeKnowledge.Infrastructure.Database;

public static class DatabasePathResolver
{
    public const string EnvironmentVariable = "CODEKNOWLEDGE_DB_PATH";

    public static string Resolve(Func<string, string?>? getEnvironmentVariable = null)
    {
        var getter = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var overridePath = getter(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, "knowledge.db")
            : overridePath;
    }
}
```

`src/CodeKnowledge.Infrastructure/Database/SqliteConnectionFactory.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Database;

public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Mode=ReadWriteCreate");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
```

`src/CodeKnowledge.Infrastructure/Database/MigrationRunner.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Database;

public static class MigrationRunner
{
    public const int CurrentVersion = 1;

    private const string V1Schema = """
        CREATE TABLE projects (
            project_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            repository_root TEXT NOT NULL,
            remote_url TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE knowledge (
            id TEXT PRIMARY KEY,
            project_id TEXT NOT NULL REFERENCES projects(project_id),
            canonical_key TEXT NOT NULL,
            title TEXT NOT NULL,
            current_version_id TEXT NULL REFERENCES knowledge_versions(id),
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE (project_id, canonical_key)
        );
        CREATE INDEX idx_knowledge_project ON knowledge(project_id);

        CREATE TABLE knowledge_versions (
            id TEXT PRIMARY KEY,
            knowledge_id TEXT NOT NULL REFERENCES knowledge(id) ON DELETE CASCADE,
            commit_hash TEXT NOT NULL,
            branch_name TEXT NULL,
            original_question TEXT NOT NULL,
            summary TEXT NOT NULL,
            confidence TEXT NOT NULL CHECK (confidence IN ('high', 'medium', 'low')),
            tags TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            created_by TEXT NOT NULL DEFAULT '',
            retain INTEGER NOT NULL DEFAULT 0,
            retain_reason TEXT NULL
        );
        CREATE INDEX idx_versions_knowledge ON knowledge_versions(knowledge_id);

        CREATE TABLE evidence (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            file_path TEXT NOT NULL,
            symbol_id TEXT NULL,
            symbol_name TEXT NOT NULL,
            symbol_kind TEXT NULL,
            signature TEXT NULL,
            start_line INTEGER NULL,
            end_line INTEGER NULL,
            commit_hash TEXT NOT NULL,
            file_hash TEXT NOT NULL,
            symbol_hash TEXT NULL,
            reason TEXT NULL
        );
        CREATE INDEX idx_evidence_version ON evidence(knowledge_version_id);

        CREATE TABLE facts (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            text TEXT NOT NULL,
            sort_order INTEGER NOT NULL
        );

        CREATE TABLE fact_evidence (
            fact_id TEXT NOT NULL REFERENCES facts(id) ON DELETE CASCADE,
            evidence_id TEXT NOT NULL REFERENCES evidence(id) ON DELETE CASCADE,
            PRIMARY KEY (fact_id, evidence_id)
        );

        CREATE TABLE inferences (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            text TEXT NOT NULL,
            confidence TEXT NOT NULL CHECK (confidence IN ('high', 'medium', 'low')),
            reason TEXT NOT NULL,
            sort_order INTEGER NOT NULL
        );

        CREATE TABLE inference_evidence (
            inference_id TEXT NOT NULL REFERENCES inferences(id) ON DELETE CASCADE,
            evidence_id TEXT NOT NULL REFERENCES evidence(id) ON DELETE CASCADE,
            PRIMARY KEY (inference_id, evidence_id)
        );

        CREATE TABLE relations (
            id TEXT PRIMARY KEY,
            knowledge_version_id TEXT NOT NULL REFERENCES knowledge_versions(id) ON DELETE CASCADE,
            from_symbol TEXT NOT NULL,
            to_symbol TEXT NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN (
                'calls', 'implements', 'inherits', 'reads', 'writes',
                'publishes', 'subscribes', 'configured-by', 'tested-by'))
        );

        CREATE VIRTUAL TABLE knowledge_fts USING fts5(
            title, original_question, summary, facts, inferences, tags,
            symbol_names, symbol_ids, file_paths, canonical_key,
            knowledge_id UNINDEXED, project_id UNINDEXED,
            tokenize = "trigram"
        );
        """;

    public static void Apply(SqliteConnectionFactory factory, string databasePath)
    {
        using var connection = factory.Open();

        var currentVersion = GetUserVersion(connection);
        if (currentVersion == CurrentVersion)
            return;
        if (currentVersion > CurrentVersion)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.SchemaVersionUnsupported,
                $"Database schema version {currentVersion} is newer than supported version {CurrentVersion}.");

        var isFreshDatabase = currentVersion == 0 && CountObjects(connection) == 0;
        if (!isFreshDatabase)
            File.Copy(databasePath, $"{databasePath}.bak-{currentVersion}", overwrite: true);

        using var transaction = connection.BeginTransaction(deferred: false);
        // 排他トランザクション開始後に再確認し、同時起動した他プロセスの適用済みを検知する
        if (GetUserVersion(connection) == CurrentVersion)
            return;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = V1Schema;
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version = {CurrentVersion};";
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long CountObjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: PASS（7テスト）。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Infrastructure tests/CodeKnowledge.Infrastructure.Tests
git commit -m "feat: add sqlite migration runner with v1 schema"
```

---

### Task 4: Git CLIアダプター

**Files:**
- Create: `src/CodeKnowledge.Core/Git/IGitRepository.cs`
- Create: `src/CodeKnowledge.Infrastructure/Git/GitCommandRunner.cs`
- Create: `src/CodeKnowledge.Infrastructure/Git/GitCliRepository.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/TestGitRepo.cs`

**Interfaces:**
- Consumes: `CodeKnowledgeException`
- Produces:

```csharp
public sealed record GitContext(
    string RepositoryRoot,          // 絶対パス
    string HeadCommit,
    string? BranchName,             // detached HEADではnull
    IReadOnlyDictionary<string, string> Remotes,  // name -> url
    string? ConfigProjectId,        // git config codeknowledge.projectId
    string? ConfigProjectName);     // git config codeknowledge.projectName

public interface IGitRepository
{
    GitContext ResolveContext(string workingDirectory);
    string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath);
}
```

- [x] **Step 1: テスト用Gitリポジトリヘルパーと失敗するテストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/TestGitRepo.cs`:

```csharp
using System.Diagnostics;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class TestGitRepo : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), $"ck-git-{Guid.NewGuid():N}");

    public TestGitRepo()
    {
        Directory.CreateDirectory(Root);
        Run("init", "--initial-branch=main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Test");
        Run("config", "commit.gpgsign", "false");
    }

    public string CommitFile(string relativePath, string content, string message = "test commit")
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        Run("add", relativePath);
        Run("commit", "-m", message);
        return Run("rev-parse", "HEAD").Trim();
    }

    public string Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    public void Dispose()
    {
        try
        {
            // .git配下の読み取り専用ファイルを削除可能にする
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch { }
    }
}
```

`tests/CodeKnowledge.Infrastructure.Tests/GitCliRepositoryTests.cs`:

```csharp
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
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Core/Git/IGitRepository.cs`:

```csharp
namespace CodeKnowledge.Core.Git;

public sealed record GitContext(
    string RepositoryRoot,
    string HeadCommit,
    string? BranchName,
    IReadOnlyDictionary<string, string> Remotes,
    string? ConfigProjectId,
    string? ConfigProjectName);

public interface IGitRepository
{
    GitContext ResolveContext(string workingDirectory);
    string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath);
}
```

`src/CodeKnowledge.Infrastructure/Git/GitCommandRunner.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Infrastructure.Git;

public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);

public static class GitCommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static GitResult Run(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=false");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new CodeKnowledgeException(
                    CodeKnowledgeException.GitNotFound, "Failed to start git.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InternalError,
                    $"git {arguments.FirstOrDefault()} timed out after {Timeout.TotalSeconds}s.");
            }
            return new GitResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Win32Exception)
        {
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitNotFound,
                "The git command was not found on PATH.");
        }
    }
}
```

`src/CodeKnowledge.Infrastructure/Git/GitCliRepository.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Infrastructure.Git;

public sealed class GitCliRepository : IGitRepository
{
    public GitContext ResolveContext(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"workingDirectory does not exist: {workingDirectory}");

        var rootResult = GitCommandRunner.Run(workingDirectory, "rev-parse", "--show-toplevel");
        if (rootResult.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "The current directory is not inside a usable Git repository.");
        var repositoryRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());

        var headResult = GitCommandRunner.Run(repositoryRoot, "rev-parse", "HEAD");
        if (headResult.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "Cannot resolve the current commit (repository may have no commits).");
        var headCommit = headResult.StandardOutput.Trim();

        var branchResult = GitCommandRunner.Run(
            repositoryRoot, "symbolic-ref", "--short", "-q", "HEAD");
        var branchName = branchResult.ExitCode == 0
            ? branchResult.StandardOutput.Trim()
            : null;

        var remotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var remoteResult = GitCommandRunner.Run(repositoryRoot, "remote", "-v");
        if (remoteResult.ExitCode == 0)
        {
            foreach (var line in remoteResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && !remotes.ContainsKey(parts[0]))
                    remotes[parts[0]] = parts[1];
            }
        }

        return new GitContext(
            repositoryRoot,
            headCommit,
            branchName,
            remotes,
            ReadConfig(repositoryRoot, "codeknowledge.projectId"),
            ReadConfig(repositoryRoot, "codeknowledge.projectName"));
    }

    public string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath)
    {
        var result = GitCommandRunner.Run(repositoryRoot, "show", $"{commitHash}:{repoRelativePath}");
        if (result.ExitCode != 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Cannot read '{repoRelativePath}' at commit {commitHash}.");
        return result.StandardOutput;
    }

    private static string? ReadConfig(string repositoryRoot, string key)
    {
        var result = GitCommandRunner.Run(repositoryRoot, "config", "--get", key);
        return result.ExitCode == 0 && result.StandardOutput.Trim().Length > 0
            ? result.StandardOutput.Trim()
            : null;
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: PASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core/Git src/CodeKnowledge.Infrastructure/Git tests/CodeKnowledge.Infrastructure.Tests
git commit -m "feat: add git cli adapter resolving repository context"
```

---

### Task 5: プロジェクトID解決（純粋ロジック）

**Files:**
- Create: `src/CodeKnowledge.Core/Projects/RemoteUrlNormalizer.cs`
- Create: `src/CodeKnowledge.Core/Projects/ProjectIdResolver.cs`
- Test: `tests/CodeKnowledge.Core.Tests/RemoteUrlNormalizerTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ProjectIdResolverTests.cs`

**Interfaces:**
- Consumes: `GitContext`、`CodeKnowledgeException`
- Produces: `RemoteUrlNormalizer.Normalize(string url) -> string`、`ProjectIdResolver.Resolve(GitContext) -> ProjectIdentity(string ProjectId, string Source, string? NormalizedRemoteUrl, string DisplayName)`（Sourceは`config` / `remote` / `local`）

- [x] **Step 1: 失敗するテストを書く**

`tests/CodeKnowledge.Core.Tests/RemoteUrlNormalizerTests.cs`:

```csharp
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Tests;

public sealed class RemoteUrlNormalizerTests
{
    // 要件5.3.2の正規化例とAC-15、AC-16
    [Theory]
    [InlineData("git@github.com:Company/Order-API.git", "github.com/company/order-api")]
    [InlineData("https://github.com/company/order-api", "github.com/company/order-api")]
    [InlineData("https://user:token@github.com/Company/order-api.git", "github.com/company/order-api")]
    [InlineData("ssh://git@github.com/company/order-api", "github.com/company/order-api")]
    [InlineData("http://git.example.local:8443/Team/Order-System/", "git.example.local:8443/team/order-system")]
    [InlineData("git://github.com/a/b.git", "github.com/a/b")]
    [InlineData("https://github.com/a\\b", "github.com/a/b")]
    public void Normalize_applies_all_rules(string input, string expected)
    {
        Assert.Equal(expected, RemoteUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_never_retains_credentials()
    {
        var normalized = RemoteUrlNormalizer.Normalize("https://user:secret@host.example/a/b.git");
        Assert.DoesNotContain("user", normalized);
        Assert.DoesNotContain("secret", normalized);
    }
}
```

`tests/CodeKnowledge.Core.Tests/ProjectIdResolverTests.cs`:

```csharp
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
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Core/Projects/RemoteUrlNormalizer.cs`:

```csharp
namespace CodeKnowledge.Core.Projects;

public static class RemoteUrlNormalizer
{
    /// <summary>要件5.3.2の8ルールを順に適用する。</summary>
    public static string Normalize(string url)
    {
        var value = url.Trim().Replace('\\', '/'); // ルール8: パス区切り統一

        // ルール2: スキーム除去
        foreach (var scheme in new[] { "https://", "http://", "ssh://", "git://" })
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value[scheme.Length..];
                break;
            }
        }

        // ルール1: scp形式（git@host:path）をhost/pathへ変換
        // 認証情報除去より先に判定する。「@より後ろで最初の:が/より前」ならscp形式
        var atIndex = value.IndexOf('@');
        var afterAt = atIndex >= 0 ? value[(atIndex + 1)..] : value;
        var colonIndex = afterAt.IndexOf(':');
        var slashIndex = afterAt.IndexOf('/');
        var isScpStyle = colonIndex > 0 &&
            (slashIndex < 0 || colonIndex < slashIndex) &&
            !IsPortNumber(afterAt, colonIndex);
        if (isScpStyle)
            afterAt = afterAt[..colonIndex] + "/" + afterAt[(colonIndex + 1)..];

        // ルール3: 認証情報除去（@より前を捨てる）
        value = afterAt;

        // ルール5・6: .gitサフィックスと末尾スラッシュの除去
        value = value.TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        value = value.TrimEnd('/');

        // ルール7: 小文字化（ルール4: ポートは保持される）
        return value.ToLowerInvariant();
    }

    private static bool IsPortNumber(string value, int colonIndex)
    {
        var rest = value[(colonIndex + 1)..];
        var end = rest.IndexOf('/');
        var candidate = end < 0 ? rest : rest[..end];
        return candidate.Length > 0 && candidate.All(char.IsAsciiDigit);
    }
}
```

`src/CodeKnowledge.Core/Projects/ProjectIdResolver.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Projects;

public sealed record ProjectIdentity(
    string ProjectId,
    string Source,
    string? NormalizedRemoteUrl,
    string DisplayName);

public static class ProjectIdResolver
{
    public static ProjectIdentity Resolve(GitContext context)
    {
        var normalizedRemote = SelectRemote(context.Remotes) is { } remoteUrl
            ? RemoteUrlNormalizer.Normalize(remoteUrl)
            : null;
        var displayName = ResolveDisplayName(context, normalizedRemote);

        if (context.ConfigProjectId is not null)
        {
            var configured = context.ConfigProjectId.Trim();
            if (configured.Length == 0 || configured.Contains('@') ||
                configured != RemoteUrlNormalizer.Normalize(configured))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    "codeknowledge.projectId must be a normalized project id without credentials.");
            return new ProjectIdentity(configured, "config", normalizedRemote, displayName);
        }

        if (normalizedRemote is not null)
            return new ProjectIdentity(normalizedRemote, "remote", normalizedRemote, displayName);

        var normalizedRoot = context.RepositoryRoot.Replace('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..16];
        return new ProjectIdentity($"local:{hash}", "local", null, displayName);
    }

    private static string? SelectRemote(IReadOnlyDictionary<string, string> remotes)
    {
        if (remotes.Count == 0)
            return null;
        if (remotes.TryGetValue("origin", out var origin))
            return origin;
        if (remotes.TryGetValue("upstream", out var upstream))
            return upstream;
        return remotes.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
    }

    private static string ResolveDisplayName(GitContext context, string? normalizedRemote)
    {
        if (!string.IsNullOrWhiteSpace(context.ConfigProjectName))
            return context.ConfigProjectName;
        if (normalizedRemote is not null)
            return normalizedRemote[(normalizedRemote.LastIndexOf('/') + 1)..];
        return Path.GetFileName(
            context.RepositoryRoot.TrimEnd(Path.DirectorySeparatorChar, '/'));
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core/Projects tests/CodeKnowledge.Core.Tests
git commit -m "feat: add project id resolution with remote url normalization"
```

---

### Task 6: プロジェクト解決ユースケースとProjectStore

**Files:**
- Create: `src/CodeKnowledge.Core/Projects/IProjectStore.cs`
- Create: `src/CodeKnowledge.Core/Projects/ResolveProjectUseCase.cs`
- Create: `src/CodeKnowledge.Infrastructure/Stores/SqliteProjectStore.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ResolveProjectUseCaseTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/Fakes/FakeProjectStore.cs`
- Test: `tests/CodeKnowledge.Core.Tests/Fakes/FakeGitRepository.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/SqliteProjectStoreTests.cs`

**Interfaces:**
- Consumes: `ProjectIdResolver`、`IGitRepository`、`Project`
- Produces:

```csharp
public interface IProjectStore
{
    Project? FindById(string projectId);
    Project? FindByRepositoryRoot(string repositoryRoot);
    void Upsert(Project project);
    int CountKnowledge(string projectId);
}

public sealed record ProjectWarning(string Code, string? PreviousProjectId, int KnowledgeCount);

public sealed record ProjectResolution(
    string ProjectId,
    string ProjectIdSource,
    string DisplayName,
    string RepositoryRoot,
    string? RemoteUrl,
    string CurrentCommit,
    string? BranchName,
    IReadOnlyList<ProjectWarning> Warnings);

public sealed class ResolveProjectUseCase(IGitRepository git, IProjectStore store)
{
    public ProjectResolution Execute(string workingDirectory);
}
```

- [x] **Step 1: フェイクと失敗するテストを書く**

`tests/CodeKnowledge.Core.Tests/Fakes/FakeGitRepository.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeGitRepository : IGitRepository
{
    public GitContext? Context { get; set; }
    public Dictionary<string, string> FilesAtCommit { get; } = new(StringComparer.Ordinal);

    public GitContext ResolveContext(string workingDirectory)
        => Context ?? throw new CodeKnowledgeException(
            CodeKnowledgeException.GitRepositoryRequired,
            "The current directory is not inside a usable Git repository.");

    public string ReadFileAtCommit(string repositoryRoot, string commitHash, string repoRelativePath)
        => FilesAtCommit.TryGetValue(repoRelativePath, out var content)
            ? content
            : throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Cannot read '{repoRelativePath}' at commit {commitHash}.");
}
```

`tests/CodeKnowledge.Core.Tests/Fakes/FakeProjectStore.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeProjectStore : IProjectStore
{
    public Dictionary<string, Project> Projects { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> KnowledgeCounts { get; } = new(StringComparer.Ordinal);

    public Project? FindById(string projectId)
        => Projects.GetValueOrDefault(projectId);

    public Project? FindByRepositoryRoot(string repositoryRoot)
        => Projects.Values.FirstOrDefault(p =>
            string.Equals(p.RepositoryRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase));

    public void Upsert(Project project) => Projects[project.ProjectId] = project;

    public int CountKnowledge(string projectId) => KnowledgeCounts.GetValueOrDefault(projectId);
}
```

`tests/CodeKnowledge.Core.Tests/ResolveProjectUseCaseTests.cs`:

```csharp
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
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: Core実装を書く**

`src/CodeKnowledge.Core/Projects/IProjectStore.cs`:

```csharp
using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Projects;

public interface IProjectStore
{
    Project? FindById(string projectId);
    Project? FindByRepositoryRoot(string repositoryRoot);
    void Upsert(Project project);
    int CountKnowledge(string projectId);
}
```

`src/CodeKnowledge.Core/Projects/ResolveProjectUseCase.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Projects;

public sealed record ProjectWarning(string Code, string? PreviousProjectId, int KnowledgeCount);

public sealed record ProjectResolution(
    string ProjectId,
    string ProjectIdSource,
    string DisplayName,
    string RepositoryRoot,
    string? RemoteUrl,
    string CurrentCommit,
    string? BranchName,
    IReadOnlyList<ProjectWarning> Warnings);

public sealed class ResolveProjectUseCase(IGitRepository git, IProjectStore store)
{
    public ProjectResolution Execute(string workingDirectory)
    {
        var context = git.ResolveContext(workingDirectory);
        var identity = ProjectIdResolver.Resolve(context);

        var warnings = new List<ProjectWarning>();
        var byRoot = store.FindByRepositoryRoot(context.RepositoryRoot);
        if (byRoot is not null && byRoot.ProjectId != identity.ProjectId)
        {
            warnings.Add(new ProjectWarning(
                "project_id_changed", byRoot.ProjectId, store.CountKnowledge(byRoot.ProjectId)));
        }

        var now = DateTimeOffset.UtcNow;
        var existing = store.FindById(identity.ProjectId);
        store.Upsert(new Project(
            identity.ProjectId,
            identity.DisplayName,
            context.RepositoryRoot,
            identity.NormalizedRemoteUrl,
            existing?.CreatedAt ?? now,
            now));

        return new ProjectResolution(
            identity.ProjectId,
            identity.Source,
            identity.DisplayName,
            context.RepositoryRoot,
            identity.NormalizedRemoteUrl,
            context.HeadCommit,
            context.BranchName,
            warnings);
    }
}
```

- [x] **Step 4: Coreテストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: SqliteProjectStoreの失敗する統合テストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/SqliteProjectStoreTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteProjectStoreTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteProjectStore Store => new(_db.Factory);

    private static Project Sample(string id = "github.com/company/order-api",
        string root = @"C:\work\order-api")
        => new(id, "order-api", root, id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public void Upsert_then_FindById_roundtrips()
    {
        Store.Upsert(Sample());
        var found = Store.FindById("github.com/company/order-api");
        Assert.NotNull(found);
        Assert.Equal(@"C:\work\order-api", found.RepositoryRoot);
    }

    [Fact]
    public void Upsert_overwrites_existing_row()
    {
        Store.Upsert(Sample());
        Store.Upsert(Sample() with { RepositoryRoot = @"C:\new\clone" });
        Assert.Equal(@"C:\new\clone",
            Store.FindById("github.com/company/order-api")!.RepositoryRoot);
    }

    [Fact]
    public void FindByRepositoryRoot_matches_case_insensitively_on_windows_paths()
    {
        Store.Upsert(Sample());
        Assert.NotNull(Store.FindByRepositoryRoot(@"c:\WORK\order-api"));
    }

    [Fact]
    public void CountKnowledge_returns_zero_without_rows()
    {
        Store.Upsert(Sample());
        Assert.Equal(0, Store.CountKnowledge("github.com/company/order-api"));
    }

    public void Dispose() => _db.Dispose();
}
```

- [x] **Step 6: SqliteProjectStoreを実装する**

`src/CodeKnowledge.Infrastructure/Stores/SqliteProjectStore.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Stores;

public sealed class SqliteProjectStore(SqliteConnectionFactory factory) : IProjectStore
{
    public Project? FindById(string projectId)
        => Query("SELECT * FROM projects WHERE project_id = @key;", projectId);

    public Project? FindByRepositoryRoot(string repositoryRoot)
        => Query(
            "SELECT * FROM projects WHERE repository_root = @key COLLATE NOCASE;",
            repositoryRoot);

    public void Upsert(Project project)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (project_id, display_name, repository_root, remote_url, created_at, updated_at)
            VALUES (@id, @name, @root, @remote, @created, @updated)
            ON CONFLICT (project_id) DO UPDATE SET
                display_name = excluded.display_name,
                repository_root = excluded.repository_root,
                remote_url = excluded.remote_url,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("@id", project.ProjectId);
        command.Parameters.AddWithValue("@name", project.DisplayName);
        command.Parameters.AddWithValue("@root", project.RepositoryRoot);
        command.Parameters.AddWithValue("@remote", (object?)project.RemoteUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@created", project.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@updated", project.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public int CountKnowledge(string projectId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM knowledge WHERE project_id = @id;";
        command.Parameters.AddWithValue("@id", projectId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private Project? Query(string sql, string key)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@key", key);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new Project(
            reader.GetString(reader.GetOrdinal("project_id")),
            reader.GetString(reader.GetOrdinal("display_name")),
            reader.GetString(reader.GetOrdinal("repository_root")),
            reader.IsDBNull(reader.GetOrdinal("remote_url"))
                ? null : reader.GetString(reader.GetOrdinal("remote_url")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))));
    }
}
```

- [x] **Step 7: 統合テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: PASS。

- [x] **Step 8: コミット**

```bash
git add src/CodeKnowledge.Core/Projects src/CodeKnowledge.Infrastructure/Stores tests/
git commit -m "feat: add resolve project use case with project store"
```

---

### Task 7: 検索キーワード処理（純粋ロジック）

**Files:**
- Create: `src/CodeKnowledge.Core/Search/KeywordPreparation.cs`
- Test: `tests/CodeKnowledge.Core.Tests/KeywordPreparationTests.cs`

**Interfaces:**
- Consumes: `CodeKnowledgeException`
- Produces:

```csharp
public sealed record PreparedKeywords(
    IReadOnlyList<string> FtsKeywords,   // 正規化済み・3文字以上（生の語）
    IReadOnlyList<string> LikeKeywords,  // 正規化済み・1〜2文字（生の語）
    string? FtsMatchExpression);         // "kw1" OR "kw2" 形式。FTS対象語ゼロならnull

public static class KeywordPreparation
{
    public static PreparedKeywords Prepare(IReadOnlyList<string> keywords);
    public static string EscapeLikePattern(string keyword); // \, %, _ をエスケープし %...% で包む
}
```

- [x] **Step 1: 失敗するテストを書く**

`tests/CodeKnowledge.Core.Tests/KeywordPreparationTests.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Search;

namespace CodeKnowledge.Core.Tests;

public sealed class KeywordPreparationTests
{
    [Fact]
    public void Prepare_routes_by_unicode_code_point_count() // 要件8.3
    {
        var prepared = KeywordPreparation.Prepare(["メール", "仕様", "M", "OrderCompleted"]);
        Assert.Equal(["メール", "OrderCompleted"], prepared.FtsKeywords);
        Assert.Equal(["仕様", "M"], prepared.LikeKeywords);
    }

    [Fact]
    public void Prepare_quotes_fts_keywords_and_joins_with_or() // AC-21・AC-22
    {
        var prepared = KeywordPreparation.Prepare(["sui-memory", "メール"]);
        Assert.Equal("\"sui-memory\" OR \"メール\"", prepared.FtsMatchExpression);
    }

    [Fact]
    public void Prepare_escapes_double_quotes_inside_keywords()
    {
        var prepared = KeywordPreparation.Prepare(["a\"b\"c"]);
        Assert.Equal("\"a\"\"b\"\"c\"", prepared.FtsMatchExpression);
    }

    [Fact]
    public void Prepare_applies_nfkc_normalization() // 要件8.4: 全角半角の揺れ吸収
    {
        var prepared = KeywordPreparation.Prepare(["ＡＢＣ"]); // 全角英字
        Assert.Equal(["ABC"], prepared.FtsKeywords);
    }

    [Fact]
    public void Prepare_discards_empty_keywords_and_rejects_all_invalid()
    {
        var prepared = KeywordPreparation.Prepare(["メール", "  "]);
        Assert.Single(prepared.FtsKeywords);

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => KeywordPreparation.Prepare(["", "   "]));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Prepare_returns_null_match_expression_without_fts_keywords()
    {
        Assert.Null(KeywordPreparation.Prepare(["仕様"]).FtsMatchExpression);
    }

    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData(@"a\b", @"%a\\b%")]
    public void EscapeLikePattern_escapes_metacharacters(string keyword, string expected) // AC-21
    {
        Assert.Equal(expected, KeywordPreparation.EscapeLikePattern(keyword));
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Core/Search/KeywordPreparation.cs`:

```csharp
using System.Text;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Core.Search;

public sealed record PreparedKeywords(
    IReadOnlyList<string> FtsKeywords,
    IReadOnlyList<string> LikeKeywords,
    string? FtsMatchExpression);

public static class KeywordPreparation
{
    public static PreparedKeywords Prepare(IReadOnlyList<string> keywords)
    {
        var normalized = keywords
            .Select(keyword => keyword.Normalize(NormalizationForm.FormKC).Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count == 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "No valid keywords remained after normalization.");

        var fts = new List<string>();
        var like = new List<string>();
        foreach (var keyword in normalized)
        {
            // 要件8.3はUnicodeコードポイント数で判定する
            var codePoints = keyword.EnumerateRunes().Count();
            (codePoints >= 3 ? fts : like).Add(keyword);
        }

        var matchExpression = fts.Count == 0
            ? null
            : string.Join(" OR ", fts.Select(k => $"\"{k.Replace("\"", "\"\"")}\""));
        return new PreparedKeywords(fts, like, matchExpression);
    }

    public static string EscapeLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        return $"%{escaped}%";
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core/Search tests/CodeKnowledge.Core.Tests
git commit -m "feat: add keyword normalization and query sanitization"
```

---

### Task 8: ハッシュ計算（純粋ロジック）

**Files:**
- Create: `src/CodeKnowledge.Core/Hashing/ContentHasher.cs`
- Test: `tests/CodeKnowledge.Core.Tests/ContentHasherTests.cs`

**Interfaces:**
- Consumes: なし
- Produces: `ContentHasher.ComputeFileHash(string content) -> string`（SHA-256 hex）、`ContentHasher.ComputeSymbolHash(string fileContent, int startLine, int endLine) -> string`（行範囲抽出 + 空白正規化 + SHA-256。要件9.4段階2）

- [x] **Step 1: 失敗するテストを書く**

`tests/CodeKnowledge.Core.Tests/ContentHasherTests.cs`:

```csharp
using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Tests;

public sealed class ContentHasherTests
{
    [Fact]
    public void ComputeFileHash_is_deterministic_and_content_sensitive()
    {
        Assert.Equal(ContentHasher.ComputeFileHash("abc"), ContentHasher.ComputeFileHash("abc"));
        Assert.NotEqual(ContentHasher.ComputeFileHash("abc"), ContentHasher.ComputeFileHash("abd"));
    }

    [Fact]
    public void ComputeSymbolHash_extracts_line_range()
    {
        const string content = "line1\nline2\nline3\nline4\n";
        var hash23 = ContentHasher.ComputeSymbolHash(content, 2, 3);
        var hash22 = ContentHasher.ComputeSymbolHash(content, 2, 2);
        Assert.NotEqual(hash23, hash22);
    }

    [Fact]
    public void ComputeSymbolHash_ignores_whitespace_noise() // 要件9.4段階2の正規化
    {
        const string original = "void  Foo()\n{\n    Bar();   \n}\n";
        const string reformatted = "void Foo()\r\n{\r\n  Bar();\r\n}\r\n";
        Assert.Equal(
            ContentHasher.ComputeSymbolHash(original, 1, 4),
            ContentHasher.ComputeSymbolHash(reformatted, 1, 4));
    }

    [Fact]
    public void ComputeSymbolHash_detects_code_change()
    {
        const string before = "void Foo()\n{\n    Bar();\n}\n";
        const string after = "void Foo()\n{\n    Baz();\n}\n";
        Assert.NotEqual(
            ContentHasher.ComputeSymbolHash(before, 1, 4),
            ContentHasher.ComputeSymbolHash(after, 1, 4));
    }

    [Fact]
    public void ComputeSymbolHash_clamps_out_of_range_lines()
    {
        const string content = "line1\nline2\n";
        var hash = ContentHasher.ComputeSymbolHash(content, 1, 999);
        Assert.Equal(ContentHasher.ComputeSymbolHash(content, 1, 2), hash);
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: 最小実装を書く**

`src/CodeKnowledge.Core/Hashing/ContentHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeKnowledge.Core.Hashing;

public static partial class ContentHasher
{
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex ConsecutiveSpaces();

    public static string ComputeFileHash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// 要件9.4段階2: 改行コード統一、行末空白除去、連続空白の縮約を行った
    /// startLine〜endLine（1始まり・両端含む）のテキストをハッシュする。
    /// </summary>
    public static string ComputeSymbolHash(string fileContent, int startLine, int endLine)
    {
        var lines = fileContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var from = Math.Max(1, startLine);
        var to = Math.Min(lines.Length, endLine);
        var normalized = new StringBuilder();
        for (var lineNumber = from; lineNumber <= to; lineNumber++)
        {
            var line = ConsecutiveSpaces().Replace(lines[lineNumber - 1], " ").TrimEnd();
            normalized.Append(line).Append('\n');
        }
        return ComputeFileHash(normalized.ToString());
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

注意: `ComputeSymbolHash_ignores_whitespace_noise`はインデント幅の違い（4スペース vs 2スペース）を同一視できるかを検証する。連続空白の縮約（`[ \t]+` → 単一スペース）で先頭インデントも縮約されるため一致する。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core/Hashing tests/CodeKnowledge.Core.Tests
git commit -m "feat: add file and symbol hash computation with whitespace normalization"
```

---

### Task 9: ナレッジ保存（ユースケース + Store）

**Files:**
- Create: `src/CodeKnowledge.Core/Knowledge/IKnowledgeStore.cs`
- Create: `src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs`
- Create: `src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs`
- Test: `tests/CodeKnowledge.Core.Tests/SaveKnowledgeUseCaseTests.cs`
- Test: `tests/CodeKnowledge.Core.Tests/Fakes/FakeKnowledgeStore.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreSaveTests.cs`

**Interfaces:**
- Consumes: `ResolveProjectUseCase`、`IGitRepository`、`ContentHasher`、`ConfidenceParser`、`RelationKind`、ドメインrecord群
- Produces:

```csharp
// IKnowledgeStore.cs
public sealed record VersionToSave(
    string ProjectId, string CanonicalKey, string Title,
    string CommitHash, string? BranchName,
    string OriginalQuestion, string Summary, Confidence Confidence,
    string Tags, string CreatedBy,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<FactRecord> Facts,
    IReadOnlyList<InferenceRecord> Inferences,
    IReadOnlyList<RelationRecord> Relations);

public sealed record SaveVersionResult(string KnowledgeId, string VersionId, bool CreatedNewKnowledge);
public sealed record FtsSearchHit(KnowledgeSummary Summary, double Score, string SearchText);
public sealed record LikeSearchHit(KnowledgeSummary Summary, string SearchText);

public interface IKnowledgeStore
{
    SaveVersionResult SaveVersion(VersionToSave version);
    IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId);
    KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId);
    IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit);
    IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns);
}

// SaveKnowledgeUseCase.cs（入力・出力）
public sealed record SaveEvidenceInput(
    string FilePath, string? SymbolId, string SymbolName, string? SymbolKind,
    string? Signature, int? StartLine, int? EndLine, string? Reason);
public sealed record SaveFactInput(string Text, IReadOnlyList<int> EvidenceIndexes);
public sealed record SaveInferenceInput(string Text, string Confidence, string Reason, IReadOnlyList<int> EvidenceIndexes);
public sealed record SaveRelationInput(string FromSymbol, string ToSymbol, string Kind);
public sealed record SaveKnowledgeRequest(
    string WorkingDirectory, string CanonicalKey, string Title,
    string OriginalQuestion, string Summary, string Confidence,
    string? Tags, string? CreatedBy, string? CommitHash,
    IReadOnlyList<SaveEvidenceInput> Evidence,
    IReadOnlyList<SaveFactInput> Facts,
    IReadOnlyList<SaveInferenceInput> Inferences,
    IReadOnlyList<SaveRelationInput> Relations);
public sealed record SaveKnowledgeResult(
    string ProjectId, string KnowledgeId, string VersionId,
    bool CreatedNewKnowledge, IReadOnlyList<KnowledgeSummary> SimilarKnowledge);
```

- [x] **Step 1: フェイクと失敗するCoreテストを書く**

`tests/CodeKnowledge.Core.Tests/Fakes/FakeKnowledgeStore.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeKnowledgeStore : IKnowledgeStore
{
    public List<VersionToSave> SavedVersions { get; } = [];
    public List<KnowledgeSummary> Summaries { get; } = [];
    public KnowledgeDetail? Detail { get; set; }

    public SaveVersionResult SaveVersion(VersionToSave version)
    {
        SavedVersions.Add(version);
        return new SaveVersionResult("knowledge-1", "version-1", CreatedNewKnowledge: true);
    }

    public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId) => Summaries;

    public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
        => Detail;

    public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
        => [];

    public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
        => [];
}
```

`tests/CodeKnowledge.Core.Tests/SaveKnowledgeUseCaseTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Tests.Fakes;

namespace CodeKnowledge.Core.Tests;

public sealed class SaveKnowledgeUseCaseTests
{
    private readonly FakeGitRepository _git = new();
    private readonly FakeProjectStore _projectStore = new();
    private readonly FakeKnowledgeStore _knowledgeStore = new();

    private SaveKnowledgeUseCase UseCase => new(
        new ResolveProjectUseCase(_git, _projectStore), _git, _knowledgeStore);

    public SaveKnowledgeUseCaseTests()
    {
        _git.Context = new GitContext(
            @"C:\work\order-api", "abc123", "main",
            new Dictionary<string, string> { ["origin"] = "https://github.com/company/order-api.git" },
            null, null);
        _git.FilesAtCommit["src/OrderService.cs"] = "class OrderService\n{\n    void Complete() { }\n}\n";
    }

    private static SaveKnowledgeRequest Request(
        IReadOnlyList<SaveFactInput>? facts = null,
        string confidence = "high",
        IReadOnlyList<SaveRelationInput>? relations = null,
        string filePath = @"C:\work\order-api\src\OrderService.cs")
        => new(
            WorkingDirectory: @"C:\work\order-api",
            CanonicalKey: "domain.mail.order-completed",
            Title: "注文完了メール仕様",
            OriginalQuestion: "注文完了メールの処理は？",
            Summary: "OrderServiceがメールを送る",
            Confidence: confidence,
            Tags: "mail order",
            CreatedBy: "test-agent",
            CommitHash: null,
            Evidence: [new SaveEvidenceInput(filePath, null, "OrderService", "class", null, 1, 4, null)],
            Facts: facts ?? [new SaveFactInput("OrderServiceがメール送信を行う", [0])],
            Inferences: [],
            Relations: relations ?? []);

    [Fact]
    public void Execute_saves_and_returns_result()
    {
        var result = UseCase.Execute(Request());

        Assert.Equal("github.com/company/order-api", result.ProjectId);
        Assert.True(result.CreatedNewKnowledge);
        var saved = Assert.Single(_knowledgeStore.SavedVersions);
        Assert.Equal("abc123", saved.CommitHash);
        Assert.Equal(Confidence.High, saved.Confidence);
    }

    [Fact]
    public void Execute_rejects_fact_without_evidence() // AC-09
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(facts: [new SaveFactInput("根拠なしの事実", [])])));
        Assert.Equal(CodeKnowledgeException.FactRequiresEvidence, exception.Code);
        Assert.Empty(_knowledgeStore.SavedVersions);
    }

    [Theory]
    [InlineData("0.9")]
    [InlineData("certain")]
    [InlineData("")]
    public void Execute_rejects_undefined_confidence(string confidence) // AC-28
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(confidence: confidence)));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
        Assert.Empty(_knowledgeStore.SavedVersions);
    }

    [Fact]
    public void Execute_rejects_unknown_relation_kind()
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(relations: [new SaveRelationInput("A", "B", "depends-on")])));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Execute_rejects_out_of_range_evidence_index()
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(facts: [new SaveFactInput("事実", [5])])));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Execute_normalizes_evidence_file_path() // 要件6.3
    {
        UseCase.Execute(Request(filePath: @"C:\work\order-api\src\OrderService.cs"));
        var evidence = Assert.Single(_knowledgeStore.SavedVersions[0].Evidence);
        Assert.Equal("src/OrderService.cs", evidence.FilePath);
    }

    [Fact]
    public void Execute_computes_hashes_from_commit_content()
    {
        UseCase.Execute(Request());
        var evidence = Assert.Single(_knowledgeStore.SavedVersions[0].Evidence);
        Assert.Equal(
            ContentHasher.ComputeFileHash(_git.FilesAtCommit["src/OrderService.cs"]),
            evidence.FileHash);
        Assert.Equal(
            ContentHasher.ComputeSymbolHash(_git.FilesAtCommit["src/OrderService.cs"], 1, 4),
            evidence.SymbolHash);
    }

    [Fact]
    public void Execute_returns_similar_knowledge_warning() // 要件6.1
    {
        _knowledgeStore.Summaries.Add(new KnowledgeSummary(
            "knowledge-9", "domain.mail.order-completed.v2", "注文完了メールの仕様まとめ",
            "...", "abc", Confidence.High, DateTimeOffset.UtcNow));

        var result = UseCase.Execute(Request());

        Assert.Single(result.SimilarKnowledge);
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: Core実装を書く**

`src/CodeKnowledge.Core/Knowledge/IKnowledgeStore.cs`:

```csharp
using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Knowledge;

public sealed record VersionToSave(
    string ProjectId,
    string CanonicalKey,
    string Title,
    string CommitHash,
    string? BranchName,
    string OriginalQuestion,
    string Summary,
    Confidence Confidence,
    string Tags,
    string CreatedBy,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<FactRecord> Facts,
    IReadOnlyList<InferenceRecord> Inferences,
    IReadOnlyList<RelationRecord> Relations);

public sealed record SaveVersionResult(string KnowledgeId, string VersionId, bool CreatedNewKnowledge);

public sealed record FtsSearchHit(KnowledgeSummary Summary, double Score, string SearchText);

public sealed record LikeSearchHit(KnowledgeSummary Summary, string SearchText);

public interface IKnowledgeStore
{
    SaveVersionResult SaveVersion(VersionToSave version);
    IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId);
    KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId);
    IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit);
    IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns);
}
```

`src/CodeKnowledge.Core/Knowledge/SaveKnowledgeUseCase.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Knowledge;

public sealed record SaveEvidenceInput(
    string FilePath, string? SymbolId, string SymbolName, string? SymbolKind,
    string? Signature, int? StartLine, int? EndLine, string? Reason);

public sealed record SaveFactInput(string Text, IReadOnlyList<int> EvidenceIndexes);

public sealed record SaveInferenceInput(
    string Text, string Confidence, string Reason, IReadOnlyList<int> EvidenceIndexes);

public sealed record SaveRelationInput(string FromSymbol, string ToSymbol, string Kind);

public sealed record SaveKnowledgeRequest(
    string WorkingDirectory, string CanonicalKey, string Title,
    string OriginalQuestion, string Summary, string Confidence,
    string? Tags, string? CreatedBy, string? CommitHash,
    IReadOnlyList<SaveEvidenceInput> Evidence,
    IReadOnlyList<SaveFactInput> Facts,
    IReadOnlyList<SaveInferenceInput> Inferences,
    IReadOnlyList<SaveRelationInput> Relations);

public sealed record SaveKnowledgeResult(
    string ProjectId, string KnowledgeId, string VersionId,
    bool CreatedNewKnowledge, IReadOnlyList<KnowledgeSummary> SimilarKnowledge);

public sealed class SaveKnowledgeUseCase(
    ResolveProjectUseCase resolveProject,
    IGitRepository git,
    IKnowledgeStore store)
{
    public SaveKnowledgeResult Execute(SaveKnowledgeRequest request)
    {
        var resolution = resolveProject.Execute(request.WorkingDirectory);
        var commitHash = string.IsNullOrWhiteSpace(request.CommitHash)
            ? resolution.CurrentCommit
            : request.CommitHash;

        Validate(request);

        var evidence = BuildEvidence(request, resolution.RepositoryRoot, commitHash);
        var facts = request.Facts
            .Select(fact => new FactRecord(
                NewId(), fact.Text,
                fact.EvidenceIndexes.Select(index => evidence[index].Id).ToList()))
            .ToList();
        var inferences = request.Inferences
            .Select(inference =>
            {
                ConfidenceParser.TryParse(inference.Confidence, out var confidence);
                return new InferenceRecord(
                    NewId(), inference.Text, confidence, inference.Reason,
                    inference.EvidenceIndexes.Select(index => evidence[index].Id).ToList());
            })
            .ToList();
        var relations = request.Relations
            .Select(relation => new RelationRecord(
                NewId(), relation.FromSymbol, relation.ToSymbol, relation.Kind))
            .ToList();

        ConfidenceParser.TryParse(request.Confidence, out var overallConfidence);
        var saved = store.SaveVersion(new VersionToSave(
            resolution.ProjectId,
            request.CanonicalKey.Trim(),
            request.Title,
            commitHash,
            resolution.BranchName,
            request.OriginalQuestion,
            request.Summary,
            overallConfidence,
            request.Tags?.Trim() ?? "",
            request.CreatedBy?.Trim() ?? "",
            evidence, facts, inferences, relations));

        var similar = FindSimilar(resolution.ProjectId, saved.KnowledgeId,
            request.CanonicalKey.Trim(), request.Title);
        return new SaveKnowledgeResult(
            resolution.ProjectId, saved.KnowledgeId, saved.VersionId,
            saved.CreatedNewKnowledge, similar);
    }

    private void Validate(SaveKnowledgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CanonicalKey) ||
            string.IsNullOrWhiteSpace(request.Title))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "canonicalKey and title are required.");

        if (!ConfidenceParser.TryParse(request.Confidence, out _))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "confidence must be one of: high, medium, low.");

        foreach (var inference in request.Inferences)
        {
            if (!ConfidenceParser.TryParse(inference.Confidence, out _))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    "inference confidence must be one of: high, medium, low.");
        }

        foreach (var relation in request.Relations)
        {
            if (!RelationKind.All.Contains(relation.Kind))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    $"Unknown relation kind: {relation.Kind}");
        }

        foreach (var fact in request.Facts)
        {
            if (fact.EvidenceIndexes.Count == 0)
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.FactRequiresEvidence,
                    $"Fact '{fact.Text}' does not reference any evidence.");
        }

        var allIndexes = request.Facts.SelectMany(fact => fact.EvidenceIndexes)
            .Concat(request.Inferences.SelectMany(inference => inference.EvidenceIndexes));
        foreach (var index in allIndexes)
        {
            if (index < 0 || index >= request.Evidence.Count)
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    $"Evidence index {index} is out of range.");
        }
    }

    private List<EvidenceRecord> BuildEvidence(
        SaveKnowledgeRequest request, string repositoryRoot, string commitHash)
    {
        var records = new List<EvidenceRecord>();
        foreach (var input in request.Evidence)
        {
            var relativePath = NormalizeFilePath(input.FilePath, repositoryRoot);
            var content = git.ReadFileAtCommit(repositoryRoot, commitHash, relativePath);
            var symbolHash = input.StartLine is { } start && input.EndLine is { } end
                ? ContentHasher.ComputeSymbolHash(content, start, end)
                : null;
            records.Add(new EvidenceRecord(
                NewId(), relativePath, input.SymbolId, input.SymbolName, input.SymbolKind,
                input.Signature, input.StartLine, input.EndLine, commitHash,
                ContentHasher.ComputeFileHash(content), symbolHash, input.Reason));
        }
        return records;
    }

    private IReadOnlyList<KnowledgeSummary> FindSimilar(
        string projectId, string savedKnowledgeId, string canonicalKey, string title)
        => store.ListSummaries(projectId)
            .Where(summary => summary.KnowledgeId != savedKnowledgeId)
            .Where(summary =>
                MutuallyContains(summary.CanonicalKey, canonicalKey) ||
                MutuallyContains(summary.Title, title))
            .ToList();

    private static bool MutuallyContains(string left, string right)
        => left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
           right.Contains(left, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeFilePath(string filePath, string repositoryRoot)
    {
        var normalized = filePath.Replace('\\', '/').Trim();
        var root = repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/";
        if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[root.Length..];
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");
}
```

- [x] **Step 4: Coreテストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: 失敗するStore統合テストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreSaveTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreSaveTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreSaveTests()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/company/order-api", "order-api", @"C:\work\order-api",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    internal static VersionToSave Sample(
        string canonicalKey = "domain.mail.order-completed",
        string title = "注文完了メール仕様",
        string summary = "OrderServiceがメールを送る",
        string projectId = "github.com/company/order-api")
    {
        var evidence = new EvidenceRecord(
            Guid.CreateVersion7().ToString("N"), "src/OrderService.cs", null,
            "OrderService.Complete", "method", null, 1, 4,
            "abc123", "filehash", "symbolhash", null);
        var fact = new FactRecord(
            Guid.CreateVersion7().ToString("N"), "メールはOrderServiceが送信する", [evidence.Id]);
        var inference = new InferenceRecord(
            Guid.CreateVersion7().ToString("N"), "リトライはなさそう",
            Confidence.Low, "呼び出し元にリトライ処理が見当たらない", [evidence.Id]);
        var relation = new RelationRecord(
            Guid.CreateVersion7().ToString("N"),
            "OrderService.Complete", "SmtpEmailSender.SendAsync", "calls");
        return new VersionToSave(
            projectId, canonicalKey, title, "abc123", "main",
            "注文完了メールの処理は？", summary, Confidence.High,
            "mail order", "test-agent", [evidence], [fact], [inference], [relation]);
    }

    private long Scalar(string sql)
    {
        using var connection = _db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void SaveVersion_creates_knowledge_version_and_children()
    {
        var result = Store.SaveVersion(Sample());

        Assert.True(result.CreatedNewKnowledge);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_versions;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM facts;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM fact_evidence;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM inferences;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM evidence;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM relations;"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM knowledge WHERE current_version_id IS NOT NULL;"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_fts;"));
    }

    [Fact]
    public void SaveVersion_adds_version_to_existing_canonical_key() // 要件6.1
    {
        var first = Store.SaveVersion(Sample(summary: "旧サマリー"));
        var second = Store.SaveVersion(Sample(summary: "新サマリー"));

        Assert.False(second.CreatedNewKnowledge);
        Assert.Equal(first.KnowledgeId, second.KnowledgeId);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge;"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM knowledge_versions;"));
        // FTSは最新確定バージョンのみ（AC-23）
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM knowledge_fts;"));
        Assert.Equal(1, Scalar(
            "SELECT COUNT(*) FROM knowledge_fts WHERE summary = '新サマリー';"));
    }

    [Fact]
    public void ListSummaries_returns_current_versions_for_project_only()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/other/repo", "other", @"C:\work\other",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Store.SaveVersion(Sample());
        Store.SaveVersion(Sample(projectId: "github.com/other/repo", canonicalKey: "other.key"));

        var summaries = Store.ListSummaries("github.com/company/order-api");

        var summary = Assert.Single(summaries);
        Assert.Equal("domain.mail.order-completed", summary.CanonicalKey);
    }

    public void Dispose() => _db.Dispose();
}
```

- [x] **Step 6: SqliteKnowledgeStoreの保存系を実装する**

`src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs`（このタスクでは`SaveVersion`と`ListSummaries`まで実装し、`GetDetail`・`SearchFts`・`SearchLike`は`NotImplementedException`ではなくTask 10・11で追加するprivateメソッド分割を見越して部分実装とする。コンパイルを通すため、このタスクの時点では後続メソッドは`throw new NotSupportedException("Implemented in a later task.");`とする）:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Stores;

public sealed class SqliteKnowledgeStore(SqliteConnectionFactory factory) : IKnowledgeStore
{
    public SaveVersionResult SaveVersion(VersionToSave version)
    {
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var knowledgeId = FindKnowledgeId(connection, version.ProjectId, version.CanonicalKey);
        var createdNew = knowledgeId is null;
        if (createdNew)
        {
            knowledgeId = Guid.CreateVersion7().ToString("N");
            Execute(connection, """
                INSERT INTO knowledge (id, project_id, canonical_key, title, current_version_id, created_at, updated_at)
                VALUES (@id, @project, @key, @title, NULL, @now, @now);
                """,
                ("@id", knowledgeId), ("@project", version.ProjectId),
                ("@key", version.CanonicalKey), ("@title", version.Title), ("@now", now));
        }

        var versionId = Guid.CreateVersion7().ToString("N");
        Execute(connection, """
            INSERT INTO knowledge_versions
                (id, knowledge_id, commit_hash, branch_name, original_question, summary,
                 confidence, tags, created_at, created_by, retain, retain_reason)
            VALUES (@id, @knowledge, @commit, @branch, @question, @summary,
                    @confidence, @tags, @now, @createdBy, 0, NULL);
            """,
            ("@id", versionId), ("@knowledge", knowledgeId!), ("@commit", version.CommitHash),
            ("@branch", (object?)version.BranchName ?? DBNull.Value),
            ("@question", version.OriginalQuestion), ("@summary", version.Summary),
            ("@confidence", version.Confidence.ToDbValue()), ("@tags", version.Tags),
            ("@now", now), ("@createdBy", version.CreatedBy));

        foreach (var evidence in version.Evidence)
        {
            Execute(connection, """
                INSERT INTO evidence
                    (id, knowledge_version_id, file_path, symbol_id, symbol_name, symbol_kind,
                     signature, start_line, end_line, commit_hash, file_hash, symbol_hash, reason)
                VALUES (@id, @version, @path, @symbolId, @symbolName, @symbolKind,
                        @signature, @start, @end, @commit, @fileHash, @symbolHash, @reason);
                """,
                ("@id", evidence.Id), ("@version", versionId), ("@path", evidence.FilePath),
                ("@symbolId", (object?)evidence.SymbolId ?? DBNull.Value),
                ("@symbolName", evidence.SymbolName),
                ("@symbolKind", (object?)evidence.SymbolKind ?? DBNull.Value),
                ("@signature", (object?)evidence.Signature ?? DBNull.Value),
                ("@start", (object?)evidence.StartLine ?? DBNull.Value),
                ("@end", (object?)evidence.EndLine ?? DBNull.Value),
                ("@commit", evidence.CommitHash), ("@fileHash", evidence.FileHash),
                ("@symbolHash", (object?)evidence.SymbolHash ?? DBNull.Value),
                ("@reason", (object?)evidence.Reason ?? DBNull.Value));
        }

        var sortOrder = 0;
        foreach (var fact in version.Facts)
        {
            Execute(connection,
                "INSERT INTO facts (id, knowledge_version_id, text, sort_order) VALUES (@id, @version, @text, @order);",
                ("@id", fact.Id), ("@version", versionId), ("@text", fact.Text), ("@order", sortOrder++));
            foreach (var evidenceId in fact.EvidenceIds)
                Execute(connection,
                    "INSERT INTO fact_evidence (fact_id, evidence_id) VALUES (@fact, @evidence);",
                    ("@fact", fact.Id), ("@evidence", evidenceId));
        }

        sortOrder = 0;
        foreach (var inference in version.Inferences)
        {
            Execute(connection, """
                INSERT INTO inferences (id, knowledge_version_id, text, confidence, reason, sort_order)
                VALUES (@id, @version, @text, @confidence, @reason, @order);
                """,
                ("@id", inference.Id), ("@version", versionId), ("@text", inference.Text),
                ("@confidence", inference.Confidence.ToDbValue()),
                ("@reason", inference.Reason), ("@order", sortOrder++));
            foreach (var evidenceId in inference.EvidenceIds)
                Execute(connection,
                    "INSERT INTO inference_evidence (inference_id, evidence_id) VALUES (@inference, @evidence);",
                    ("@inference", inference.Id), ("@evidence", evidenceId));
        }

        foreach (var relation in version.Relations)
        {
            Execute(connection, """
                INSERT INTO relations (id, knowledge_version_id, from_symbol, to_symbol, kind)
                VALUES (@id, @version, @from, @to, @kind);
                """,
                ("@id", relation.Id), ("@version", versionId),
                ("@from", relation.FromSymbol), ("@to", relation.ToSymbol), ("@kind", relation.Kind));
        }

        // 昇格 + FTS同期（要件12.1: 同一トランザクション内の明示delete + insert）
        Execute(connection,
            "UPDATE knowledge SET current_version_id = @version, title = @title, updated_at = @now WHERE id = @id;",
            ("@version", versionId), ("@title", version.Title), ("@now", now), ("@id", knowledgeId!));
        Execute(connection,
            "DELETE FROM knowledge_fts WHERE knowledge_id = @id;", ("@id", knowledgeId!));
        Execute(connection, """
            INSERT INTO knowledge_fts
                (title, original_question, summary, facts, inferences, tags,
                 symbol_names, symbol_ids, file_paths, canonical_key, knowledge_id, project_id)
            VALUES (@title, @question, @summary, @facts, @inferences, @tags,
                    @symbolNames, @symbolIds, @filePaths, @key, @knowledge, @project);
            """,
            ("@title", version.Title), ("@question", version.OriginalQuestion),
            ("@summary", version.Summary),
            ("@facts", string.Join('\n', version.Facts.Select(fact => fact.Text))),
            ("@inferences", string.Join('\n',
                version.Inferences.Select(inference => $"{inference.Text}\n{inference.Reason}"))),
            ("@tags", version.Tags),
            ("@symbolNames", string.Join('\n', version.Evidence.Select(e => e.SymbolName))),
            ("@symbolIds", string.Join('\n',
                version.Evidence.Where(e => e.SymbolId is not null).Select(e => e.SymbolId!))),
            ("@filePaths", string.Join('\n', version.Evidence.Select(e => e.FilePath).Distinct())),
            ("@key", version.CanonicalKey), ("@knowledge", knowledgeId!),
            ("@project", version.ProjectId));

        transaction.Commit();
        return new SaveVersionResult(knowledgeId!, versionId, createdNew);
    }

    public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at
            FROM knowledge k
            JOIN knowledge_versions v ON v.id = k.current_version_id
            WHERE k.project_id = @project;
            """;
        command.Parameters.AddWithValue("@project", projectId);
        using var reader = command.ExecuteReader();
        var summaries = new List<KnowledgeSummary>();
        while (reader.Read())
            summaries.Add(ReadSummary(reader));
        return summaries;
    }

    public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
        => throw new NotSupportedException("Implemented in a later task.");

    public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
        => throw new NotSupportedException("Implemented in a later task.");

    public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
        => throw new NotSupportedException("Implemented in a later task.");

    internal static KnowledgeSummary ReadSummary(SqliteDataReader reader)
    {
        ConfidenceParser.TryParse(reader.GetString(5), out var confidence);
        return new KnowledgeSummary(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), confidence,
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static string? FindKnowledgeId(
        SqliteConnection connection, string projectId, string canonicalKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id FROM knowledge WHERE project_id = @project AND canonical_key = @key;";
        command.Parameters.AddWithValue("@project", projectId);
        command.Parameters.AddWithValue("@key", canonicalKey);
        return command.ExecuteScalar() as string;
    }

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
```

注意: `SqliteCommand.Transaction`は同一接続の`BeginTransaction`中は自動関連付けされないため、コンパイル・実行時にエラーになる場合は`Execute`へ`SqliteTransaction`を渡して`command.Transaction`へ設定する形へ修正する（Microsoft.Data.Sqliteはトランザクション中のコマンドに`Transaction`設定を要求する）。

- [x] **Step 7: 統合テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: PASS。

- [x] **Step 8: コミット**

```bash
git add src/CodeKnowledge.Core/Knowledge src/CodeKnowledge.Infrastructure/Stores tests/
git commit -m "feat: add save knowledge use case with transactional store and fts sync"
```

---

### Task 10: ハイブリッド検索

**Files:**
- Create: `src/CodeKnowledge.Core/Search/SearchKnowledgeUseCase.cs`
- Modify: `src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs`（`SearchFts`・`SearchLike`の実装）
- Test: `tests/CodeKnowledge.Core.Tests/SearchKnowledgeUseCaseTests.cs`
- Test: `tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreSearchTests.cs`

**Interfaces:**
- Consumes: `KeywordPreparation`、`IKnowledgeStore`、`ResolveProjectUseCase`
- Produces:

```csharp
public sealed record SearchResultItem(
    string KnowledgeId, string CanonicalKey, string Title, string Summary,
    string CommitHash, Confidence Confidence, DateTimeOffset UpdatedAt,
    string MatchedRoute,                      // "fts" / "like" / "both"
    IReadOnlyList<string> MatchedKeywords);

public sealed record SearchKnowledgeResult(
    string ProjectId, IReadOnlyList<SearchResultItem> Results, int TotalCandidates);

public sealed class SearchKnowledgeUseCase(ResolveProjectUseCase resolveProject, IKnowledgeStore store)
{
    public SearchKnowledgeResult Execute(
        string workingDirectory, IReadOnlyList<string> keywords, int? limit);
}
```

- [x] **Step 1: 失敗するCoreテスト（マージ・ランキング）を書く**

`tests/CodeKnowledge.Core.Tests/SearchKnowledgeUseCaseTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Core.Tests.Fakes;

namespace CodeKnowledge.Core.Tests;

public sealed class SearchKnowledgeUseCaseTests
{
    private sealed class SearchFake : IKnowledgeStore
    {
        public List<FtsSearchHit> FtsHits { get; } = [];
        public List<LikeSearchHit> LikeHits { get; } = [];
        public string? LastMatchExpression;
        public IReadOnlyList<string>? LastLikePatterns;

        public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
        {
            LastMatchExpression = matchExpression;
            return FtsHits;
        }

        public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
        {
            LastLikePatterns = likePatterns;
            return LikeHits;
        }

        public SaveVersionResult SaveVersion(VersionToSave version) => throw new NotSupportedException();
        public IReadOnlyList<KnowledgeSummary> ListSummaries(string projectId) => [];
        public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId) => null;
    }

    private readonly FakeGitRepository _git = new();
    private readonly SearchFake _store = new();

    private SearchKnowledgeUseCase UseCase => new(
        new ResolveProjectUseCase(_git, new FakeProjectStore()), _store);

    public SearchKnowledgeUseCaseTests()
    {
        _git.Context = new GitContext(
            @"C:\work\order-api", "abc123", "main",
            new Dictionary<string, string> { ["origin"] = "https://h.example/company/order-api" },
            null, null);
    }

    private static KnowledgeSummary Summary(string id, DateTimeOffset? updatedAt = null)
        => new(id, $"key.{id}", $"タイトル{id}", "概要", "abc",
            Confidence.High, updatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Execute_ranks_both_then_fts_then_like() // 要件8.3のマージ表
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("fts-only"), Score: -1.5, "メール通知の仕様"));
        _store.FtsHits.Add(new FtsSearchHit(Summary("both"), Score: -0.5, "注文メールの仕様 確認"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("both"), "注文メールの仕様 確認"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("like-only"), "仕様の確認メモ"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール", "仕様"], limit: 10);

        Assert.Equal(["both", "fts-only", "like-only"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
        Assert.Equal("both", result.Results[0].MatchedRoute);
        Assert.Equal("fts", result.Results[1].MatchedRoute);
        Assert.Equal("like", result.Results[2].MatchedRoute);
        Assert.Equal(3, result.TotalCandidates);
    }

    [Fact]
    public void Execute_orders_fts_hits_by_bm25_ascending()
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("weak"), Score: -0.2, "メール"));
        _store.FtsHits.Add(new FtsSearchHit(Summary("strong"), Score: -3.0, "メール メール"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール"], limit: 10);

        Assert.Equal(["strong", "weak"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
    }

    [Fact]
    public void Execute_orders_like_only_hits_by_matched_count_then_recency()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-1);
        _store.LikeHits.Add(new LikeSearchHit(Summary("one-word", older), "仕様のみ"));
        _store.LikeHits.Add(new LikeSearchHit(Summary("two-words"), "仕様の確認"));

        var result = UseCase.Execute(@"C:\work\order-api", ["仕様", "確認"], limit: 10);

        Assert.Equal(["two-words", "one-word"],
            result.Results.Select(item => item.KnowledgeId).ToArray());
        Assert.Equal(["仕様", "確認"], result.Results[0].MatchedKeywords);
    }

    [Fact]
    public void Execute_reports_matched_keywords_from_search_text() // AC-22
    {
        _store.FtsHits.Add(new FtsSearchHit(Summary("hit"), Score: -1.0, "注文完了メールの仕様"));

        var result = UseCase.Execute(@"C:\work\order-api", ["メール", "存在しない語"], limit: 10);

        Assert.Equal(["メール"], result.Results[0].MatchedKeywords);
    }

    [Fact]
    public void Execute_clamps_limit_and_allows_empty_results()
    {
        var result = UseCase.Execute(@"C:\work\order-api", ["メール"], limit: 999);
        Assert.Empty(result.Results); // エラーにしない（要件10.2）
    }

    [Fact]
    public void Execute_skips_fts_route_when_no_long_keywords()
    {
        UseCase.Execute(@"C:\work\order-api", ["仕様"], limit: 10);
        Assert.Null(_store.LastMatchExpression);
        Assert.NotNull(_store.LastLikePatterns);
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: SearchKnowledgeUseCaseを実装する**

`src/CodeKnowledge.Core/Search/SearchKnowledgeUseCase.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Search;

public sealed record SearchResultItem(
    string KnowledgeId, string CanonicalKey, string Title, string Summary,
    string CommitHash, Confidence Confidence, DateTimeOffset UpdatedAt,
    string MatchedRoute,
    IReadOnlyList<string> MatchedKeywords);

public sealed record SearchKnowledgeResult(
    string ProjectId, IReadOnlyList<SearchResultItem> Results, int TotalCandidates);

public sealed class SearchKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IKnowledgeStore store)
{
    private const int CandidateFetchLimit = 200;

    public SearchKnowledgeResult Execute(
        string workingDirectory, IReadOnlyList<string> keywords, int? limit)
    {
        var resolution = resolveProject.Execute(workingDirectory);
        var effectiveLimit = Math.Clamp(limit ?? 10, 1, 50);
        var prepared = KeywordPreparation.Prepare(keywords);
        var allKeywords = prepared.FtsKeywords.Concat(prepared.LikeKeywords).ToList();

        var ftsHits = prepared.FtsMatchExpression is null
            ? (IReadOnlyList<FtsSearchHit>)[]
            : store.SearchFts(resolution.ProjectId, prepared.FtsMatchExpression, CandidateFetchLimit);
        var likeHits = prepared.LikeKeywords.Count == 0
            ? (IReadOnlyList<LikeSearchHit>)[]
            : store.SearchLike(
                resolution.ProjectId,
                prepared.LikeKeywords.Select(KeywordPreparation.EscapeLikePattern).ToList());

        var likeIds = likeHits.Select(hit => hit.Summary.KnowledgeId).ToHashSet(StringComparer.Ordinal);
        var ftsIds = ftsHits.Select(hit => hit.Summary.KnowledgeId).ToHashSet(StringComparer.Ordinal);

        var ranked = new List<(int Tier, double Primary, double Secondary, SearchResultItem Item)>();
        foreach (var hit in ftsHits)
        {
            var route = likeIds.Contains(hit.Summary.KnowledgeId) ? "both" : "fts";
            ranked.Add((route == "both" ? 0 : 1, hit.Score, 0,
                ToItem(hit.Summary, route, MatchedKeywords(hit.SearchText, allKeywords))));
        }
        foreach (var hit in likeHits.Where(hit => !ftsIds.Contains(hit.Summary.KnowledgeId)))
        {
            var matched = MatchedKeywords(hit.SearchText, allKeywords);
            var likeMatchedCount = prepared.LikeKeywords.Count(matched.Contains);
            ranked.Add((2, -likeMatchedCount, -hit.Summary.UpdatedAt.ToUnixTimeMilliseconds(),
                ToItem(hit.Summary, "like", matched)));
        }

        var ordered = ranked
            .OrderBy(entry => entry.Tier)
            .ThenBy(entry => entry.Primary)
            .ThenBy(entry => entry.Secondary)
            .Select(entry => entry.Item)
            .ToList();

        return new SearchKnowledgeResult(
            resolution.ProjectId,
            ordered.Take(effectiveLimit).ToList(),
            ordered.Count);
    }

    private static SearchResultItem ToItem(
        KnowledgeSummary summary, string route, IReadOnlyList<string> matchedKeywords)
        => new(summary.KnowledgeId, summary.CanonicalKey, summary.Title, summary.Summary,
            summary.CommitHash, summary.Confidence, summary.UpdatedAt, route, matchedKeywords);

    private static IReadOnlyList<string> MatchedKeywords(
        string searchText, IReadOnlyList<string> keywords)
        => keywords
            .Where(keyword => searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
```

- [x] **Step 4: Coreテストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Core.Tests`
Expected: PASS。

- [x] **Step 5: 失敗するStore統合テストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreSearchTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreSearchTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreSearchTests()
    {
        var projects = new SqliteProjectStore(_db.Factory);
        projects.Upsert(new Project("github.com/company/order-api", "order-api",
            @"C:\work\order-api", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        projects.Upsert(new Project("github.com/other/repo", "other",
            @"C:\work\other", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SearchFts_matches_japanese_trigram_keyword()
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            title: "注文完了メール仕様"));

        var hits = Store.SearchFts("github.com/company/order-api", "\"メール\"", 10);

        var hit = Assert.Single(hits);
        Assert.Equal("注文完了メール仕様", hit.Summary.Title);
        Assert.Contains("メール", hit.SearchText);
    }

    [Fact]
    public void SearchFts_scopes_to_project() // AC-01
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "注文完了メール仕様"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(
            projectId: "github.com/other/repo", title: "注文完了メール仕様"));

        var hits = Store.SearchFts("github.com/company/order-api", "\"メール\"", 10);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchFts_only_finds_current_version() // AC-23
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "旧世代キーワードXYZQW"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "新しい概要"));

        Assert.Empty(Store.SearchFts("github.com/company/order-api", "\"XYZQW\"", 10));
    }

    [Fact]
    public void SearchLike_matches_two_char_keyword()
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(title: "注文完了メール仕様"));

        var hits = Store.SearchLike("github.com/company/order-api", ["%仕様%"]);

        Assert.Single(hits);
    }

    [Fact]
    public void SearchLike_does_not_scan_file_paths() // 要件8.3
    {
        // file_pathにだけ「ab」を含むナレッジ（symbol名等には含まない）
        var version = SqliteKnowledgeStoreSaveTests.Sample() with
        {
            Title = "タイトル", Summary = "概要", Tags = "",
            CanonicalKey = "key.one", OriginalQuestion = "質問",
        };
        var evidence = version.Evidence[0] with
        {
            FilePath = "src/ab.cs", SymbolName = "Foo", SymbolId = null,
        };
        var fact = version.Facts[0] with { Text = "事実", EvidenceIds = [evidence.Id] };
        var inference = version.Inferences[0] with { Text = "推論", Reason = "理由", EvidenceIds = [evidence.Id] };
        Store.SaveVersion(version with
        {
            Evidence = [evidence], Facts = [fact], Inferences = [inference],
        });

        Assert.Empty(Store.SearchLike("github.com/company/order-api", ["%ab%"]));
    }

    [Fact]
    public void SearchLike_escapes_metacharacters() // AC-21
    {
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "進捗は50%です"));

        var hits = Store.SearchLike("github.com/company/order-api", ["%50\\%%"]);

        Assert.Single(hits);
        // ワイルドカードとして解釈されるなら「50x」もヒットしてしまう
        Assert.Empty(Store.SearchLike("github.com/company/order-api", ["%5\\_0%"]));
    }

    public void Dispose() => _db.Dispose();
}
```

- [x] **Step 6: Storeの検索メソッドを実装する**

`src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs`の`SearchFts`・`SearchLike`を置き換える:

```csharp
    public IReadOnlyList<FtsSearchHit> SearchFts(string projectId, string matchExpression, int limit)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at,
                   bm25(knowledge_fts) AS score,
                   knowledge_fts.title || char(10) || knowledge_fts.original_question || char(10) ||
                   knowledge_fts.summary || char(10) || knowledge_fts.facts || char(10) ||
                   knowledge_fts.inferences || char(10) || knowledge_fts.tags || char(10) ||
                   knowledge_fts.symbol_names || char(10) || knowledge_fts.symbol_ids || char(10) ||
                   knowledge_fts.file_paths || char(10) || knowledge_fts.canonical_key AS search_text
            FROM knowledge_fts
            JOIN knowledge k ON k.id = knowledge_fts.knowledge_id
            JOIN knowledge_versions v ON v.id = k.current_version_id
            WHERE knowledge_fts MATCH @match AND knowledge_fts.project_id = @project
            ORDER BY score
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@match", matchExpression);
        command.Parameters.AddWithValue("@project", projectId);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var hits = new List<FtsSearchHit>();
        while (reader.Read())
            hits.Add(new FtsSearchHit(ReadSummary(reader), reader.GetDouble(7), reader.GetString(8)));
        return hits;
    }

    public IReadOnlyList<LikeSearchHit> SearchLike(string projectId, IReadOnlyList<string> likePatterns)
    {
        if (likePatterns.Count == 0)
            return [];
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        // 要件8.3: LIKEルートは実テーブルを走査し、ファイルパスは対象外とする
        var conditions = string.Join(" OR ", likePatterns.Select(
            (_, index) => $"search_text LIKE @like{index} ESCAPE '\\'"));
        command.CommandText = $"""
            SELECT * FROM (
                SELECT k.id, k.canonical_key, k.title, v.summary, v.commit_hash, v.confidence, k.updated_at,
                       k.title || char(10) || v.original_question || char(10) || v.summary || char(10) ||
                       v.tags || char(10) || k.canonical_key || char(10) ||
                       IFNULL((SELECT group_concat(f.text, char(10)) FROM facts f
                               WHERE f.knowledge_version_id = v.id), '') || char(10) ||
                       IFNULL((SELECT group_concat(i.text || char(10) || i.reason, char(10)) FROM inferences i
                               WHERE i.knowledge_version_id = v.id), '') || char(10) ||
                       IFNULL((SELECT group_concat(e.symbol_name || char(10) || IFNULL(e.symbol_id, ''), char(10))
                               FROM evidence e WHERE e.knowledge_version_id = v.id), '')
                       AS search_text
                FROM knowledge k
                JOIN knowledge_versions v ON v.id = k.current_version_id
                WHERE k.project_id = @project
            )
            WHERE {conditions};
            """;
        command.Parameters.AddWithValue("@project", projectId);
        for (var index = 0; index < likePatterns.Count; index++)
            command.Parameters.AddWithValue($"@like{index}", likePatterns[index]);
        using var reader = command.ExecuteReader();
        var hits = new List<LikeSearchHit>();
        while (reader.Read())
            hits.Add(new LikeSearchHit(ReadSummary(reader), reader.GetString(7)));
        return hits;
    }
```

- [x] **Step 7: 統合テストが成功することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: PASS。

- [x] **Step 8: コミット**

```bash
git add src/CodeKnowledge.Core/Search src/CodeKnowledge.Infrastructure/Stores tests/
git commit -m "feat: add hybrid fts and like search with merge ranking"
```

---

### Task 11: ナレッジ取得

**Files:**
- Create: `src/CodeKnowledge.Core/Knowledge/GetKnowledgeUseCase.cs`
- Modify: `src/CodeKnowledge.Infrastructure/Stores/SqliteKnowledgeStore.cs`（`GetDetail`の実装）
- Test: `tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreGetTests.cs`

**Interfaces:**
- Consumes: `IKnowledgeStore`、`ResolveProjectUseCase`、`KnowledgeDetail`
- Produces: `GetKnowledgeUseCase(ResolveProjectUseCase, IKnowledgeStore).Execute(string workingDirectory, string knowledgeId, string? versionId) -> KnowledgeDetail`（見つからなければ`knowledge_not_found`）

- [x] **Step 1: 失敗する統合テストを書く**

`tests/CodeKnowledge.Infrastructure.Tests/SqliteKnowledgeStoreGetTests.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Infrastructure.Stores;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class SqliteKnowledgeStoreGetTests : IDisposable
{
    private readonly TestDatabase _db = new TestDatabase().Migrated();
    private SqliteKnowledgeStore Store => new(_db.Factory);

    public SqliteKnowledgeStoreGetTests()
    {
        new SqliteProjectStore(_db.Factory).Upsert(new Project(
            "github.com/company/order-api", "order-api", @"C:\work\order-api",
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GetDetail_returns_current_version_with_children() // AC-03
    {
        var saved = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample());

        var detail = Store.GetDetail("github.com/company/order-api", saved.KnowledgeId, null);

        Assert.NotNull(detail);
        Assert.Equal(saved.VersionId, detail.VersionId);
        Assert.Equal("注文完了メール仕様", detail.Title);
        var fact = Assert.Single(detail.Facts);
        var evidence = Assert.Single(detail.Evidence);
        Assert.Equal([evidence.Id], fact.EvidenceIds);
        Assert.Equal("src/OrderService.cs", evidence.FilePath);
        Assert.Single(detail.Inferences);
        Assert.Single(detail.Relations);
        Assert.Equal(Confidence.High, detail.Confidence);
    }

    [Fact]
    public void GetDetail_returns_specified_older_version()
    {
        var first = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "旧"));
        Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample(summary: "新"));

        var detail = Store.GetDetail(
            "github.com/company/order-api", first.KnowledgeId, first.VersionId);

        Assert.NotNull(detail);
        Assert.Equal("旧", detail.Summary);
    }

    [Fact]
    public void GetDetail_returns_null_for_unknown_id_or_wrong_project()
    {
        var saved = Store.SaveVersion(SqliteKnowledgeStoreSaveTests.Sample());
        Assert.Null(Store.GetDetail("github.com/company/order-api", "missing", null));
        Assert.Null(Store.GetDetail("github.com/other/repo", saved.KnowledgeId, null));
    }

    public void Dispose() => _db.Dispose();
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Infrastructure.Tests`
Expected: `GetDetail`が`NotSupportedException`のため失敗。

- [x] **Step 3: `GetDetail`とユースケースを実装する**

`SqliteKnowledgeStore.GetDetail`を置き換える:

```csharp
    public KnowledgeDetail? GetDetail(string projectId, string knowledgeId, string? versionId)
    {
        using var connection = factory.Open();
        using var headerCommand = connection.CreateCommand();
        headerCommand.CommandText = """
            SELECT k.id, k.canonical_key, k.title, v.id, v.commit_hash, v.branch_name,
                   v.original_question, v.summary, v.confidence, v.tags, v.created_by, v.created_at
            FROM knowledge k
            JOIN knowledge_versions v
              ON v.knowledge_id = k.id
             AND v.id = IFNULL(@version, k.current_version_id)
            WHERE k.id = @knowledge AND k.project_id = @project;
            """;
        headerCommand.Parameters.AddWithValue("@knowledge", knowledgeId);
        headerCommand.Parameters.AddWithValue("@project", projectId);
        headerCommand.Parameters.AddWithValue("@version", (object?)versionId ?? DBNull.Value);
        using var headerReader = headerCommand.ExecuteReader();
        if (!headerReader.Read())
            return null;

        ConfidenceParser.TryParse(headerReader.GetString(8), out var confidence);
        var resolvedVersionId = headerReader.GetString(3);

        return new KnowledgeDetail(
            headerReader.GetString(0), headerReader.GetString(1), headerReader.GetString(2),
            resolvedVersionId, headerReader.GetString(4),
            headerReader.IsDBNull(5) ? null : headerReader.GetString(5),
            headerReader.GetString(6), headerReader.GetString(7), confidence,
            headerReader.GetString(9), headerReader.GetString(10),
            DateTimeOffset.Parse(headerReader.GetString(11)),
            ReadFacts(connection, resolvedVersionId),
            ReadInferences(connection, resolvedVersionId),
            ReadEvidence(connection, resolvedVersionId),
            ReadRelations(connection, resolvedVersionId));
    }

    private static List<FactRecord> ReadFacts(SqliteConnection connection, string versionId)
    {
        var facts = new List<FactRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.id, f.text,
                   IFNULL((SELECT group_concat(fe.evidence_id, ',') FROM fact_evidence fe
                           WHERE fe.fact_id = f.id), '')
            FROM facts f
            WHERE f.knowledge_version_id = @version
            ORDER BY f.sort_order;
            """;
        command.Parameters.AddWithValue("@version", versionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            facts.Add(new FactRecord(reader.GetString(0), reader.GetString(1),
                SplitIds(reader.GetString(2))));
        return facts;
    }

    private static List<InferenceRecord> ReadInferences(SqliteConnection connection, string versionId)
    {
        var inferences = new List<InferenceRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.id, i.text, i.confidence, i.reason,
                   IFNULL((SELECT group_concat(ie.evidence_id, ',') FROM inference_evidence ie
                           WHERE ie.inference_id = i.id), '')
            FROM inferences i
            WHERE i.knowledge_version_id = @version
            ORDER BY i.sort_order;
            """;
        command.Parameters.AddWithValue("@version", versionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ConfidenceParser.TryParse(reader.GetString(2), out var confidence);
            inferences.Add(new InferenceRecord(reader.GetString(0), reader.GetString(1),
                confidence, reader.GetString(3), SplitIds(reader.GetString(4))));
        }
        return inferences;
    }

    private static List<EvidenceRecord> ReadEvidence(SqliteConnection connection, string versionId)
    {
        var records = new List<EvidenceRecord>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_path, symbol_id, symbol_name, symbol_kind, signature,
                   start_line, end_line, commit_hash, file_hash, symbol_hash, reason
            FROM evidence WHERE knowledge_version_id = @version;
            """;
        command.Parameters.AddWithValue("@version", versionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            records.Add(new EvidenceRecord(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        return records;
    }

    private static List<RelationRecord> ReadRelations(SqliteConnection connection, string versionId)
    {
        var relations = new List<RelationRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, from_symbol, to_symbol, kind FROM relations WHERE knowledge_version_id = @version;";
        command.Parameters.AddWithValue("@version", versionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            relations.Add(new RelationRecord(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
        return relations;
    }

    private static IReadOnlyList<string> SplitIds(string joined)
        => joined.Length == 0 ? [] : joined.Split(',');
```

`src/CodeKnowledge.Core/Knowledge/GetKnowledgeUseCase.cs`:

```csharp
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Knowledge;

public sealed class GetKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IKnowledgeStore store)
{
    public KnowledgeDetail Execute(string workingDirectory, string knowledgeId, string? versionId)
    {
        var resolution = resolveProject.Execute(workingDirectory);
        return store.GetDetail(resolution.ProjectId, knowledgeId, versionId)
            ?? throw new CodeKnowledgeException(
                CodeKnowledgeException.KnowledgeNotFound,
                $"Knowledge '{knowledgeId}' was not found in project '{resolution.ProjectId}'.");
    }
}
```

- [x] **Step 4: テストが成功することを確認する**

Run: `dotnet test CodeKnowledge.slnx`
Expected: 全テストPASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Core/Knowledge src/CodeKnowledge.Infrastructure/Stores tests/
git commit -m "feat: add get knowledge use case returning full detail"
```

---

### Task 12: MCPアダプター

**Files:**
- Create: `src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs`
- Create: `src/CodeKnowledge.Mcp/Tools/ToolGuard.cs`
- Modify: `src/CodeKnowledge.Mcp/Program.cs`
- Test: `tests/CodeKnowledge.Mcp.Tests/ToolGuardTests.cs`

**Interfaces:**
- Consumes: 4ユースケース、`DatabasePathResolver`、`MigrationRunner`、`CodeKnowledgeException`
- Produces: MCP Tool `resolve_project` / `search_knowledge` / `get_knowledge` / `save_knowledge`。エラーは`McpException`のメッセージ先頭に`<code>: `を付けて返す

- [x] **Step 1: Mcp.TestsへMcpプロジェクト参照を追加し、失敗するテストを書く**

Run: `dotnet add tests/CodeKnowledge.Mcp.Tests/CodeKnowledge.Mcp.Tests.csproj reference src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj`

`tests/CodeKnowledge.Mcp.Tests/ToolGuardTests.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Mcp.Tools;
using ModelContextProtocol;

namespace CodeKnowledge.Mcp.Tests;

public sealed class ToolGuardTests
{
    [Fact]
    public void Execute_passes_through_success()
    {
        Assert.Equal(42, ToolGuard.Execute(() => 42));
    }

    [Fact]
    public void Execute_maps_domain_error_to_mcp_exception_with_code()
    {
        var exception = Assert.Throws<McpException>(() => ToolGuard.Execute<int>(
            () => throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "The current directory is not inside a usable Git repository.")));
        Assert.StartsWith("git_repository_required: ", exception.Message);
    }

    [Fact]
    public void Execute_maps_unexpected_error_to_internal_error()
    {
        var exception = Assert.Throws<McpException>(() => ToolGuard.Execute<int>(
            () => throw new InvalidOperationException("boom")));
        Assert.StartsWith("internal_error: ", exception.Message);
    }
}
```

- [x] **Step 2: テストが失敗することを確認する**

Run: `dotnet test tests/CodeKnowledge.Mcp.Tests`
Expected: コンパイルエラーで失敗。

- [x] **Step 3: ToolGuard・Tool・Programを実装する**

`src/CodeKnowledge.Mcp/Tools/ToolGuard.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;

namespace CodeKnowledge.Mcp.Tools;

public static class ToolGuard
{
    private const int SqliteBusy = 5;

    public static T Execute<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (CodeKnowledgeException exception)
        {
            throw new McpException($"{exception.Code}: {exception.Message}");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteBusy)
        {
            throw new McpException(
                $"{CodeKnowledgeException.DatabaseBusy}: The database is busy. Retry later.");
        }
        catch (Exception exception)
        {
            // 内部詳細（スタックトレース等）はクライアントへ返さずstderrログに任せる
            throw new McpException(
                $"{CodeKnowledgeException.InternalError}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
```

`src/CodeKnowledge.Mcp/Tools/CodeKnowledgeTools.cs`:

```csharp
using System.ComponentModel;
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using ModelContextProtocol.Server;

namespace CodeKnowledge.Mcp.Tools;

[McpServerToolType]
public sealed class CodeKnowledgeTools(
    ResolveProjectUseCase resolveProject,
    SearchKnowledgeUseCase searchKnowledge,
    GetKnowledgeUseCase getKnowledge,
    SaveKnowledgeUseCase saveKnowledge)
{
    [McpServerTool(Name = "resolve_project", UseStructuredContent = true),
        Description("Resolves the current Git repository into a Code Knowledge project. " +
            "Call before assuming any knowledge scope. Fails outside a Git repository.")]
    public ProjectResolution ResolveProject(
        [Description("Absolute path of the current working directory.")] string workingDirectory)
        => ToolGuard.Execute(() => resolveProject.Execute(workingDirectory));

    [McpServerTool(Name = "search_knowledge", UseStructuredContent = true),
        Description("Searches saved knowledge in the current project with hybrid FTS/LIKE search. " +
            "Expand keywords aggressively: nouns and compound words from the question, English " +
            "translations (mail, spec), guessed symbol names (EmailSender, OrderCompleted). " +
            "Prefer keywords of 3+ characters; 1-2 character keywords are also matched by substring.")]
    public SearchKnowledgeResult SearchKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Expanded search keywords.")] IReadOnlyList<string> keywords,
        [Description("Maximum results (default 10, max 50).")] int? limit = null)
        => ToolGuard.Execute(() => searchKnowledge.Execute(workingDirectory, keywords, limit));

    [McpServerTool(Name = "get_knowledge", UseStructuredContent = true),
        Description("Gets the current (or a specific) version of a knowledge entry with facts, " +
            "inferences, evidence, and relations.")]
    public KnowledgeDetail GetKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Knowledge id from search results.")] string knowledgeId,
        [Description("Optional specific version id. Omit for the current version.")] string? versionId = null)
        => ToolGuard.Execute(() => getKnowledge.Execute(workingDirectory, knowledgeId, versionId));

    [McpServerTool(Name = "save_knowledge", UseStructuredContent = true),
        Description("Saves an investigation result as knowledge. Only call after the user " +
            "explicitly asked to save or agreed to a save proposal. Facts must reference evidence; " +
            "put uncertain content into inferences. confidence: 'high' = evidence read directly and " +
            "consistent across implementation/callers/tests; 'medium' = main evidence read but " +
            "surroundings unverified; 'low' = mostly guessed from naming/conventions.")]
    public SaveKnowledgeResult SaveKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Stable key for the topic, e.g. domain.mail.order-completed.")] string canonicalKey,
        [Description("Human readable title.")] string title,
        [Description("The original user question that triggered the investigation.")] string originalQuestion,
        [Description("Summary of the findings.")] string summary,
        [Description("Overall confidence: high, medium, or low.")] string confidence,
        [Description("Evidence code locations. Paths may be absolute or repo-relative.")]
            IReadOnlyList<SaveEvidenceInput> evidence,
        [Description("Facts directly confirmed from code. Each must reference evidence by index.")]
            IReadOnlyList<SaveFactInput> facts,
        [Description("Inferences with their own confidence and reason.")]
            IReadOnlyList<SaveInferenceInput> inferences,
        [Description("Symbol relations discovered during the investigation.")]
            IReadOnlyList<SaveRelationInput> relations,
        [Description("Space separated tags.")] string? tags = null,
        [Description("Agent name for created_by.")] string? createdBy = null,
        [Description("Commit the investigation was performed at. Omit to use HEAD.")] string? commitHash = null)
        => ToolGuard.Execute(() => saveKnowledge.Execute(new SaveKnowledgeRequest(
            workingDirectory, canonicalKey, title, originalQuestion, summary, confidence,
            tags, createdBy, commitHash, evidence, facts, inferences, relations)));
}
```

`src/CodeKnowledge.Mcp/Program.cs`:

```csharp
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Infrastructure.Database;
using CodeKnowledge.Infrastructure.Git;
using CodeKnowledge.Infrastructure.Stores;
using CodeKnowledge.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var databasePath = DatabasePathResolver.Resolve();
var connectionFactory = new SqliteConnectionFactory(databasePath);
try
{
    MigrationRunner.Apply(connectionFactory, databasePath);
}
catch (CodeKnowledgeException exception)
{
    await Console.Error.WriteLineAsync($"{exception.Code}: {exception.Message}");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace); // stdoutはMCP通信専用

builder.Services.AddSingleton(connectionFactory);
builder.Services.AddSingleton<IGitRepository, GitCliRepository>();
builder.Services.AddSingleton<IProjectStore, SqliteProjectStore>();
builder.Services.AddSingleton<IKnowledgeStore, SqliteKnowledgeStore>();
builder.Services.AddSingleton<ResolveProjectUseCase>();
builder.Services.AddSingleton<SearchKnowledgeUseCase>();
builder.Services.AddSingleton<GetKnowledgeUseCase>();
builder.Services.AddSingleton<SaveKnowledgeUseCase>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeKnowledgeTools>();

await builder.Build().RunAsync();
return 0;
```

- [x] **Step 4: テストとビルドが成功することを確認する**

Run: `dotnet test CodeKnowledge.slnx`
Expected: 全テストPASS。

- [x] **Step 5: コミット**

```bash
git add src/CodeKnowledge.Mcp tests/CodeKnowledge.Mcp.Tests
git commit -m "feat: add mcp stdio adapter exposing four knowledge tools"
```

---

### Task 13: 発行E2Eテスト

**Files:**
- Test: `tests/CodeKnowledge.Mcp.Tests/PublishedServerFixture.cs`
- Test: `tests/CodeKnowledge.Mcp.Tests/McpEndToEndTests.cs`

**Interfaces:**
- Consumes: 発行済み`CodeKnowledge.Mcp.exe`、`ModelContextProtocol`クライアントAPI、`TestGitRepo`と同等のGitフィクスチャ
- Produces: なし（検証のみ）

注意: MCPプロトコルクライアントの正確なAPI使用例は`spikes/phase0/CodeKnowledge.Phase0.Tests/McpProbeTests.cs`にPhase 0で実証済みの形がある。テストコードの書き方の参照は許可する（本番コードへの昇格禁止とは別問題）。`TestGitRepo`はInfrastructure.Testsのものと同内容をMcp.Testsへも作成する（テストプロジェクト間の共有プロジェクトは作らない。重複を許容する）。

- [x] **Step 1: 発行フィクスチャを書く**

`tests/CodeKnowledge.Mcp.Tests/PublishedServerFixture.cs`:

```csharp
using System.Diagnostics;

namespace CodeKnowledge.Mcp.Tests;

/// <summary>テストアセンブリごとに1回だけEXEを発行する。</summary>
public sealed class PublishedServerFixture : IDisposable
{
    public string ExePath { get; }
    public string PublishDirectory { get; }

    public PublishedServerFixture()
    {
        var repoRoot = FindRepoRoot();
        PublishDirectory = Path.Combine(
            Path.GetTempPath(), $"ck-publish-{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "publish", "src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj",
            "--configuration", "Release", "--runtime", "win-x64",
            "--self-contained", "false", "--output", PublishDirectory,
        })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"publish failed: {stdout}\n{stderr}");
        ExePath = Path.Combine(PublishDirectory, "CodeKnowledge.Mcp.exe");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CodeKnowledge.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("CodeKnowledge.slnx not found above test directory.");
    }

    public void Dispose()
    {
        try { Directory.Delete(PublishDirectory, recursive: true); } catch { }
    }
}
```

- [x] **Step 2: 失敗するE2Eテストを書く**

`tests/CodeKnowledge.Mcp.Tests/McpEndToEndTests.cs`（`TestGitRepo`はInfrastructure.Testsと同内容をこのプロジェクトへコピーして使う）:

```csharp
using System.Text.Json;
using ModelContextProtocol.Client;

namespace CodeKnowledge.Mcp.Tests;

public sealed class McpEndToEndTests : IClassFixture<PublishedServerFixture>, IDisposable
{
    private readonly PublishedServerFixture _server;
    private readonly TestGitRepo _repo = new();
    private readonly string _dbDirectory =
        Path.Combine(Path.GetTempPath(), $"ck-e2edb-{Guid.NewGuid():N}");

    public McpEndToEndTests(PublishedServerFixture server)
    {
        _server = server;
        Directory.CreateDirectory(_dbDirectory);
        _repo.Run("remote", "add", "origin", "https://github.com/company/order-api.git");
        _repo.CommitFile("src/OrderService.cs",
            "class OrderService\n{\n    void Complete() { /* メール送信 */ }\n}\n");
    }

    private async Task<IMcpClient> ConnectAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "code-knowledge",
            Command = _server.ExePath,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CODEKNOWLEDGE_DB_PATH"] = Path.Combine(_dbDirectory, "knowledge.db"),
            },
        });
        return await McpClientFactory.CreateAsync(transport);
    }

    private static JsonElement Structured(object? callToolResult)
    {
        // CallToolAsyncの戻り値からstructuredContentをJsonElementとして取り出すヘルパー。
        // SDKの戻り値型（CallToolResult.StructuredContent）に合わせて実装する。
        return JsonSerializer.SerializeToElement(callToolResult);
    }

    [Fact]
    public async Task Lists_all_four_tools()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "resolve_project", "search_knowledge", "get_knowledge", "save_knowledge",
        }, names);
    }

    [Fact]
    public async Task Save_search_get_roundtrip_works() // AC-02, AC-20, AC-27
    {
        await using var client = await ConnectAsync();

        var save = await client.CallToolAsync("save_knowledge", new Dictionary<string, object?>
        {
            ["workingDirectory"] = _repo.Root,
            ["canonicalKey"] = "domain.mail.order-completed",
            ["title"] = "注文完了メール仕様",
            ["originalQuestion"] = "注文完了メールの処理は？",
            ["summary"] = "OrderService.Completeがメールを送信する",
            ["confidence"] = "high",
            ["evidence"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["filePath"] = "src/OrderService.cs",
                    ["symbolName"] = "OrderService.Complete",
                    ["symbolKind"] = "method",
                    ["startLine"] = 1,
                    ["endLine"] = 4,
                },
            },
            ["facts"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["text"] = "Completeがメール送信を行う",
                    ["evidenceIndexes"] = new[] { 0 },
                },
            },
            ["inferences"] = Array.Empty<object>(),
            ["relations"] = Array.Empty<object>(),
        });
        Assert.False(save.IsError);

        var search = await client.CallToolAsync("search_knowledge", new Dictionary<string, object?>
        {
            ["workingDirectory"] = _repo.Root,
            ["keywords"] = new[] { "メール", "仕様", "OrderCompleted" }, // 3文字語 + 2文字語（AC-20）
        });
        Assert.False(search.IsError);
        var searchJson = JsonSerializer.SerializeToElement(search.StructuredContent);
        var first = searchJson.GetProperty("results")[0];
        Assert.Equal("注文完了メール仕様", first.GetProperty("title").GetString());
        var knowledgeId = first.GetProperty("knowledgeId").GetString()!;

        var get = await client.CallToolAsync("get_knowledge", new Dictionary<string, object?>
        {
            ["workingDirectory"] = _repo.Root,
            ["knowledgeId"] = knowledgeId,
        });
        Assert.False(get.IsError);
        var getJson = JsonSerializer.SerializeToElement(get.StructuredContent);
        Assert.Equal(1, getJson.GetProperty("facts").GetArrayLength());
        Assert.Equal("src/OrderService.cs",
            getJson.GetProperty("evidence")[0].GetProperty("filePath").GetString());
    }

    [Fact]
    public async Task Fails_outside_git_repository_without_persisting() // AC-13
    {
        var outside = Path.Combine(Path.GetTempPath(), $"ck-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            await using var client = await ConnectAsync();
            var result = await client.CallToolAsync("resolve_project",
                new Dictionary<string, object?> { ["workingDirectory"] = outside });
            Assert.True(result.IsError);
            var text = string.Join('\n',
                result.Content.Select(content => content.ToString()));
            Assert.Contains("git_repository_required", text);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_dbDirectory, recursive: true); } catch { }
    }
}
```

注意: `CallToolAsync`の戻り値型・`IsError`・`StructuredContent`・エラー時の挙動（`McpException`がスローされるかIsErrorレスポンスになるか）はModelContextProtocol 1.4.1の実際のAPIに合わせて調整する。エラーがスローされる場合は`Assert.ThrowsAsync<McpException>`へ書き換え、メッセージに`git_repository_required`が含まれることを検証する。未使用の`Structured`ヘルパーは削除する。

- [x] **Step 3: E2Eテストを実行して調整する**

Run: `dotnet test tests/CodeKnowledge.Mcp.Tests --configuration Release`
Expected: PASS（発行に数十秒かかる）。

- [x] **Step 4: stdout純度を確認する**

発行EXEを直接起動し、initializeリクエストを送ってstdoutにJSON-RPC以外が混ざらないことをE2Eが実質担保している（プロトコルクライアントが接続に成功する = stdout汚染なし）。追加の手動確認は不要。

- [x] **Step 5: コミット**

```bash
git add tests/CodeKnowledge.Mcp.Tests
git commit -m "test: add published exe end-to-end tests over mcp stdio"
```

---

### Task 14: README・クライアント設定・Agent行動ルール

**Files:**
- Create: `README.md`（リポジトリルート）

**Interfaces:**
- Consumes: 設計書9章、要件10.10・11章
- Produces: 利用者向けドキュメント一式

- [ ] **Step 1: READMEを書く**

`README.md`に以下の章立てで記載する（値はすべて実測・実物に合わせる）:

1. **概要** — Code Knowledgeとは何か（コード調査結果のプロジェクト単位キャッシュ、MCPサーバー）。Phase 1で提供する4 Tool。
2. **ビルドと発行** —

```powershell
dotnet test CodeKnowledge.slnx
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/mcp/win-x64
```

3. **配置とDBルール** — EXEは任意のフォルダへ配置可能（管理者権限不要）。DBは既定でEXEと同じフォルダの`knowledge.db`（環境変数`CODEKNOWLEDGE_DB_PATH`で上書き）。バージョン更新はEXEの上書き置換。フォルダ移動時は`knowledge.db`・`knowledge.db-wal`・`knowledge.db-shm`も一緒に移動。OneDrive等の同期フォルダへ置かない（WAL破損リスク）。
4. **MCPクライアント設定（3クライアント）** — Phase 0 READMEと同形式で、`CodeKnowledge.Mcp.exe`の絶対パスを使ったCursor（`.cursor/mcp.json`、`mcpServers`キー）、GitHub Copilot in VS Code（`.vscode/mcp.json`、`servers`キー）、Claude Code（`claude mcp add --transport stdio --scope project code-knowledge -- <exe絶対パス>`）の設定例。
5. **Agent行動ルール** — 要件11章のルールブロック（13項目のうちPhase 1で有効な1・2・8・9・13。3〜7・10〜12はPhase 2〜3のToolに依存するため「Phase 2以降で追加」と注記して全文は載せる）を、各Agentのルールファイル（`CLAUDE.md`等）へコピーできるコードブロックとして記載。
6. **検証記録** — Phase 0 READMEと同形式の表（自動テスト結果、発行成果物一覧、Claude Code実機検証、Cursor / Copilotは対象外の注記）。このタスクの時点では実機検証行を`未実施`とする。
7. **要件からの変更点** — 設計書11章のDeviations 3点を要約して転記。

- [ ] **Step 2: コミット**

```bash
git add README.md
git commit -m "docs: add readme with client setup, db rules, and agent rules"
```

---

### Task 15: Phase 1完了ゲート（手動検証と記録）

**Files:**
- Modify: `README.md`（検証記録の実測値）
- Modify: `.mcp.json`（`code-knowledge`サーバーの追加）
- Modify: `docs/code-knowledge-tool-requirements-v2.md`（14章 Phase 1チェックリスト。ユーザー承認後）

- [ ] **Step 1: 全自動テストと発行を最終実行する**

Run:

```powershell
dotnet test CodeKnowledge.slnx --configuration Release
dotnet publish src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/mcp/win-x64
```

Expected: 全テストPASS、発行成功。`superpowers:verification-before-completion`スキルに従い、実行結果を確認してから完了を主張する。

- [ ] **Step 2: Claude Codeへ登録する**

```powershell
claude mcp add --transport stdio --scope project code-knowledge -- "C:\zDev\repo\ck\artifacts\mcp\win-x64\CodeKnowledge.Mcp.exe"
```

新しいClaude Codeセッションで承認し、実リポジトリで「resolve_project → save_knowledge → search_knowledge → get_knowledge」の一連をユーザーが実行する。DBは既定パス（EXE隣接）で動作することを確認する。

- [ ] **Step 3: 検証記録を更新してコミットする**

READMEの検証記録へ、テスト件数、発行成果物一覧（`Get-ChildItem -File artifacts/mcp/win-x64`）、Claude Codeのバージョン・検証日・4 Toolの成否を実測で記入する。

```bash
git add README.md .mcp.json
git commit -m "docs: record phase 1 verification results"
```

- [ ] **Step 4: ユーザー承認と要件定義書のチェック**

ユーザーがPhase 1完了を承認したら、`docs/code-knowledge-tool-requirements-v2.md`の14章「Phase 1: 最小実用版」の各項目へ`[x]`と注記を付け、`docs: record phase 1 completion approval`としてコミットする（Phase 0と同じ運用）。承認が得られるまでPhase 2へ進まない。

---

## セルフレビュー記録

- スペックカバレッジ: 設計書3章（起動フロー）→ Task 3・12、4章（スキーマ）→ Task 3、5章（ユースケース）→ Task 5〜11、6章（Tool契約）→ Task 12、7章（Infrastructure）→ Task 3・4、8章（テスト戦略）→ 各タスク + Task 13、9章（ドキュメント）→ Task 14、10章（完了ゲート）→ Task 15。
- AC対応: AC-01（Task 10）、AC-02/03（Task 11・13）、AC-09（Task 9）、AC-10（Task 13の発行E2E）、AC-11（Coreテスト全般がフェイクで直接実行）、AC-12（構成自体で担保）、AC-13（Task 13）、AC-15/16/17/19（Task 5）、AC-20（Task 13）、AC-21（Task 7・10）、AC-22（Task 10）、AC-23（Task 10）、AC-27（Task 13）、AC-28（Task 9）。
- 既知の調整ポイント（実装時にSDK/ライブラリ実挙動へ合わせる箇所）: Task 9のSqliteトランザクション関連付け、Task 13のModelContextProtocolクライアントAPI形状。いずれも注記済み。
