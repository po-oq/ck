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
    {
        var lines = fileContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // Don't count trailing empty element from terminal newline
        var lineCount = lines.Length;
        if (lineCount > 0 && lines[lineCount - 1] == "")
            lineCount--;

        var from = Math.Max(1, startLine);
        var to = Math.Min(lineCount, endLine);
        var normalized = new StringBuilder();
        for (var lineNumber = from; lineNumber <= to; lineNumber++)
        {
            var line = ConsecutiveSpaces().Replace(lines[lineNumber - 1], " ").TrimEnd();
            normalized.Append(line).Append('\n');
        }
        return ComputeFileHash(normalized.ToString());
    }
}
