using System.Diagnostics;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class TestGitRepo : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), $"ck-git-{Guid.NewGuid():N}");

    public TestGitRepo()
    {
        Directory.CreateDirectory(Root);
        Run("init", "--initial-branch=main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Test");
        Run("config", "commit.gpgsign", "false");
    }

    public string CommitFile(string relativePath, string content, string message = "test commit")
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        Run("add", relativePath);
        Run("commit", "-m", message);
        return Run("rev-parse", "HEAD").Trim();
    }

    public string Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    public void Dispose()
    {
        try
        {
            // .git配下の読み取り専用ファイルを削除可能にする
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch { }
    }
}
