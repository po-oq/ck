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
