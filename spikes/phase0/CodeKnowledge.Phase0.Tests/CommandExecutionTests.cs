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

    private static Task<ProcessResult> RunProbeAsync(params string[] arguments) =>
        ProcessRunner.RunAsync(
            "dotnet",
            [typeof(CommandLine).Assembly.Location, .. arguments],
            workingDirectory: null,
            TestContext.Current.CancellationToken);
}
