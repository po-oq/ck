using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0.Tests;

public sealed class ConcurrencyProbeTests
{
    [Fact]
    public async Task Process_runner_cancellation_terminates_the_waiting_child()
    {
        using var workspace = new TestWorkspace();
        var databasePath = workspace.PathFor("cancel.db");
        CreateOperationsTable(databasePath);
        var missingStartFile = workspace.PathFor("never-created.signal");
        var started = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var run = ProcessRunner.RunAsync(
            "dotnet",
            [
                typeof(CommandLine).Assembly.Location,
                "concurrency-worker",
                "--database", databasePath,
                "--worker-id", "99",
                "--iterations", "1",
                "--start-file", missingStartFile
            ],
            workspace.Root,
            cancellation.Token,
            processId => started.TrySetResult(processId));

        var processId = await started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var childWasStillRunning = false;
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
            childWasStillRunning = IsProcessRunning(processId);
        }
        finally
        {
            KillProcessTreeIfRunning(processId);
        }

        Assert.False(childWasStillRunning, $"Child process {processId} survived cancellation.");
    }

    [Fact]
    public async Task Worker_rejects_incomplete_duplicate_or_unknown_arguments()
    {
        string[][] invalidArguments =
        [
            ["--database", "probe.db"],
            [
                "--database", "probe.db",
                "--worker-id", "1",
                "--iterations", "50",
                "--start-file", "start.signal",
                "--database", "other.db"
            ],
            [
                "--database", "probe.db",
                "--worker-id", "1",
                "--iterations", "50",
                "--unknown", "start.signal"
            ]
        ];

        foreach (var arguments in invalidArguments)
        {
            var exitCode = await ConcurrencyWorker.RunAsync(
                arguments,
                TextWriter.Null,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProbeExitCodes.InvalidArguments, exitCode);
        }
    }

    [Fact]
    public async Task Worker_reports_a_stable_diagnostic_when_a_database_operation_fails()
    {
        using var workspace = new TestWorkspace();
        var databasePath = workspace.PathFor("invalid-schema.db");
        var startFile = workspace.PathFor("start.signal");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
        }
        await File.WriteAllTextAsync(startFile, "start", TestContext.Current.CancellationToken);

        var result = await ProcessRunner.RunAsync(
            "dotnet",
            [
                typeof(CommandLine).Assembly.Location,
                "concurrency-worker",
                "--database", databasePath,
                "--worker-id", "1",
                "--iterations", "1",
                "--start-file", startFile
            ],
            workspace.Root,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProbeExitCodes.UnexpectedError, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(
            "concurrency_worker_failed: sqlite_error=1 extended_error=1",
            result.StandardError.Trim());
    }

    [Fact]
    public async Task Writers_and_reader_search_and_save_concurrently_without_lock_errors()
    {
        using var workspace = new TestWorkspace();
        var databasePath = workspace.PathFor("concurrency.db");
        var startFile = workspace.PathFor("start.signal");
        CreateOperationsTable(databasePath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var assemblyPath = typeof(CommandLine).Assembly.Location;
        var workers = Enumerable.Range(1, 4)
            .Select(workerId => ProcessRunner.RunAsync(
                "dotnet",
                [
                    assemblyPath,
                    "concurrency-worker",
                    "--database", databasePath,
                    "--worker-id", workerId.ToString(),
                    "--iterations", "50",
                    "--start-file", startFile,
                    "--delay-ms", "5"
                ],
                workspace.Root,
                timeout.Token))
            .ToArray();
        var reader = ProcessRunner.RunAsync(
            "dotnet",
            [
                assemblyPath,
                "concurrency-reader",
                "--database", databasePath,
                "--expected-writers", "4",
                "--expected-rows", "200",
                "--start-file", startFile
            ],
            workspace.Root,
            timeout.Token);

        await File.WriteAllTextAsync(startFile, "start", timeout.Token);
        var results = await Task.WhenAll(workers);
        var readerResult = await reader;

        foreach (var (result, index) in results.Select((result, index) => (result, index)))
        {
            Assert.Equal(0, result.ExitCode);
            Assert.False(
                (result.StandardOutput + result.StandardError).Contains(
                    "database is locked",
                    StringComparison.OrdinalIgnoreCase),
                $"Worker {index + 1} reported a lock error: {result.StandardError}");

            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.Equal("concurrency-worker", root.GetProperty("mode").GetString());
            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.Equal(index + 1, root.GetProperty("workerId").GetInt32());
            Assert.Equal(50, root.GetProperty("writes").GetInt32());
        }

        Assert.Equal(0, readerResult.ExitCode);
        Assert.False(
            (readerResult.StandardOutput + readerResult.StandardError).Contains(
                "database is locked",
                StringComparison.OrdinalIgnoreCase),
            $"Reader reported a lock error: {readerResult.StandardError}");
        using (var readerDocument = JsonDocument.Parse(readerResult.StandardOutput))
        {
            var root = readerDocument.RootElement;
            Assert.Equal("concurrency-reader", root.GetProperty("mode").GetString());
            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.True(
                root.GetProperty("consistentSearchesWhileWritesInProgress").GetInt32() > 0);
            Assert.False(root.TryGetProperty("searchesWhileWritersActive", out _));
            Assert.False(root.TryGetProperty("searchesBeforeAllRowsSaved", out _));
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        Assert.Equal(200L, ExecuteScalar(connection, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(200L, ExecuteScalar(connection, "SELECT COUNT(DISTINCT operation_id) FROM operations;"));
        Assert.Equal(4L, ExecuteScalar(connection, "SELECT COUNT(DISTINCT worker_id) FROM operations;"));
        Assert.Equal(0L, ExecuteScalar(connection, """
            SELECT COUNT(*) FROM operations
            WHERE worker_id NOT BETWEEN 1 AND 4 OR sequence NOT BETWEEN 0 AND 49;
            """));
        Assert.Equal(0L, ExecuteScalar(connection, """
            SELECT COUNT(*) FROM (
                SELECT worker_id
                FROM operations
                GROUP BY worker_id
                HAVING COUNT(*) <> 50 OR COUNT(DISTINCT sequence) <> 50
            );
            """));
        Assert.Equal(200L, ExecuteScalar(connection, "SELECT COUNT(*) FROM operations_fts;"));
        Assert.Equal(0L, ExecuteScalar(connection, """
            SELECT COUNT(*)
            FROM operations AS o
            LEFT JOIN operations_fts AS f ON f.operation_id = o.operation_id
            WHERE f.operation_id IS NULL OR f.payload <> o.payload;
            """));
        Assert.Equal(0L, ExecuteScalar(connection, """
            SELECT COUNT(*)
            FROM operations_fts AS f
            LEFT JOIN operations AS o ON o.operation_id = f.operation_id
            WHERE o.operation_id IS NULL;
            """));
        Assert.Equal(4L, ExecuteScalar(connection, "SELECT COUNT(*) FROM concurrency_workers WHERE status = 'completed';"));
        Assert.Equal(4L, ExecuteScalar(connection, "SELECT COUNT(*) FROM operations_fts WHERE payload MATCH '\"operation-0001\"';"));
        Assert.Equal(4L, ExecuteScalar(connection, "SELECT COUNT(*) FROM operations WHERE payload LIKE '%operation-0001%';"));
        Assert.Equal(0L, ExecuteScalar(connection, """
            SELECT COUNT(*) FROM operations
            WHERE operation_id <> printf('%02d-%04d', worker_id, sequence)
               OR payload <> printf('worker-%02d-operation-%04d', worker_id, sequence);
            """));
        Assert.Equal("wal", ExecuteTextScalar(connection, "PRAGMA journal_mode;"));
    }

    private static void CreateOperationsTable(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE operations(
                operation_id TEXT PRIMARY KEY,
                worker_id INTEGER NOT NULL,
                sequence INTEGER NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE operations_fts USING fts5(
                operation_id UNINDEXED,
                payload,
                tokenize = 'trigram'
            );
            CREATE TABLE concurrency_workers(
                worker_id INTEGER PRIMARY KEY,
                status TEXT NOT NULL CHECK(status IN ('active', 'completed'))
            );
            """;
        command.ExecuteNonQuery();
    }

    private static long ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static string ExecuteTextScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void KillProcessTreeIfRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (ArgumentException)
        {
            // The child already exited.
        }
    }
}
