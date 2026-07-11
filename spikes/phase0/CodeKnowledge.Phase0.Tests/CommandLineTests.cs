namespace CodeKnowledge.Phase0.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_WithNoArguments_DefaultsToMcp() =>
        Assert.Equal(ProbeMode.Mcp, CommandLine.Parse([]).Mode);

    [Theory]
    [InlineData("mcp", ProbeMode.Mcp)]
    [InlineData("self-check", ProbeMode.SelfCheck)]
    [InlineData("concurrency-worker", ProbeMode.ConcurrencyWorker)]
    [InlineData("unknown", ProbeMode.Invalid)]
    public void Parse_SelectsExpectedMode(string argument, ProbeMode expected)
    {
        Assert.Equal(expected, CommandLine.Parse([argument]).Mode);
    }
}
