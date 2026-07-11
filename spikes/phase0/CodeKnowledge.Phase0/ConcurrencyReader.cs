using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0;

internal static class ConcurrencyReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

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
            using var connection = OpenConnection(options.Database);
            var startedAt = Stopwatch.GetTimestamp();
            var consistentSearchesWhileWritesInProgress = 0;

            while (true)
            {
                if (Stopwatch.GetElapsedTime(startedAt) >= Timeout)
                {
                    throw new TimeoutException("Expected writers did not complete before the reader timeout.");
                }

                using var snapshot = connection.BeginTransaction(deferred: true);
                var registered = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM concurrency_workers;");
                if (registered < options.ExpectedWriters)
                {
                    snapshot.Commit();
                    await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
                    continue;
                }

                var active = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM concurrency_workers WHERE status = 'active';");
                var rowCount = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM operations;");
                var matchCount = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM operations_fts WHERE payload MATCH 'operation';");
                var likeCount = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM operations WHERE payload LIKE '%operation%';");
                if (active > 0 &&
                    rowCount > 0 &&
                    rowCount < options.ExpectedRows &&
                    matchCount == rowCount &&
                    likeCount == rowCount)
                {
                    consistentSearchesWhileWritesInProgress++;
                }

                var completed = ExecuteCount(
                    connection,
                    snapshot,
                    "SELECT COUNT(*) FROM concurrency_workers WHERE status = 'completed';");
                snapshot.Commit();
                if (completed == options.ExpectedWriters)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }

            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                mode = "concurrency-reader",
                status = "ok",
                consistentSearchesWhileWritesInProgress
            }));
            return ProbeExitCodes.Success;
        }
        catch (SqliteException exception)
        {
            await stderr.WriteLineAsync(
                $"concurrency_reader_failed: sqlite_error={exception.SqliteErrorCode} extended_error={exception.SqliteExtendedErrorCode}");
            return ProbeExitCodes.UnexpectedError;
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync(
                $"concurrency_reader_failed: exception={exception.GetType().Name}");
            return ProbeExitCodes.UnexpectedError;
        }
    }

    private static bool TryParseArguments(string[] args, out ReaderOptions options)
    {
        options = default!;
        if (args.Length != 8)
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

        if (values.Count != 4 ||
            !values.TryGetValue("--database", out var database) ||
            !values.TryGetValue("--expected-writers", out var expectedWritersText) ||
            !values.TryGetValue("--expected-rows", out var expectedRowsText) ||
            !values.TryGetValue("--start-file", out var startFile) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(startFile) ||
            !int.TryParse(expectedWritersText, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedWriters) ||
            expectedWriters <= 0 ||
            !int.TryParse(expectedRowsText, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedRows) ||
            expectedRows <= 0)
        {
            return false;
        }

        options = new(database, expectedWriters, expectedRows, startFile);
        return true;
    }

    private static async Task WaitForStartAsync(string startFile, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!File.Exists(startFile))
        {
            if (Stopwatch.GetElapsedTime(startedAt) >= Timeout)
            {
                throw new TimeoutException("Start signal was not created before the reader timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private static SqliteConnection OpenConnection(string database)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static long ExecuteCount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private sealed record ReaderOptions(
        string Database,
        int ExpectedWriters,
        int ExpectedRows,
        string StartFile);
}
