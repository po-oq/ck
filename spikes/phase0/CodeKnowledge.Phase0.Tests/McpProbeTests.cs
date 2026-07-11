using System.Text.Json;
using ModelContextProtocol.Client;

namespace CodeKnowledge.Phase0.Tests;

public sealed class McpProbeTests
{
    [Theory]
    [InlineData()]
    [InlineData("mcp")]
    public async Task StdioServer_ListsAndCallsPhase0ProbeWithoutStdoutContamination(
        params string[] modeArguments)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "phase0-test",
            Command = "dotnet",
            Arguments = [typeof(CommandLine).Assembly.Location, .. modeArguments]
        });
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, tool => tool.Name == "phase0_probe");

        var result = await client.CallToolAsync(
            "phase0_probe",
            cancellationToken: TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(result.StructuredContent);

        Assert.Contains("\"status\":\"ok\"", json, StringComparison.Ordinal);
        Assert.Contains("\"executableVersion\":", json, StringComparison.Ordinal);
        Assert.Contains("\"processId\":", json, StringComparison.Ordinal);
        Assert.Contains("\"sqliteVersion\":", json, StringComparison.Ordinal);
        Assert.Contains("\"serverTimestampUtc\":", json, StringComparison.Ordinal);
    }
}
