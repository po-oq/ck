namespace CodeKnowledge.Core.Projects;

/// <remarks>
/// Phase 1の既知の制限: Windowsドライブレター形式のローカルパスリモート
/// （例: C:\Backups\repo.git）は正規化ルールの対象外（scp形式として誤変換される）。
/// </remarks>
public static class RemoteUrlNormalizer
{
    /// <summary>要件5.3.2の8ルールを順に適用する。</summary>
    public static string Normalize(string url)
    {
        var value = url.Trim().Replace('\\', '/'); // ルール8: パス区切り統一

        // ルール2: スキーム除去
        var hadScheme = false;
        foreach (var scheme in new[] { "https://", "http://", "ssh://", "git://" })
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value[scheme.Length..];
                hadScheme = true;
                break;
            }
        }

        // ルール3の前半: 認証情報の区切りはパス開始（最初の/）より前の最後の@。
        // 未エンコードの@を含むユーザー名（user@corp.com:token@host）も全体を除去し、
        // パス中の@は区切りとして扱わない
        var pathStart = value.IndexOf('/');
        var hostAndAuth = pathStart < 0 ? value : value[..pathStart];
        var atIndex = hostAndAuth.LastIndexOf('@');
        var afterAt = atIndex >= 0 ? value[(atIndex + 1)..] : value;

        // IPv6ブラケットホスト（[2001:db8::1]）は]までをホストとして扱い、
        // :や/の探索は]より後ろから始める
        var searchStart = 0;
        if (afterAt.StartsWith('['))
        {
            var closingBracket = afterAt.IndexOf(']');
            if (closingBracket >= 0)
                searchStart = closingBracket + 1;
        }

        // ルール1: scp形式（git@host:path）をhost/pathへ変換
        // scp形式（スキームなし）ではホスト直後の:は常にパス区切りであり、ポートの概念はない。
        // ポート判定（host:NNNN/）はスキームが明示された場合にのみ適用する（ルール4）
        var colonIndex = afterAt.IndexOf(':', searchStart);
        var slashIndex = afterAt.IndexOf('/', searchStart);
        var isScpStyle = colonIndex > 0 &&
            (slashIndex < 0 || colonIndex < slashIndex) &&
            !(hadScheme && IsPortNumber(afterAt, colonIndex));
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
