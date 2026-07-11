using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0;

internal static class ConcurrencyWorker
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    public static Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        CancellationToken cancellationToken) =>
        RunAsync(args, stdout, TextWriter.Null, cancellationToken);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (!TryParseArguments(args, out var options))
        {
            return ProbeExitCodes.InvalidArguments;
        }

        try
        {
            await WaitForStartAsync(options.StartFile, cancellationToken);

            for (var sequence = 0; sequence < options.Iterations; sequence++)
            {
                WriteOperation(options.Database, options.WorkerId, sequence);
                if (options.DelayMilliseconds > 0)
                {
                    await Task.Delay(options.DelayMilliseconds, cancellationToken);
                }
            }

            SetWorkerStatus(options.Database, options.WorkerId, "completed");

            var result = JsonSerializer.Serialize(new
            {
                mode = "concurrency-worker",
                status = "ok",
                workerId = options.WorkerId,
                writes = options.Iterations
            });
            await stdout.WriteLineAsync(result);
            return ProbeExitCodes.Success;
        }
        catch (SqliteException exception)
        {
            await stderr.WriteLineAsync(
                $"concurrency_worker_failed: sqlite_error={exception.SqliteErrorCode} extended_error={exception.SqliteExtendedErrorCode}");
            return ProbeExitCodes.UnexpectedError;
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync(
                $"concurrency_worker_failed: exception={exception.GetType().Name}");
            return ProbeExitCodes.UnexpectedError;
        }
    }

    private static bool TryParseArguments(string[] args, out WorkerOptions options)
    {
        options = default!;
        if (args.Length is not (8 or 10))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!values.TryAdd(args[index], args[index + 1]))
            {
                return false;
            }
        }

        if (values.Count is not (4 or 5) ||
            !values.TryGetValue("--database", out var database) ||
            !values.TryGetValue("--worker-id", out var workerIdText) ||
            !values.TryGetValue("--iterations", out var iterationsText) ||
            !values.TryGetValue("--start-file", out var startFile) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(startFile) ||
            !int.TryParse(workerIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var workerId) ||
            workerId < 0 ||
            !int.TryParse(iterationsText, NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations <= 0 ||
            (values.TryGetValue("--delay-ms", out var delayText) &&
             (!int.TryParse(delayText, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
              int.Parse(delayText, CultureInfo.InvariantCulture) < 0)) ||
            values.Keys.Any(key => key is not ("--database" or "--worker-id" or "--iterations" or "--start-file" or "--delay-ms")))
        {
            return false;
        }

        var delayMilliseconds = values.TryGetValue("--delay-ms", out delayText)
            ? int.Parse(delayText, CultureInfo.InvariantCulture)
            : 0;
        options = new(database, workerId, iterations, startFile, delayMilliseconds);
        return true;
    }

    private static async Task WaitForStartAsync(string startFile, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!File.Exists(startFile))
        {
            if (Stopwatch.GetElapsedTime(startedAt) >= StartTimeout)
            {
                throw new TimeoutException($"Start signal was not created within {StartTimeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private static void WriteOperation(string database, int workerId, int sequence)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, "PRAGMA busy_timeout = 5000;");
        Execute(connection, "PRAGMA journal_mode = WAL;");

        using var transaction = connection.BeginTransaction();
        if (sequence == 0)
        {
            using var activate = connection.CreateCommand();
            activate.Transaction = transaction;
            activate.CommandText = """
                INSERT INTO concurrency_workers(worker_id, status)
                VALUES ($workerId, 'active');
                """;
            activate.Parameters.AddWithValue("$workerId", workerId);
            activate.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO operations(operation_id, worker_id, sequence, payload)
                VALUES ($operationId, $workerId, $sequence, $payload);
                """;
            insert.Parameters.AddWithValue("$operationId", $"{workerId:D2}-{sequence:D4}");
            insert.Parameters.AddWithValue("$workerId", workerId);
            insert.Parameters.AddWithValue("$sequence", sequence);
            insert.Parameters.AddWithValue("$payload", $"worker-{workerId:D2}-operation-{sequence:D4}");
            insert.ExecuteNonQuery();
        }

        using (var insertSearch = connection.CreateCommand())
        {
            insertSearch.Transaction = transaction;
            insertSearch.CommandText = """
                INSERT INTO operations_fts(operation_id, payload)
                VALUES ($operationId, $payload);
                """;
            insertSearch.Parameters.AddWithValue("$operationId", $"{workerId:D2}-{sequence:D4}");
            insertSearch.Parameters.AddWithValue("$payload", $"worker-{workerId:D2}-operation-{sequence:D4}");
            insertSearch.ExecuteNonQuery();
        }

        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM operations;";
            _ = count.ExecuteScalar();
        }

        transaction.Commit();
    }

    private static void SetWorkerStatus(string database, int workerId, string status)
    {
        using var connection = OpenConnection(database);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE concurrency_workers SET status = $status WHERE worker_id = $workerId;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$workerId", workerId);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection(string database)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout = 5000;");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record WorkerOptions(
        string Database,
        int WorkerId,
        int Iterations,
        string StartFile,
        int DelayMilliseconds);
}
