namespace CodeKnowledge.Phase0.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_WithNoArguments_DefaultsToMcp() =>
        Assert.Equal(ProbeMode.Mcp, CommandLine.Parse([]).Mode);

    [Fact]
    public void ProbeMode_IsInternal() =>
        Assert.False(typeof(ProbeMode).IsPublic);

    [Theory]
    [InlineData("mcp", "Mcp")]
    [InlineData("self-check", "SelfCheck")]
    [InlineData("concurrency-worker", "ConcurrencyWorker")]
    [InlineData("unknown", "Invalid")]
    public void Parse_SelectsExpectedMode(string argument, string expected)
    {
        Assert.Equal(expected, CommandLine.Parse([argument]).Mode.ToString());
    }
}
