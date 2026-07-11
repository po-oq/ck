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

    [Fact]
    public void Serializes_as_lowercase_string_in_json()
    {
        Assert.Equal("\"high\"", System.Text.Json.JsonSerializer.Serialize(Confidence.High));
        Assert.Equal("\"medium\"", System.Text.Json.JsonSerializer.Serialize(Confidence.Medium));
        Assert.Equal("\"low\"", System.Text.Json.JsonSerializer.Serialize(Confidence.Low));
    }

    [Fact]
    public void Deserializes_from_lowercase_string_in_json()
    {
        Assert.Equal(Confidence.High, System.Text.Json.JsonSerializer.Deserialize<Confidence>("\"high\""));
    }
}
