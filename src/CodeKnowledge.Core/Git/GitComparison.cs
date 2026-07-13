namespace CodeKnowledge.Core.Git;

public enum GitFileSnapshotStatus { Available, Missing, Unavailable }
public enum GitChangeKind { Added, Modified, Deleted, Renamed, Copied }

public sealed record GitFileSnapshot(GitFileSnapshotStatus Status, string? Content)
{
    public static GitFileSnapshot Available(string content) =>
        new(GitFileSnapshotStatus.Available, content);
    public static GitFileSnapshot Missing() => new(GitFileSnapshotStatus.Missing, null);
    public static GitFileSnapshot Unavailable() => new(GitFileSnapshotStatus.Unavailable, null);
}

public sealed record GitDiffHunk(int OldStart, int OldCount, int NewStart, int NewCount);

public sealed record GitFileChange(
    GitChangeKind Kind, string OldPath, string? NewPath, IReadOnlyList<GitDiffHunk> Hunks)
{
    public string? TargetPath => Kind == GitChangeKind.Deleted ? null : NewPath;

    public int MapOldLineToNew(int oldLine)
    {
        var delta = 0;
        foreach (var hunk in Hunks.OrderBy(value => value.OldStart))
        {
            if (oldLine < hunk.OldStart) break;
            if (hunk.OldCount == 0)
            {
                if (oldLine > hunk.OldStart) delta += hunk.NewCount;
                continue;
            }
            if (oldLine <= hunk.OldStart + hunk.OldCount - 1)
                return Math.Max(1, hunk.NewStart);
            delta += hunk.NewCount - hunk.OldCount;
        }
        return Math.Max(1, oldLine + delta);
    }
}

public sealed record GitCommitDiff(IReadOnlyList<GitFileChange> Files)
{
    public string? ResolveTargetPath(string originalPath)
    {
        var change = Find(originalPath);
        return change is null ? originalPath : change.TargetPath;
    }
    public int MapOldLineToNew(string originalPath, int oldLine)
        => Find(originalPath)?.MapOldLineToNew(oldLine) ?? oldLine;
    private GitFileChange? Find(string path) => Files.FirstOrDefault(change =>
        string.Equals(change.OldPath, path, StringComparison.Ordinal));
}
