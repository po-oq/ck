namespace CodeKnowledge.Core.Projects;

internal static class ProjectPathNormalizer
{
    public static string NormalizeForLocalId(string path)
    {
        var value = Trim(path).Replace('\\', '/');
        return IsWindows(path) ? value.ToLowerInvariant() : value;
    }

    public static string GetDisplayName(string path)
    {
        var value = Trim(path);
        var index = value.LastIndexOfAny(['/', '\\']);
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private static string Trim(string path)
    {
        var end = path.Length;
        while (end > 1 && path[end - 1] is '/' or '\\') end--;
        return path[..end];
    }

    private static bool IsWindows(string path)
        => path.Contains('\\') ||
           (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}
