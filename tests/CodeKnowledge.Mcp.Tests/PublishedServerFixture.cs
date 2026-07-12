using System.Diagnostics;

namespace CodeKnowledge.Mcp.Tests;

/// <summary>テストアセンブリごとに1回だけEXEを発行する。</summary>
public sealed class PublishedServerFixture : IDisposable
{
    public string ExePath { get; }
    public string PublishDirectory { get; }

    public PublishedServerFixture()
    {
        var repoRoot = FindRepoRoot();
        PublishDirectory = Path.Combine(
            Path.GetTempPath(), $"ck-publish-{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "publish", "src/CodeKnowledge.Mcp/CodeKnowledge.Mcp.csproj",
            "--configuration", "Release", "--runtime", "win-x64",
            "--self-contained", "false", "--output", PublishDirectory,
        })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"publish failed: {stdout}\n{stderr}");
        ExePath = Path.Combine(PublishDirectory, "CodeKnowledge.Mcp.exe");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CodeKnowledge.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("CodeKnowledge.slnx not found above test directory.");
    }

    public void Dispose()
    {
        try { Directory.Delete(PublishDirectory, recursive: true); } catch { }
    }
}
