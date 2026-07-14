using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Validation;

internal sealed class EvidenceValidator(
    IGitRepository git, string repositoryRoot,
    string baseCommit, string targetCommit, GitCommitDiff diff)
{
    private readonly Dictionary<(string Commit, string Path), CachedFile> _files = [];

    public EvidenceValidationResult Validate(EvidenceRecord evidence, bool? dirty)
    {
        var label = Label(evidence);
        var targetPath = diff.ResolveTargetPath(evidence.FilePath);
        if (targetPath is null)
            return Result(EvidenceValidationStatus.Missing,
                ValidationReason.TargetFileMissing, null);

        var target = File(targetCommit, targetPath);
        if (target.Snapshot.Status == GitFileSnapshotStatus.Missing)
            return Result(EvidenceValidationStatus.Missing,
                ValidationReason.TargetFileMissing, targetPath);
        if (target.Snapshot.Status != GitFileSnapshotStatus.Available ||
            target.Snapshot.Content is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.TargetFileUnavailable, targetPath);
        if (string.Equals(target.FileHash, evidence.FileHash, StringComparison.Ordinal))
            return Result(EvidenceValidationStatus.Unchanged,
                ValidationReason.FileHashMatch, targetPath);
        if (evidence.SymbolHash is null || evidence.StartLine is null || evidence.EndLine is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.SymbolHashUnavailable, targetPath);

        var source = File(baseCommit, evidence.FilePath);
        if (source.Snapshot.Status != GitFileSnapshotStatus.Available ||
            source.Snapshot.Content is null)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.BaseFileUnavailable, targetPath);
        var effectiveEnd = Math.Min(source.LineCount, evidence.EndLine.Value);
        var windowLength = effectiveEnd - evidence.StartLine.Value + 1;
        if (windowLength < 1)
            return Result(EvidenceValidationStatus.Unknown,
                ValidationReason.SymbolHashUnavailable, targetPath);

        var mappedStart = diff.MapOldLineToNew(evidence.FilePath, evidence.StartLine.Value);
        var match = target.Locator!.Find(
            evidence.SymbolHash, windowLength, mappedStart);
        return match is null
            ? Result(EvidenceValidationStatus.Changed,
                ValidationReason.SymbolHashNotFound, targetPath)
            : Result(EvidenceValidationStatus.Unchanged, match.Reason, targetPath);

        EvidenceValidationResult Result(
            EvidenceValidationStatus status, ValidationReason reason, string? path)
            => new(evidence.Id, label, evidence.FilePath, path, status, reason, dirty);
    }

    private CachedFile File(string commit, string path)
    {
        var key = (commit, path);
        if (!_files.TryGetValue(key, out var value))
            _files[key] = value = new CachedFile(
                git.TryReadFileAtCommit(repositoryRoot, commit, path));
        return value;
    }

    internal static string Label(EvidenceRecord evidence)
    {
        if (!string.IsNullOrWhiteSpace(evidence.SymbolId)) return evidence.SymbolId;
        if (!string.IsNullOrWhiteSpace(evidence.Signature)) return evidence.Signature;
        return !string.IsNullOrWhiteSpace(evidence.SymbolName)
            ? evidence.SymbolName : evidence.FilePath;
    }

    private sealed class CachedFile
    {
        public CachedFile(GitFileSnapshot snapshot)
        {
            Snapshot = snapshot;
            if (snapshot.Content is null) return;
            FileHash = ContentHasher.ComputeFileHash(snapshot.Content);
            LineCount = ContentHasher.CountLines(snapshot.Content);
            Locator = new SymbolRangeLocator(snapshot.Content);
        }

        public GitFileSnapshot Snapshot { get; }
        public string? FileHash { get; }
        public int LineCount { get; }
        public SymbolRangeLocator? Locator { get; }
    }
}
