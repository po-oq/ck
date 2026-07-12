using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Projects;

public interface IProjectStore
{
    Project? FindById(string projectId);

    /// <summary>
    /// 同じrepository_rootを持ちproject_idが現在のIDと異なる行をすべて返す（要件5.8.2）。
    /// 複数回のID変遷で残った旧プロジェクトを漏れなく警告するため、updated_at降順で返す。
    /// </summary>
    IReadOnlyList<Project> FindStaleByRepositoryRoot(string repositoryRoot, string currentProjectId);

    void Upsert(Project project);
    int CountKnowledge(string projectId);
}
