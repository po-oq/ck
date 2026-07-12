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
