using System.Text.Json;

namespace CodeKnowledge.Phase0.Tests;

public sealed class PublishSmokeTests
{
    [Fact]
    public async Task FrameworkDependentSingleFilePublish_RunsSelfCheckAndInventoriesArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "test-publish",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var publishResult = await ProcessRunner.RunAsync(
                "dotnet",
                [
                    "publish",
                    Path.Combine(
                        repositoryRoot,
                        "spikes",
                        "phase0",
                        "CodeKnowledge.Phase0",
                        "CodeKnowledge.Phase0.csproj"),
                    "--configuration",
                    "Release",
                    "--runtime",
                    "win-x64",
                    "--self-contained",
                    "false",
                    "--output",
                    outputDirectory
                ],
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.True(
                publishResult.ExitCode == 0,
                $"dotnet publish failed.{Environment.NewLine}stdout:{Environment.NewLine}{publishResult.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{publishResult.StandardError}");

            var publishedFiles = Directory
                .EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();

            foreach (var file in publishedFiles)
                TestContext.Current.TestOutputHelper?.WriteLine($"{file.Name}\t{file.Length}");

            var executablePath = Path.Combine(outputDirectory, "CodeKnowledge.Phase0.exe");
            Assert.True(File.Exists(executablePath), $"Published executable not found: {executablePath}");

            var selfCheckResult = await ProcessRunner.RunAsync(
                executablePath,
                ["self-check"],
                outputDirectory,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProbeExitCodes.Success, selfCheckResult.ExitCode);
            Assert.Empty(selfCheckResult.StandardError);
            using var document = JsonDocument.Parse(selfCheckResult.StandardOutput);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.Equal("ok", document.RootElement.GetProperty("Status").GetString());
            Assert.DoesNotContain(
                publishedFiles,
                file => string.Equals(
                    file.Name,
                    "CodeKnowledge.Phase0.dll",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodeKnowledge.Phase0.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find CodeKnowledge.Phase0.slnx above {AppContext.BaseDirectory}.");
    }
}
