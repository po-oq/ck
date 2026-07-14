using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Core.Validation;
using CodeKnowledge.Infrastructure.Database;
using CodeKnowledge.Infrastructure.Git;
using CodeKnowledge.Infrastructure.Stores;
using CodeKnowledge.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var databasePath = DatabasePathResolver.Resolve();
var connectionFactory = new SqliteConnectionFactory(databasePath);
// DBロック待ちと「固まっている」をオペレーターが区別できるよう、移行前に一行だけ通知する
await Console.Error.WriteLineAsync($"codeknowledge: applying migrations to {databasePath}");
try
{
    MigrationRunner.Apply(connectionFactory, databasePath);
}
catch (CodeKnowledgeException exception)
{
    await Console.Error.WriteLineAsync($"{exception.Code}: {exception.Message}");
    return 1;
}
catch (Exception exception)
{
    // 生のスタックトレースではなく {code}: {message} + 非ゼロ終了の契約を守る
    await Console.Error.WriteLineAsync(
        $"{CodeKnowledgeException.InternalError}: {exception.GetType().Name}: {exception.Message}");
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
builder.Services.AddSingleton<ValidateKnowledgeUseCase>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeKnowledgeTools>();

await builder.Build().RunAsync();
return 0;
