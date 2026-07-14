using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodeKnowledge.Cli.Tests;

internal readonly record struct PublishedCliTarget(
    string RuntimeIdentifier, string ExecutableName);

internal static class PublishedCliTargetResolver
{
    public static PublishedCliTarget ResolveCurrent()
    {
        var os = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.Create(RuntimeInformation.OSDescription);
        return Resolve(os, RuntimeInformation.ProcessArchitecture);
    }

    public static PublishedCliTarget Resolve(OSPlatform os, Architecture cpu)
    {
        if (os == OSPlatform.Windows && cpu == Architecture.X64)
            return new("win-x64", "CodeKnowledge.Cli.exe");
        if (os == OSPlatform.OSX && cpu == Architecture.Arm64)
            return new("osx-arm64", "CodeKnowledge.Cli");
        throw new PlatformNotSupportedException(
            $"Supported E2E targets: WINDOWS/X64, OSX/Arm64; detected {os}/{cpu}.");
    }
}

/// <summary>テストアセンブリごとに1回だけEXEを発行する。</summary>
public sealed class PublishedCliFixture : IDisposable
{
    public string ExePath { get; }
    public string PublishDirectory { get; }

    public PublishedCliFixture()
    {
        var repoRoot = FindRepoRoot();
        var target = PublishedCliTargetResolver.ResolveCurrent();
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
            "publish", "src/CodeKnowledge.Cli/CodeKnowledge.Cli.csproj",
            "--configuration", "Release", "--runtime", target.RuntimeIdentifier,
            "--self-contained", "false", "--output", PublishDirectory,
            "--disable-build-servers",
        })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        // stdoutとstderrは並行に読み取る。逐次のReadToEndでは、後回しにした側の
        // パイプバッファが満杯になると子プロセスが書き込みでブロックし、
        // 親は読み終わらない側を待ち続けるデッドロックになり得る。
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"publish failed: {stdout}\n{stderr}");
        ExePath = Path.Combine(PublishDirectory, target.ExecutableName);
        if (!File.Exists(ExePath))
            throw new FileNotFoundException("Published CLI executable not found.", ExePath);
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
