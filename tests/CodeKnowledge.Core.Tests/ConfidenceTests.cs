using CodeKnowledge.Core.Domain;

namespace CodeKnowledge.Core.Tests;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData("high", Confidence.High)]
    [InlineData("medium", Confidence.Medium)]
    [InlineData("low", Confidence.Low)]
    public void TryParse_accepts_defined_values(string input, Confidence expected)
    {
        Assert.True(ConfidenceParser.TryParse(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HIGH")]
    [InlineData("0.9")]
    [InlineData("certain")]
    public void TryParse_rejects_undefined_values(string? input)
    {
        Assert.False(ConfidenceParser.TryParse(input, out _));
    }

    [Fact]
    public void ToDbValue_roundtrips()
    {
        Assert.Equal("high", Confidence.High.ToDbValue());
        Assert.Equal("medium", Confidence.Medium.ToDbValue());
        Assert.Equal("low", Confidence.Low.ToDbValue());
    }
}
