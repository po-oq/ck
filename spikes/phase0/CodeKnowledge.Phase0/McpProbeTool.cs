using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace CodeKnowledge.Phase0;

public sealed record McpProbeResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("executableVersion")] string ExecutableVersion,
    [property: JsonPropertyName("processId")] int ProcessId,
    [property: JsonPropertyName("sqliteVersion")] string SqliteVersion,
    [property: JsonPropertyName("serverTimestampUtc")] DateTimeOffset ServerTimestampUtc);

[McpServerToolType]
public static class McpProbeTool
{
    [McpServerTool(Name = "phase0_probe", UseStructuredContent = true),
        Description("Returns Phase 0 MCP and SQLite diagnostics.")]
    public static McpProbeResponse Probe() => new(
        "ok",
        typeof(McpProbeTool).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
        Environment.ProcessId,
        GetSqliteVersion(),
        DateTimeOffset.UtcNow);

    private static string GetSqliteVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return (string)command.ExecuteScalar()!;
    }
}
