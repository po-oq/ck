namespace CodeKnowledge.Core.Projects;

public static class RemoteUrlNormalizer
{
    /// <summary>要件5.3.2の8ルールを順に適用する。</summary>
    public static string Normalize(string url)
    {
        var value = url.Trim().Replace('\\', '/'); // ルール8: パス区切り統一

        // ルール2: スキーム除去
        foreach (var scheme in new[] { "https://", "http://", "ssh://", "git://" })
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value[scheme.Length..];
                break;
            }
        }

        // ルール1: scp形式（git@host:path）をhost/pathへ変換
        // 認証情報除去より先に判定する。「@より後ろで最初の:が/より前」ならscp形式
        var atIndex = value.IndexOf('@');
        var afterAt = atIndex >= 0 ? value[(atIndex + 1)..] : value;
        var colonIndex = afterAt.IndexOf(':');
        var slashIndex = afterAt.IndexOf('/');
        var isScpStyle = colonIndex > 0 &&
            (slashIndex < 0 || colonIndex < slashIndex) &&
            !IsPortNumber(afterAt, colonIndex);
        if (isScpStyle)
            afterAt = afterAt[..colonIndex] + "/" + afterAt[(colonIndex + 1)..];

        // ルール3: 認証情報除去（@より前を捨てる）
        value = afterAt;

        // ルール5・6: .gitサフィックスと末尾スラッシュの除去
        value = value.TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        value = value.TrimEnd('/');

        // ルール7: 小文字化（ルール4: ポートは保持される）
        return value.ToLowerInvariant();
    }

    private static bool IsPortNumber(string value, int colonIndex)
    {
        var rest = value[(colonIndex + 1)..];
        var end = rest.IndexOf('/');
        var candidate = end < 0 ? rest : rest[..end];
        return candidate.Length > 0 && candidate.All(char.IsAsciiDigit);
    }
}
