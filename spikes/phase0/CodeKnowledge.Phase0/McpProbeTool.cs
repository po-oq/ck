using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace CodeKnowledge.Phase0;

[McpServerToolType]
public static class McpProbeTool
{
    [McpServerTool(Name = "phase0_probe", UseStructuredContent = true),
        Description("Returns Phase 0 MCP and SQLite diagnostics.")]
    public static object Probe() => new
    {
        status = "ok",
        executableVersion = typeof(McpProbeTool).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
        processId = Environment.ProcessId,
        sqliteVersion = GetSqliteVersion(),
        serverTimestampUtc = DateTimeOffset.UtcNow
    };

    private static string GetSqliteVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return (string)command.ExecuteScalar()!;
    }
}
