using System.ComponentModel;
using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Search;
using ModelContextProtocol.Server;

namespace CodeKnowledge.Mcp.Tools;

[McpServerToolType]
public sealed class CodeKnowledgeTools(
    ResolveProjectUseCase resolveProject,
    SearchKnowledgeUseCase searchKnowledge,
    GetKnowledgeUseCase getKnowledge,
    SaveKnowledgeUseCase saveKnowledge)
{
    [McpServerTool(Name = "resolve_project", UseStructuredContent = true),
        Description("Resolves the current Git repository into a Code Knowledge project. " +
            "Call before assuming any knowledge scope. Fails outside a Git repository.")]
    public ProjectResolution ResolveProject(
        [Description("Absolute path of the current working directory.")] string workingDirectory)
        => ToolGuard.Execute(() => resolveProject.Execute(workingDirectory));

    [McpServerTool(Name = "search_knowledge", UseStructuredContent = true),
        Description("Searches saved knowledge in the current project with hybrid FTS/LIKE search. " +
            "Expand keywords aggressively: nouns and compound words from the question, English " +
            "translations (mail, spec), guessed symbol names (EmailSender, OrderCompleted). " +
            "Prefer keywords of 3+ characters; 1-2 character keywords are also matched by substring.")]
    public SearchKnowledgeResult SearchKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Expanded search keywords.")] IReadOnlyList<string> keywords,
        [Description("Maximum results (default 10, max 50).")] int? limit = null)
        => ToolGuard.Execute(() => searchKnowledge.Execute(workingDirectory, keywords, limit));

    [McpServerTool(Name = "get_knowledge", UseStructuredContent = true),
        Description("Gets the current (or a specific) version of a knowledge entry with facts, " +
            "inferences, evidence, and relations.")]
    public KnowledgeDetail GetKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Knowledge id from search results.")] string knowledgeId,
        [Description("Optional specific version id. Omit for the current version.")] string? versionId = null)
        => ToolGuard.Execute(() => getKnowledge.Execute(workingDirectory, knowledgeId, versionId));

    [McpServerTool(Name = "save_knowledge", UseStructuredContent = true),
        Description("Saves an investigation result as knowledge. Only call after the user " +
            "explicitly asked to save or agreed to a save proposal. Facts must reference evidence; " +
            "put uncertain content into inferences. confidence: 'high' = evidence read directly and " +
            "consistent across implementation/callers/tests; 'medium' = main evidence read but " +
            "surroundings unverified; 'low' = mostly guessed from naming/conventions.")]
    public SaveKnowledgeResult SaveKnowledge(
        [Description("Absolute path of the current working directory.")] string workingDirectory,
        [Description("Stable key for the topic, e.g. domain.mail.order-completed.")] string canonicalKey,
        [Description("Human readable title.")] string title,
        [Description("The original user question that triggered the investigation.")] string originalQuestion,
        [Description("Summary of the findings.")] string summary,
        [Description("Overall confidence: high, medium, or low.")] string confidence,
        [Description("Evidence code locations. Paths may be absolute or repo-relative.")]
            IReadOnlyList<SaveEvidenceInput> evidence,
        [Description("Facts directly confirmed from code. Each must reference evidence by index.")]
            IReadOnlyList<SaveFactInput> facts,
        [Description("Inferences with their own confidence and reason.")]
            IReadOnlyList<SaveInferenceInput> inferences,
        [Description("Symbol relations discovered during the investigation.")]
            IReadOnlyList<SaveRelationInput> relations,
        [Description("Space separated tags.")] string? tags = null,
        [Description("Agent name for created_by.")] string? createdBy = null,
        [Description("Commit the investigation was performed at. Omit to use HEAD.")] string? commitHash = null)
        => ToolGuard.Execute(() => saveKnowledge.Execute(new SaveKnowledgeRequest(
            workingDirectory, canonicalKey, title, originalQuestion, summary, confidence,
            tags, createdBy, commitHash, evidence, facts, inferences, relations)));
}
