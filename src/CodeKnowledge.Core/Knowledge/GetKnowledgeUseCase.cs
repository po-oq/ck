using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Knowledge;

public sealed class GetKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IKnowledgeStore store)
{
    public KnowledgeDetail Execute(string workingDirectory, string knowledgeId, string? versionId)
    {
        var resolution = resolveProject.Execute(workingDirectory);
        return store.GetDetail(resolution.ProjectId, knowledgeId, versionId)
            ?? throw new CodeKnowledgeException(
                CodeKnowledgeException.KnowledgeNotFound,
                $"Knowledge '{knowledgeId}' was not found in project '{resolution.ProjectId}'.");
    }
}
