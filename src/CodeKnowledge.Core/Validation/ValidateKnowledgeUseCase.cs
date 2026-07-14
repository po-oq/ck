using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Validation;

public sealed class ValidateKnowledgeUseCase(
    ResolveProjectUseCase resolveProject, IGitRepository git, IKnowledgeStore store)
{
    public ValidateKnowledgeResult Execute(ValidateKnowledgeRequest request)
    {
        ValidateArguments(request);
        var project = resolveProject.Execute(request.WorkingDirectory);
        var detail = store.GetDetail(project.ProjectId, request.KnowledgeId, versionId: null)
            ?? throw new CodeKnowledgeException(CodeKnowledgeException.KnowledgeNotFound,
                $"Knowledge '{request.KnowledgeId}' was not found in project '{project.ProjectId}'.");
        var requestedTarget = request.TargetCommit?.Trim();
        var baseCommit = git.ResolveCommit(project.RepositoryRoot, detail.CommitHash);
        var targetCommit = requestedTarget is null
            ? project.CurrentCommit
            : git.ResolveCommit(project.RepositoryRoot, requestedTarget);
        if (baseCommit is null)
            return Unavailable(detail, targetCommit, ValidationReason.CommitUnavailable,
                $"base_commit_unavailable: {detail.CommitHash}");
        if (targetCommit is null)
            return Unavailable(detail, null, ValidationReason.CommitUnavailable,
                $"target_commit_unavailable: {requestedTarget}");

        var targetDiff = git.CompareCommits(project.RepositoryRoot, baseCommit, targetCommit);
        if (targetDiff is null)
            return Unavailable(detail, targetCommit, ValidationReason.DiffUnavailable,
                "diff_unavailable");
        var headDiff = string.Equals(targetCommit, project.CurrentCommit, StringComparison.Ordinal)
            ? targetDiff
            : git.CompareCommits(project.RepositoryRoot, baseCommit, project.CurrentCommit);
        var changedPaths = git.GetWorkingTreeChangedPaths(project.RepositoryRoot);
        var dirtyAvailable = headDiff is not null && changedPaths is not null;

        var validator = new EvidenceValidator(
            git, project.RepositoryRoot, baseCommit, targetCommit, targetDiff);
        var details = detail.Evidence.Select(evidence =>
        {
            bool? dirty = null;
            if (dirtyAvailable)
            {
                var currentPath = headDiff!.ResolveTargetPath(evidence.FilePath);
                dirty = changedPaths!.Contains(evidence.FilePath) ||
                    currentPath is not null && changedPaths.Contains(currentPath);
            }
            return validator.Validate(evidence, dirty);
        }).ToList();

        bool? anyDirty = dirtyAvailable
            ? details.Any(value => value.IsWorkingTreeDirty == true)
            : null;
        var decision = ValidationDecision.Decide(
            details.Select(value => value.Status).ToList(), anyDirty);
        IReadOnlyList<string> warnings = dirtyAvailable ? [] : ["dirty_check_unavailable"];
        return Build(baseCommit, targetCommit, anyDirty, details, decision, warnings);
    }

    private static void ValidateArguments(ValidateKnowledgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(request.KnowledgeId) ||
            request.TargetCommit is not null && string.IsNullOrWhiteSpace(request.TargetCommit))
            throw new CodeKnowledgeException(CodeKnowledgeException.InvalidArguments,
                "workingDirectory and knowledgeId are required; targetCommit cannot be blank.");
    }

    private static ValidateKnowledgeResult Unavailable(
        KnowledgeDetail detail, string? targetCommit, ValidationReason reason, string warning)
    {
        var details = detail.Evidence.Select(evidence => new EvidenceValidationResult(
            evidence.Id, EvidenceValidator.Label(evidence), evidence.FilePath, null,
            EvidenceValidationStatus.Unknown, reason, null)).ToList();
        return Build(detail.CommitHash, targetCommit, null, details,
            new(ValidationStatus.Unknown, RecommendedAction.InspectEvidence), [warning]);
    }

    private static ValidateKnowledgeResult Build(
        string baseCommit, string? targetCommit, bool? dirty,
        IReadOnlyList<EvidenceValidationResult> details,
        ValidationDecisionResult decision, IReadOnlyList<string> warnings)
    {
        IReadOnlyList<string> Labels(EvidenceValidationStatus status) =>
            details.Where(value => value.Status == status).Select(value => value.Label).ToList();
        return new(
            decision.Status, baseCommit, targetCommit, dirty,
            Labels(EvidenceValidationStatus.Changed),
            Labels(EvidenceValidationStatus.Unchanged),
            Labels(EvidenceValidationStatus.Missing),
            Labels(EvidenceValidationStatus.Unknown),
            details.Where(value => value.IsWorkingTreeDirty == true)
                .Select(value => value.Label).ToList(),
            details, decision.RecommendedAction, warnings);
    }
}
