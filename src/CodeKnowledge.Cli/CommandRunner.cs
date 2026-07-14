using System.Text.Json;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using CodeKnowledge.Core.Validation;
using CodeKnowledge.Infrastructure.Database;
using CodeKnowledge.Infrastructure.Git;
using CodeKnowledge.Infrastructure.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CodeKnowledge.Cli;

public sealed class CommandRunner(IServiceProvider services)
{
    public static IServiceProvider BuildServices(SqliteConnectionFactory factory)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(factory);
        collection.AddSingleton<IGitRepository, GitCliRepository>();
        collection.AddSingleton<IProjectStore, SqliteProjectStore>();
        collection.AddSingleton<IKnowledgeStore, SqliteKnowledgeStore>();
        collection.AddSingleton<ResolveProjectUseCase>();
        collection.AddSingleton<SearchKnowledgeUseCase>();
        collection.AddSingleton<GetKnowledgeUseCase>();
        collection.AddSingleton<SaveKnowledgeUseCase>();
        collection.AddSingleton<ValidateKnowledgeUseCase>();
        return collection.BuildServiceProvider();
    }

    public object Run(string subcommand, string inputJson, string effectiveWorkingDirectory)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        var input = document.RootElement;

        return subcommand switch
        {
            "resolve" => services.GetRequiredService<ResolveProjectUseCase>()
                .Execute(effectiveWorkingDirectory),
            _ => throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Unknown subcommand: {subcommand}"),
        };
    }

    private static string? OptionalString(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
