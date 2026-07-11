using System.Globalization;
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
        var tool = Assert.Single(tools);
        Assert.Equal("phase0_probe", tool.Name);
        AssertOutputSchema(tool);

        var result = await client.CallToolAsync(
            "phase0_probe",
            cancellationToken: TestContext.Current.CancellationToken);
        using var contentDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(result.StructuredContent));
        var content = contentDocument.RootElement;
        Assert.Equal("ok", content.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(
            content.GetProperty("executableVersion").GetString()));
        Assert.True(content.GetProperty("processId").GetInt32() > 0);
        Assert.True(Version.TryParse(content.GetProperty("sqliteVersion").GetString(), out _));

        var timestampText = content.GetProperty("serverTimestampUtc").GetString();
        Assert.True(DateTimeOffset.TryParse(
            timestampText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp));
        Assert.Equal(TimeSpan.Zero, timestamp.Offset);
    }

    private static void AssertOutputSchema(McpClientTool tool)
    {
        using var schemaDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema));
        var schema = schemaDocument.RootElement;
        Assert.Equal("object", schema.GetProperty("type").GetString());

        var properties = schema.GetProperty("properties");
        Assert.Equal(
            [
                "executableVersion",
                "processId",
                "serverTimestampUtc",
                "sqliteVersion",
                "status"
            ],
            properties.EnumerateObject().Select(static property => property.Name).Order());
        Assert.Equal("string", properties.GetProperty("status").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("executableVersion").GetProperty("type").GetString());
        Assert.Equal("integer", properties.GetProperty("processId").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("sqliteVersion").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("serverTimestampUtc").GetProperty("type").GetString());

        Assert.Equal(
            [
                "executableVersion",
                "processId",
                "serverTimestampUtc",
                "sqliteVersion",
                "status"
            ],
            schema.GetProperty("required")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Order());
    }
}
