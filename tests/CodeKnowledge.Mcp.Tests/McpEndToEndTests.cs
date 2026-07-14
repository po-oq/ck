using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;

namespace CodeKnowledge.Mcp.Tests;

public sealed class McpEndToEndTests : IClassFixture<PublishedServerFixture>, IDisposable
{
    // 発行済みEXEを実プロセスとして起動しstdio経由で通信する回帰ガード:
    // 修正前は標準入力の継承不備によりすべての呼び出しが30秒ハングしていた
    // (Task 12品質レビュー指摘)。30秒より十分短いタイムアウトを全呼び出しに
    // かけることで、そのハングが再発すればテストが失敗として検知する。
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

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

    private string DatabasePath => Path.Combine(_dbDirectory, "knowledge.db");

    private async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "code-knowledge",
            Command = _server.ExePath,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CODEKNOWLEDGE_DB_PATH"] = DatabasePath,
            },
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static Dictionary<string, object?> SaveArguments(string repositoryRoot) => new()
    {
        ["workingDirectory"] = repositoryRoot,
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
    };

    private static Dictionary<string, object?> ValidateArguments(
        string repositoryRoot, string knowledgeId, string? targetCommit = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["workingDirectory"] = repositoryRoot,
            ["knowledgeId"] = knowledgeId,
        };
        if (targetCommit is not null) arguments["targetCommit"] = targetCommit;
        return arguments;
    }

    private static Dictionary<string, object?> SaveTwoEvidenceArguments(string repositoryRoot)
    {
        var arguments = SaveArguments(repositoryRoot);
        arguments["evidence"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["filePath"] = "src/OrderService.cs",
                ["symbolName"] = "OrderService.Complete",
                ["symbolKind"] = "method",
                ["startLine"] = 1,
                ["endLine"] = 4,
            },
            new Dictionary<string, object?>
            {
                ["filePath"] = "src/MailSender.cs",
                ["symbolName"] = "MailSender.Send",
                ["symbolKind"] = "method",
                ["startLine"] = 1,
                ["endLine"] = 4,
            },
        };
        arguments["facts"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["text"] = "CompleteがMailSenderを利用する",
                ["evidenceIndexes"] = new[] { 0, 1 },
            },
        };
        return arguments;
    }

    private async Task<long> ReadVersionCountAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM knowledge_versions;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    [Fact]
    public async Task Lists_all_five_tools()
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);
        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        var names = tools.Select(tool => tool.Name).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "resolve_project", "search_knowledge", "get_knowledge", "save_knowledge",
            "validate_knowledge",
        }, names);
    }

    [Fact]
    public async Task Validate_knowledge_reports_valid_partial_and_stale_for_real_commits()
    {
        var baseCommit = _repo.CommitFile("src/MailSender.cs",
            "class MailSender\n{\n    void Send() { }\n}\n");
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);
        var save = await client.CallToolAsync(
            "save_knowledge", SaveTwoEvidenceArguments(_repo.Root),
            cancellationToken: cts.Token);
        Assert.NotEqual(true, save.IsError);
        var knowledgeId = JsonSerializer.SerializeToElement(save.StructuredContent)
            .GetProperty("knowledgeId").GetString()!;

        var valid = await client.CallToolAsync(
            "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId, baseCommit),
            cancellationToken: cts.Token);
        Assert.NotEqual(true, valid.IsError);
        var validJson = JsonSerializer.SerializeToElement(valid.StructuredContent);
        Assert.Equal("valid", validJson.GetProperty("status").GetString());
        Assert.Equal(baseCommit, validJson.GetProperty("targetCommit").GetString());
        Assert.False(validJson.GetProperty("isWorkingTreeDirty").GetBoolean());

        _repo.CommitFile("src/OrderService.cs",
            "class OrderService\n{\n    void Changed() { }\n}\n", "change first evidence");
        var partial = await client.CallToolAsync(
            "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId),
            cancellationToken: cts.Token);
        Assert.NotEqual(true, partial.IsError);
        var partialJson = JsonSerializer.SerializeToElement(partial.StructuredContent);
        Assert.Equal("partially_stale", partialJson.GetProperty("status").GetString());
        Assert.Equal(1, partialJson.GetProperty("changedEvidence").GetArrayLength());
        Assert.Equal(1, partialJson.GetProperty("unchangedEvidence").GetArrayLength());

        _repo.CommitFile("src/MailSender.cs",
            "class MailSender\n{\n    void Changed() { }\n}\n", "change second evidence");
        var stale = await client.CallToolAsync(
            "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId),
            cancellationToken: cts.Token);
        Assert.NotEqual(true, stale.IsError);
        var staleJson = JsonSerializer.SerializeToElement(stale.StructuredContent);
        Assert.Equal("stale", staleJson.GetProperty("status").GetString());
        Assert.Equal(2, staleJson.GetProperty("changedEvidence").GetArrayLength());
        Assert.Equal("reinvestigate_knowledge",
            staleJson.GetProperty("recommendedAction").GetString());
    }

    [Fact]
    public async Task Validate_knowledge_reports_dirty_without_persisting_a_version() // AC-25
    {
        var baseCommit = _repo.Run("rev-parse", "HEAD").Trim();
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);
        var save = await client.CallToolAsync(
            "save_knowledge", SaveArguments(_repo.Root), cancellationToken: cts.Token);
        Assert.NotEqual(true, save.IsError);
        var knowledgeId = JsonSerializer.SerializeToElement(save.StructuredContent)
            .GetProperty("knowledgeId").GetString()!;
        var before = await ReadVersionCountAsync(cts.Token);

        File.WriteAllText(Path.Combine(_repo.Root, "src", "OrderService.cs"),
            "class OrderService\n{\n    void Dirty() { }\n}\n");
        var validate = await client.CallToolAsync(
            "validate_knowledge", ValidateArguments(_repo.Root, knowledgeId, baseCommit),
            cancellationToken: cts.Token);
        Assert.NotEqual(true, validate.IsError);
        var json = JsonSerializer.SerializeToElement(validate.StructuredContent);
        Assert.Equal("valid", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("isWorkingTreeDirty").GetBoolean());
        Assert.Equal("inspect_dirty_evidence",
            json.GetProperty("recommendedAction").GetString());
        Assert.Equal(before, await ReadVersionCountAsync(cts.Token));
    }

    [Fact]
    public async Task Validate_knowledge_fails_outside_git_without_persisting()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"ck-validate-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            using var cts = new CancellationTokenSource(CallTimeout);
            await using var client = await ConnectAsync(cts.Token);
            var result = await client.CallToolAsync(
                "validate_knowledge", ValidateArguments(outside, "knowledge-1"),
                cancellationToken: cts.Token);
            Assert.True(result.IsError);
            var text = string.Join('\n', result.Content.Select(value => value.ToString()));
            Assert.Contains("git_repository_required: ", text);
            await using var connection = new SqliteConnection(
                $"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync(cts.Token);
            await using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM projects;";
            Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync(cts.Token)));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Save_search_get_roundtrip_works() // AC-02, AC-20, AC-27
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);

        var save = await client.CallToolAsync(
            "save_knowledge", SaveArguments(_repo.Root), cancellationToken: cts.Token);
        // 成功時、SDKはIsErrorをfalseへ明示的に設定せずnullのままにする。
        // Assert.False(bool?)はnullを失敗させるため、"trueではない"で判定する。
        Assert.NotEqual(true, save.IsError);

        var search = await client.CallToolAsync(
            "search_knowledge",
            new Dictionary<string, object?>
            {
                ["workingDirectory"] = _repo.Root,
                ["keywords"] = new[] { "メール", "仕様", "OrderCompleted" }, // 3文字語 + 2文字語（AC-20）
            },
            cancellationToken: cts.Token);
        Assert.NotEqual(true, search.IsError);
        var searchJson = JsonSerializer.SerializeToElement(search.StructuredContent);
        var first = searchJson.GetProperty("results")[0];
        Assert.Equal("注文完了メール仕様", first.GetProperty("title").GetString());
        // confidenceはワイヤ上、整数(0/1/2)ではなく小文字文字列("high")として現れる
        // (Confidence enumのJsonStringEnumConverter設定に依存)。
        Assert.Equal("high", first.GetProperty("confidence").GetString());
        var knowledgeId = first.GetProperty("knowledgeId").GetString()!;

        var get = await client.CallToolAsync(
            "get_knowledge",
            new Dictionary<string, object?>
            {
                ["workingDirectory"] = _repo.Root,
                ["knowledgeId"] = knowledgeId,
            },
            cancellationToken: cts.Token);
        Assert.NotEqual(true, get.IsError);
        var getJson = JsonSerializer.SerializeToElement(get.StructuredContent);
        Assert.Equal(1, getJson.GetProperty("facts").GetArrayLength());
        Assert.Equal("src/OrderService.cs",
            getJson.GetProperty("evidence")[0].GetProperty("filePath").GetString());
        // save→getラウンドトリップでも同じくconfidenceは小文字文字列で往復する。
        Assert.Equal("high", getJson.GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task Fails_outside_git_repository_without_persisting() // AC-13
    {
        var outside = Path.Combine(Path.GetTempPath(), $"ck-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            using var cts = new CancellationTokenSource(CallTimeout);
            await using var client = await ConnectAsync(cts.Token);
            var result = await client.CallToolAsync(
                "resolve_project",
                new Dictionary<string, object?> { ["workingDirectory"] = outside },
                cancellationToken: cts.Token);
            Assert.True(result.IsError);
            var text = string.Join('\n', result.Content.Select(content => content.ToString()));
            // エラー契約(design doc Deviations #4): McpExceptionのメッセージは
            // SDKが付ける可変プレフィックスの後ろに "<code>: " を部分文字列として含む。
            // 構造化{code, message}オブジェクトではないため、部分一致で照合する。
            Assert.Contains("git_repository_required: ", text);

            // AC-13の「永続化しない」を実データで検証する。DBファイル自体はサーバー
            // 起動時のmigration(Program.cs)がツール呼び出しと無関係に必ず作成するため、
            // File.Existsの否定では判定できない。正しい契約は「resolve_projectが失敗
            // した場合、projects行が1件も書かれない」(ResolveProjectUseCaseはGit解決が
            // 先に走り、失敗時はUpsertへ到達しない)なので、行数0を直接照合する。
            // Pooling=False: 既定の接続プールはDispose後もファイルハンドルを保持し、
            // テスト後の一時DBディレクトリ削除を妨げるため無効化する。
            await using var verification = new SqliteConnection(
                $"Data Source={DatabasePath};Pooling=False");
            await verification.OpenAsync(TestContext.Current.CancellationToken);
            await using var count = verification.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM projects;";
            Assert.Equal(0L, Convert.ToInt64(
                await count.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Save_knowledge_with_missing_required_field_surfaces_generic_client_error() // Task 12品質レビュー項目2
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        await using var client = await ConnectAsync(cts.Token);

        var arguments = SaveArguments(_repo.Root);
        arguments.Remove("evidence"); // 必須パラメータを欠落させる

        var result = await client.CallToolAsync(
            "save_knowledge", arguments, cancellationToken: cts.Token);

        Assert.True(result.IsError);
        var text = string.Join('\n', result.Content.Select(content => content.ToString()));
        // 既知の挙動: 必須引数の欠落はMCP SDKのパラメータバインディング段階で
        // 失敗し、ToolGuard（ドメインエラー→{code}: {message}変換）より手前で
        // 弾かれるため、クライアントには"An error occurred invoking 'save_knowledge'."
        // のような汎用メッセージしか届かず、code(例: invalid_arguments)は含まれない。
        // このテストは現状挙動を固定するものであり、改善はPhase 2候補として
        // ここでは追わない。
        Assert.Contains("save_knowledge", text);
        Assert.DoesNotContain("invalid_arguments: ", text);
    }

    [Fact]
    public async Task Concurrent_writer_holding_the_database_lock_surfaces_observed_behavior() // Task 12品質レビュー項目5
    {
        using (var warmupCts = new CancellationTokenSource(CallTimeout))
        await using (var warmupClient = await ConnectAsync(warmupCts.Token))
        {
            // migrationとDBファイル作成をウォームアップの保存で確実にしてから
            // 生のSqlite接続でロックを取得する。
            var warmupSave = await warmupClient.CallToolAsync(
                "save_knowledge", SaveArguments(_repo.Root), cancellationToken: warmupCts.Token);
            Assert.NotEqual(true, warmupSave.IsError);
        }

        // Pooling=False: 既定の接続プールはDispose後もファイルハンドルを保持し、
        // テスト後の一時DBディレクトリ削除を妨げるため無効化する。
        await using var lockConnection = new SqliteConnection(
            $"Data Source={DatabasePath};Mode=ReadWriteCreate;Pooling=False");
        await lockConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var pragma = lockConnection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            await pragma.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await using var lockTransaction = lockConnection.BeginTransaction(
            System.Data.IsolationLevel.Serializable); // BEGIN IMMEDIATE相当の書き込みロックを取得
        await using (var writeThroughLock = lockConnection.CreateCommand())
        {
            writeThroughLock.Transaction = lockTransaction;
            writeThroughLock.CommandText = "UPDATE projects SET display_name = display_name;";
            await writeThroughLock.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // サーバー側のbusy_timeoutは5秒(SqliteConnectionFactory)。ロックを保持したまま
        // 別プロセス(発行済みEXE)から書き込みを試みる。実測: PRAGMA busy_timeoutの
        // 公称値(5秒)よりかなり長く待ってからSQLITE_BUSYが返る(この開発機での測定値:
        // 約35〜45秒。負荷やマシン差で変動する)。WAL構成での他プロセスからの書き込み
        // 再試行の内部挙動によるものと見られるが、この差異自体の追調査(busy_timeout
        // チューニング)はタイムボックスしPhase 2へ委ねる。ここではdatabase_busyへ
        // 正しくマッピングされることのみを検証する。タイムアウト予算は実測値の
        // 2倍超(120秒)を意図的に確保し、CI等の遅い環境でのフレークを避ける。
        using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var writerClient = await ConnectAsync(writeCts.Token);
        var result = await writerClient.CallToolAsync(
            "save_knowledge", SaveArguments(_repo.Root), cancellationToken: writeCts.Token);

        lockTransaction.Rollback();

        Assert.True(result.IsError);
        var text = string.Join('\n', result.Content.Select(content => content.ToString()));
        Assert.Contains("database_busy: ", text);
    }

    public void Dispose()
    {
        _repo.Dispose();
        // 終了させたサーバープロセスのDBファイルハンドル解放は非同期のため、
        // 即時のDirectory.Deleteは競合して失敗し一時ディレクトリが残ることがある
        // (Task 13品質レビューで実測)。短いリトライで解放を待ってから静かに諦める。
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Directory.Delete(_dbDirectory, recursive: true);
                return;
            }
            catch when (attempt < 3)
            {
                Thread.Sleep(500);
            }
            catch
            {
                // 3回失敗したら静かに諦める(OSの一時領域なのでリークは致命的でない)
            }
        }
    }
}
