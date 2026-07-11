using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Infrastructure.Git;

public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);

public sealed record GitBytesResult(int ExitCode, byte[] StandardOutput, string StandardError);

public static class GitCommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static GitResult Run(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = StartGit(workingDirectory, arguments);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            WaitOrKill(process, arguments);
            return new GitResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Win32Exception)
        {
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitNotFound,
                "The git command was not found on PATH.");
        }
    }

    /// <summary>
    /// Runs git and returns stdout as raw bytes, bypassing any StreamReader
    /// decoding (which would strip a leading UTF-8 BOM, among other things).
    /// </summary>
    public static GitBytesResult RunBytes(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = StartGit(workingDirectory, arguments);
            using var stdoutBuffer = new MemoryStream();
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer);
            var stderrTask = process.StandardError.ReadToEndAsync();
            WaitOrKill(process, arguments);
            stdoutTask.Wait();
            return new GitBytesResult(process.ExitCode, stdoutBuffer.ToArray(), stderrTask.Result);
        }
        catch (Win32Exception)
        {
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitNotFound,
                "The git command was not found on PATH.");
        }
    }

    private static Process StartGit(string workingDirectory, string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=false");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo)
            ?? throw new CodeKnowledgeException(
                CodeKnowledgeException.GitNotFound, "Failed to start git.");
    }

    private static void WaitOrKill(Process process, string[] arguments)
    {
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new CodeKnowledgeException(
                CodeKnowledgeException.InternalError,
                $"git {arguments.FirstOrDefault()} timed out after {Timeout.TotalSeconds}s.");
        }
    }
}
