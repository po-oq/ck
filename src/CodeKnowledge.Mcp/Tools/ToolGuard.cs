using CodeKnowledge.Core.Errors;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;

namespace CodeKnowledge.Mcp.Tools;

public static class ToolGuard
{
    private const int SqliteBusy = 5;

    public static T Execute<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (CodeKnowledgeException exception)
        {
            throw new McpException($"{exception.Code}: {exception.Message}");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteBusy)
        {
            throw new McpException(
                $"{CodeKnowledgeException.DatabaseBusy}: The database is busy. Retry later.");
        }
        catch (Exception exception)
        {
            // 内部詳細（スタックトレース等）はクライアントへ返さずstderrログに任せる
            throw new McpException(
                $"{CodeKnowledgeException.InternalError}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
