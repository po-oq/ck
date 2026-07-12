using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Knowledge;

public sealed record SaveEvidenceInput(
    string FilePath, string? SymbolId, string SymbolName, string? SymbolKind,
    string? Signature, int? StartLine, int? EndLine, string? Reason);

public sealed record SaveFactInput(string Text, IReadOnlyList<int> EvidenceIndexes);

public sealed record SaveInferenceInput(
    string Text, string Confidence, string Reason, IReadOnlyList<int> EvidenceIndexes);

public sealed record SaveRelationInput(string FromSymbol, string ToSymbol, string Kind);

public sealed record SaveKnowledgeRequest(
    string WorkingDirectory, string CanonicalKey, string Title,
    string OriginalQuestion, string Summary, string Confidence,
    string? Tags, string? CreatedBy, string? CommitHash,
    IReadOnlyList<SaveEvidenceInput> Evidence,
    IReadOnlyList<SaveFactInput> Facts,
    IReadOnlyList<SaveInferenceInput> Inferences,
    IReadOnlyList<SaveRelationInput> Relations);

public sealed record SaveKnowledgeResult(
    string ProjectId, string KnowledgeId, string VersionId,
    bool CreatedNewKnowledge, IReadOnlyList<KnowledgeSummary> SimilarKnowledge);

public sealed class SaveKnowledgeUseCase(
    ResolveProjectUseCase resolveProject,
    IGitRepository git,
    IKnowledgeStore store)
{
    public SaveKnowledgeResult Execute(SaveKnowledgeRequest request)
    {
        var resolution = resolveProject.Execute(request.WorkingDirectory);
        var commitHash = string.IsNullOrWhiteSpace(request.CommitHash)
            ? resolution.CurrentCommit
            : request.CommitHash;

        Validate(request);

        var evidence = BuildEvidence(request, resolution.RepositoryRoot, commitHash);
        var facts = request.Facts
            .Select(fact => new FactRecord(
                NewId(), fact.Text,
                fact.EvidenceIndexes.Select(index => evidence[index].Id).ToList()))
            .ToList();
        var inferences = request.Inferences
            .Select(inference =>
            {
                ConfidenceParser.TryParse(inference.Confidence, out var confidence);
                return new InferenceRecord(
                    NewId(), inference.Text, confidence, inference.Reason,
                    inference.EvidenceIndexes.Select(index => evidence[index].Id).ToList());
            })
            .ToList();
        var relations = request.Relations
            .Select(relation => new RelationRecord(
                NewId(), relation.FromSymbol, relation.ToSymbol, relation.Kind))
            .ToList();

        ConfidenceParser.TryParse(request.Confidence, out var overallConfidence);
        var saved = store.SaveVersion(new VersionToSave(
            resolution.ProjectId,
            request.CanonicalKey.Trim(),
            request.Title,
            commitHash,
            resolution.BranchName,
            request.OriginalQuestion,
            request.Summary,
            overallConfidence,
            request.Tags?.Trim() ?? "",
            request.CreatedBy?.Trim() ?? "",
            evidence, facts, inferences, relations));

        var similar = FindSimilar(resolution.ProjectId, saved.KnowledgeId,
            request.CanonicalKey.Trim(), request.Title);
        return new SaveKnowledgeResult(
            resolution.ProjectId, saved.KnowledgeId, saved.VersionId,
            saved.CreatedNewKnowledge, similar);
    }

    private void Validate(SaveKnowledgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CanonicalKey) ||
            string.IsNullOrWhiteSpace(request.Title))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "canonicalKey and title are required.");

        if (!ConfidenceParser.TryParse(request.Confidence, out _))
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "confidence must be one of: high, medium, low.");

        foreach (var inference in request.Inferences)
        {
            if (!ConfidenceParser.TryParse(inference.Confidence, out _))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    "inference confidence must be one of: high, medium, low.");
        }

        foreach (var relation in request.Relations)
        {
            if (!RelationKind.All.Contains(relation.Kind))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    $"Unknown relation kind: {relation.Kind}");
        }

        foreach (var fact in request.Facts)
        {
            if (fact.EvidenceIndexes.Count == 0)
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.FactRequiresEvidence,
                    $"Fact '{fact.Text}' does not reference any evidence.");
        }

        var allIndexes = request.Facts.SelectMany(fact => fact.EvidenceIndexes)
            .Concat(request.Inferences.SelectMany(inference => inference.EvidenceIndexes));
        foreach (var index in allIndexes)
        {
            if (index < 0 || index >= request.Evidence.Count)
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    $"Evidence index {index} is out of range.");
        }

        // データ整合性: startLine/endLineが両方指定されている場合、範囲が不正だと
        // ContentHasher.ComputeSymbolHashが黙って空文字列をハッシュしてしまう（Task 8で判明）。
        // 保存前に拒否することでハッシュの欠陥データが永続化されるのを防ぐ。
        foreach (var evidenceInput in request.Evidence)
        {
            if (evidenceInput.StartLine is { } start && evidenceInput.EndLine is { } end &&
                (start < 1 || end < start))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    $"Evidence line range is invalid: startLine={start}, endLine={end}.");
        }
    }

    private List<EvidenceRecord> BuildEvidence(
        SaveKnowledgeRequest request, string repositoryRoot, string commitHash)
    {
        var records = new List<EvidenceRecord>();
        foreach (var input in request.Evidence)
        {
            var relativePath = NormalizeFilePath(input.FilePath, repositoryRoot);
            var content = git.ReadFileAtCommit(repositoryRoot, commitHash, relativePath);
            var symbolHash = input.StartLine is { } start && input.EndLine is { } end
                ? ContentHasher.ComputeSymbolHash(content, start, end)
                : null;
            records.Add(new EvidenceRecord(
                NewId(), relativePath, input.SymbolId, input.SymbolName, input.SymbolKind,
                input.Signature, input.StartLine, input.EndLine, commitHash,
                ContentHasher.ComputeFileHash(content), symbolHash, input.Reason));
        }
        return records;
    }

    private IReadOnlyList<KnowledgeSummary> FindSimilar(
        string projectId, string savedKnowledgeId, string canonicalKey, string title)
        => store.ListSummaries(projectId)
            .Where(summary => summary.KnowledgeId != savedKnowledgeId)
            .Where(summary =>
                MutuallyContains(summary.CanonicalKey, canonicalKey) ||
                MutuallyContains(summary.Title, title))
            .ToList();

    private static bool MutuallyContains(string left, string right)
        => left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
           right.Contains(left, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeFilePath(string filePath, string repositoryRoot)
    {
        var normalized = filePath.Replace('\\', '/').Trim();
        var root = repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/";
        if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[root.Length..];
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");
}
