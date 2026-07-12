using System.Security.Cryptography;
using System.Text;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;

namespace CodeKnowledge.Core.Projects;

public sealed record ProjectIdentity(
    string ProjectId,
    string Source,
    string? NormalizedRemoteUrl,
    string DisplayName);

public static class ProjectIdResolver
{
    public static ProjectIdentity Resolve(GitContext context)
    {
        var normalizedRemote = SelectRemote(context.Remotes) is { } remoteUrl
            ? RemoteUrlNormalizer.Normalize(remoteUrl)
            : null;
        var displayName = ResolveDisplayName(context, normalizedRemote);

        if (context.ConfigProjectId is not null)
        {
            var configured = context.ConfigProjectId.Trim();
            if (configured.Length == 0 || configured.Contains('@') ||
                configured != RemoteUrlNormalizer.Normalize(configured))
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InvalidArguments,
                    "codeknowledge.projectId must be a normalized project id without credentials.");
            return new ProjectIdentity(configured, "config", normalizedRemote, displayName);
        }

        if (normalizedRemote is not null)
            return new ProjectIdentity(normalizedRemote, "remote", normalizedRemote, displayName);

        var normalizedRoot = context.RepositoryRoot.Replace('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..16];
        return new ProjectIdentity($"local:{hash}", "local", null, displayName);
    }

    private static string? SelectRemote(IReadOnlyDictionary<string, string> remotes)
    {
        if (remotes.Count == 0)
            return null;
        if (remotes.TryGetValue("origin", out var origin))
            return origin;
        if (remotes.TryGetValue("upstream", out var upstream))
            return upstream;
        return remotes.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
    }

    private static string ResolveDisplayName(GitContext context, string? normalizedRemote)
    {
        if (!string.IsNullOrWhiteSpace(context.ConfigProjectName))
            return context.ConfigProjectName;
        if (normalizedRemote is not null)
            return normalizedRemote[(normalizedRemote.LastIndexOf('/') + 1)..];
        return Path.GetFileName(
            context.RepositoryRoot.TrimEnd(Path.DirectorySeparatorChar, '/'));
    }
}
