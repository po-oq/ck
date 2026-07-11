using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Infrastructure.Git;

public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);

public static class GitCommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static GitResult Run(string workingDirectory, params string[] arguments)
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

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new CodeKnowledgeException(
                    CodeKnowledgeException.GitNotFound, "Failed to start git.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new CodeKnowledgeException(
                    CodeKnowledgeException.InternalError,
                    $"git {arguments.FirstOrDefault()} timed out after {Timeout.TotalSeconds}s.");
            }
            return new GitResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Win32Exception)
        {
            throw new CodeKnowledgeException(
                CodeKnowledgeException.GitNotFound,
                "The git command was not found on PATH.");
        }
    }
}
