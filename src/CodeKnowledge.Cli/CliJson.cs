using System.Text.Json;

namespace CodeKnowledge.Cli;

public static class CliJson
{
    // Core DTOのenumは各型の[JsonConverter]属性で小文字文字列化されるため、
    // ここではプロパティ命名(camelCase)だけMCPワイヤ形式へ合わせれば出力が一致する。
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
