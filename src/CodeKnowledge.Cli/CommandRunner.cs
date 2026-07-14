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
            "search" => services.GetRequiredService<SearchKnowledgeUseCase>()
                .Execute(
                    effectiveWorkingDirectory,
                    StringArray(input, "keywords"),
                    OptionalInt(input, "limit")),
            "get" => services.GetRequiredService<GetKnowledgeUseCase>()
                .Execute(
                    effectiveWorkingDirectory,
                    RequiredString(input, "knowledgeId"),
                    OptionalString(input, "versionId")),
            "save" => services.GetRequiredService<SaveKnowledgeUseCase>()
                .Execute(DeserializeSaveRequest(input, effectiveWorkingDirectory)),
            "validate" => services.GetRequiredService<ValidateKnowledgeUseCase>()
                .Execute(new ValidateKnowledgeRequest(
                    effectiveWorkingDirectory,
                    RequiredString(input, "knowledgeId"),
                    OptionalString(input, "targetCommit"))),
            _ => throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                $"Unknown subcommand: {subcommand}"),
        };
    }

    private static string? OptionalString(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string RequiredString(JsonElement input, string name)
        => OptionalString(input, name)
           ?? throw new CodeKnowledgeException(
               CodeKnowledgeException.InvalidArguments, $"'{name}' is required.");

    private static int? OptionalInt(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static IReadOnlyList<string> StringArray(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments, $"'{name}' must be a JSON array.");
        return value.EnumerateArray().Select(item => item.GetString() ?? "").ToList();
    }

    private static SaveKnowledgeRequest DeserializeSaveRequest(
        JsonElement input, string effectiveWorkingDirectory)
    {
        // save入力はSaveKnowledgeRequestの形と同じ(camelCase)なので直接デシリアライズし、
        // workingDirectoryだけ実効値で上書きする。改行はJSON文字列の\nとして復元される。
        var partial = input.Deserialize<SaveKnowledgeRequest>(CliJson.Options)
            ?? throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments, "save input must be a JSON object.");
        return partial with { WorkingDirectory = effectiveWorkingDirectory };
    }
}
