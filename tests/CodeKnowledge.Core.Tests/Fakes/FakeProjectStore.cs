using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Tests.Fakes;

public sealed class FakeProjectStore : IProjectStore
{
    public Dictionary<string, Project> Projects { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> KnowledgeCounts { get; } = new(StringComparer.Ordinal);

    public Project? FindById(string projectId)
        => Projects.GetValueOrDefault(projectId);

    public IReadOnlyList<Project> FindStaleByRepositoryRoot(string repositoryRoot, string currentProjectId)
        => Projects.Values
            .Where(p =>
                string.Equals(p.RepositoryRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase) &&
                p.ProjectId != currentProjectId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();

    public void Upsert(Project project) => Projects[project.ProjectId] = project;

    public int CountKnowledge(string projectId) => KnowledgeCounts.GetValueOrDefault(projectId);
}
