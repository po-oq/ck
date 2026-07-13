using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeKnowledge.Core.Hashing;

public static partial class ContentHasher
{
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex ConsecutiveSpaces();

    public static string ComputeFileHash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// 要件9.4段階2: 改行コード統一、行末空白除去、連続空白の縮約を行った
    /// startLine〜endLine（1始まり・両端含む）のテキストをハッシュする。
    /// </summary>
    public static string ComputeSymbolHash(string fileContent, int startLine, int endLine)
        => ComputeSymbolHash(NormalizeSymbolLines(fileContent), startLine, endLine);

    public static int CountLines(string content) => SplitLines(content).Length;

    internal static string[] NormalizeSymbolLines(string content)
    {
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
            lines[index] = ConsecutiveSpaces().Replace(lines[index], " ").TrimEnd();
        return lines;
    }

    internal static string ComputeSymbolHash(
        IReadOnlyList<string> normalizedLines, int startLine, int endLine)
    {
        var from = Math.Max(1, startLine);
        var to = Math.Min(normalizedLines.Count, endLine);
        var normalized = new StringBuilder();
        for (var lineNumber = from; lineNumber <= to; lineNumber++)
            normalized.Append(normalizedLines[lineNumber - 1]).Append('\n');
        return ComputeFileHash(normalized.ToString());
    }

    private static string[] SplitLines(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return lines.Length > 0 && lines[^1] == "" ? lines[..^1] : lines;
    }
}
