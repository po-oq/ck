using System.Text;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Core.Search;

public sealed record PreparedKeywords(
    IReadOnlyList<string> FtsKeywords,
    IReadOnlyList<string> LikeKeywords,
    string? FtsMatchExpression);

public static class KeywordPreparation
{
    public static PreparedKeywords Prepare(IReadOnlyList<string> keywords)
    {
        var normalized = keywords
            .Select(keyword => StripControlCharacters(
                keyword.Normalize(NormalizationForm.FormKC)).Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count == 0)
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InvalidArguments,
                "No valid keywords remained after normalization.");

        var fts = new List<string>();
        var like = new List<string>();
        foreach (var keyword in normalized)
        {
            // 要件8.3はUnicodeコードポイント数で判定する
            var codePoints = keyword.EnumerateRunes().Count();
            (codePoints >= 3 ? fts : like).Add(keyword);
        }

        var matchExpression = fts.Count == 0
            ? null
            : string.Join(" OR ", fts.Select(k => $"\"{k.Replace("\"", "\"\"")}\""));
        return new PreparedKeywords(fts, like, matchExpression);
    }

    private static string StripControlCharacters(string keyword)
    {
        // 要件8.4: C0制御文字（U+0000〜U+001F）とDEL（U+007F）はFTS MATCH式を壊すため除去する
        var builder = new StringBuilder(keyword.Length);
        foreach (var ch in keyword)
        {
            if (ch is <= '\u001F' or '\u007F')
                continue;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    public static string EscapeLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        return $"%{escaped}%";
    }
}
