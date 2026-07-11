using System.Text.Json.Serialization;

namespace CodeKnowledge.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
public enum Confidence
{
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("medium")] Medium,
    [JsonStringEnumMemberName("low")] Low,
}

public static class ConfidenceParser
{
    public static bool TryParse(string? value, out Confidence confidence)
    {
        confidence = value switch
        {
            "high" => Confidence.High,
            "medium" => Confidence.Medium,
            "low" => Confidence.Low,
            _ => (Confidence)(-1),
        };
        return (int)confidence >= 0;
    }
}

public static class ConfidenceExtensions
{
    public static string ToDbValue(this Confidence confidence) => confidence switch
    {
        Confidence.High => "high",
        Confidence.Medium => "medium",
        Confidence.Low => "low",
        _ => throw new ArgumentOutOfRangeException(nameof(confidence)),
    };
}
