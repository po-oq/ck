using System.Text.Json;

namespace CodeKnowledge.Phase0.Tests;

public sealed class CommandExecutionTests
{
    [Fact]
    public async Task SelfCheck_WritesExactlyOneJsonValueToStandardOutput()
    {
        var result = await RunProbeAsync("self-check");

        Assert.Equal(ProbeExitCodes.Success, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("self-check", document.RootElement.GetProperty("Mode").GetString());
        Assert.Equal("ok", document.RootElement.GetProperty("Status").GetString());
        Assert.DoesNotContain("Hello, World!", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownMode_ReturnsInvalidArgumentsWithStderrDiagnosticOnly()
    {
        var result = await RunProbeAsync("unknown", "extra");

        Assert.Equal(ProbeExitCodes.InvalidArguments, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains(
            "invalid_arguments: unsupported mode 'unknown extra'",
            result.StandardError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelfCheck_WithExtraArguments_ReturnsInvalidArguments()
    {
        var result = await RunProbeAsync("self-check", "extra");

        Assert.Equal(ProbeExitCodes.InvalidArguments, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public void SelfCheckCleanup_WhenDatabaseDeleteFails_StillDeletesWalAndShm()
    {
        using var workspace = new TestWorkspace();
        var databasePath = workspace.PathFor("cleanup.db");
        File.WriteAllText(databasePath, "database");
        File.WriteAllText(databasePath + "-wal", "wal");
        File.WriteAllText(databasePath + "-shm", "shm");
        File.SetAttributes(databasePath, FileAttributes.ReadOnly);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(
                () => SelfCheckDatabaseCleanup.DeleteCandidates(databasePath));

            Assert.True(File.Exists(databasePath));
            Assert.False(File.Exists(databasePath + "-wal"));
            Assert.False(File.Exists(databasePath + "-shm"));
        }
        finally
        {
            File.SetAttributes(databasePath, FileAttributes.Normal);
        }
    }

    private static Task<ProcessResult> RunProbeAsync(params string[] arguments) =>
        ProcessRunner.RunAsync(
            "dotnet",
            [typeof(CommandLine).Assembly.Location, .. arguments],
            workingDirectory: null,
            TestContext.Current.CancellationToken);
}
