using System.Diagnostics;

namespace CodeKnowledge.Phase0.Tests;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        Action<int>? processStarted = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        Task<string>? stdoutTask = null;
        Task<string>? stderrTask = null;
        try
        {
            processStarted?.Invoke(process.Id);
            stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(cancellationToken);
            return new(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception exception)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process exited between the state check and the kill request.
                }
            }

            await process.WaitForExitAsync(CancellationToken.None);
            if (stdoutTask is not null && stderrTask is not null)
            {
                await Task.WhenAll(stdoutTask, stderrTask);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }
}
