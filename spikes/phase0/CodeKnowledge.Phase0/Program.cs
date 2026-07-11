using System.Text.Json;
using CodeKnowledge.Phase0;
using Microsoft.Data.Sqlite;

var selection = CommandLine.Parse(args);
try
{
    return selection.Mode switch
    {
        ProbeMode.Mcp => await McpServerRunner.RunAsync(CancellationToken.None),
        ProbeMode.SelfCheck => RunSelfCheck(selection.Arguments),
        ProbeMode.ConcurrencyWorker => await ConcurrencyWorker.RunAsync(
            selection.Arguments,
            Console.Out,
            Console.Error,
            CancellationToken.None),
        ProbeMode.ConcurrencyReader => await ConcurrencyReader.RunAsync(
            selection.Arguments,
            Console.Out,
            Console.Error,
            CancellationToken.None),
        _ => InvalidMode(args)
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"unexpected_error: {exception.GetType().Name}: {exception.Message}");
    return ProbeExitCodes.UnexpectedError;
}

static int RunSelfCheck(string[] modeArguments)
{
    if (modeArguments.Length != 0)
        return ProbeExitCodes.InvalidArguments;

    var path = Path.Combine(Path.GetTempPath(), $"ck-phase0-{Guid.NewGuid():N}.db");
    try
    {
        var report = SqliteProbe.Run(path);
        Console.Out.WriteLine(JsonSerializer.Serialize(report));
        return report.Status == "ok"
            ? ProbeExitCodes.Success
            : ProbeExitCodes.CheckFailed;
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        SelfCheckDatabaseCleanup.DeleteCandidates(path);
    }
}

static int InvalidMode(string[] modeArguments)
{
    Console.Error.WriteLine(
        $"invalid_arguments: unsupported mode '{string.Join(' ', modeArguments)}'");
    return ProbeExitCodes.InvalidArguments;
}
