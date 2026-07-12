using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Projects;

public sealed record ProjectWarning(string Code, string? PreviousProjectId, int KnowledgeCount);

public sealed record ProjectResolution(
    string ProjectId,
    string ProjectIdSource,
    string DisplayName,
    string RepositoryRoot,
    string? RemoteUrl,
    string CurrentCommit,
    string? BranchName,
    IReadOnlyList<ProjectWarning> Warnings);

public sealed class ResolveProjectUseCase(IGitRepository git, IProjectStore store)
{
    public ProjectResolution Execute(string workingDirectory)
    {
        var context = git.ResolveContext(workingDirectory);
        var identity = ProjectIdResolver.Resolve(context);

        var warnings = new List<ProjectWarning>();
        foreach (var stale in store.FindStaleByRepositoryRoot(context.RepositoryRoot, identity.ProjectId))
        {
            warnings.Add(new ProjectWarning(
                "project_id_changed", stale.ProjectId, store.CountKnowledge(stale.ProjectId)));
        }

        var now = DateTimeOffset.UtcNow;
        var existing = store.FindById(identity.ProjectId);
        store.Upsert(new Project(
            identity.ProjectId,
            identity.DisplayName,
            context.RepositoryRoot,
            identity.NormalizedRemoteUrl,
            existing?.CreatedAt ?? now,
            now));

        return new ProjectResolution(
            identity.ProjectId,
            identity.Source,
            identity.DisplayName,
            context.RepositoryRoot,
            identity.NormalizedRemoteUrl,
            context.HeadCommit,
            context.BranchName,
            warnings);
    }
}
