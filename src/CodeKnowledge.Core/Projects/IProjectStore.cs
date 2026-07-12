using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Projects;

public interface IProjectStore
{
    Project? FindById(string projectId);
    Project? FindByRepositoryRoot(string repositoryRoot);
    void Upsert(Project project);
    int CountKnowledge(string projectId);
}
