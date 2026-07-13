using System.Text;
using System.Text.RegularExpressions;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Infrastructure.Git;

internal static partial class GitDiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeader();

    public static IReadOnlyList<GitFileChange> ParseChanges(byte[] raw, string patch)
    {
        var fields = Encoding.UTF8.GetString(raw).Split('\0');
        var changes = new List<GitFileChange>();
        for (var i = 0; i < fields.Length && fields[i].Length > 0;)
        {
            var status = fields[i++];
            var code = status[0];
            if (code is 'R' or 'C')
                changes.Add(new(code == 'R' ? GitChangeKind.Renamed : GitChangeKind.Copied,
                    fields[i++], fields[i++], []));
            else
            {
                var path = fields[i++];
                var kind = code switch
                {
                    'A' => GitChangeKind.Added,
                    'D' => GitChangeKind.Deleted,
                    _ => GitChangeKind.Modified,
                };
                changes.Add(new(kind, path, kind == GitChangeKind.Deleted ? null : path, []));
            }
        }

        var hunks = changes.Select(_ => new List<GitDiffHunk>()).ToList();
        var file = -1;
        foreach (var line in patch.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) { file++; continue; }
            var match = HunkHeader().Match(line);
            if (!match.Success || file < 0 || file >= hunks.Count) continue;
            static int Count(Group value) => value.Success ? int.Parse(value.Value) : 1;
            hunks[file].Add(new(
                int.Parse(match.Groups[1].Value), Count(match.Groups[2]),
                int.Parse(match.Groups[3].Value), Count(match.Groups[4])));
        }
        return changes.Select((change, index) => change with { Hunks = hunks[index] }).ToList();
    }

    public static IReadOnlySet<string> ParseWorkingTreePaths(byte[] raw)
    {
        var fields = Encoding.UTF8.GetString(raw).Split('\0');
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length && fields[i].Length > 0; i++)
        {
            var field = fields[i];
            if (field.Length < 4) continue;
            var status = field[..2];
            paths.Add(field[3..]);
            if ((status.Contains('R') || status.Contains('C')) && i + 1 < fields.Length)
                paths.Add(fields[++i]);
        }
        return paths;
    }
}
