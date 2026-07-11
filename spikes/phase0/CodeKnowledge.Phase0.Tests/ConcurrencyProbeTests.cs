using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0.Tests;

public sealed class ConcurrencyProbeTests
{
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
    public async Task Four_workers_write_two_hundred_unique_rows_without_lock_errors()
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
                    "--start-file", startFile
                ],
                workspace.Root,
                timeout.Token))
            .ToArray();

        await File.WriteAllTextAsync(startFile, "start", timeout.Token);
        var results = await Task.WhenAll(workers);

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

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        Assert.Equal(200L, ExecuteScalar(connection, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(200L, ExecuteScalar(connection, "SELECT COUNT(DISTINCT operation_id) FROM operations;"));
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
}
