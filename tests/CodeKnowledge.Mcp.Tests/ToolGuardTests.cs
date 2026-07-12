using CodeKnowledge.Core.Errors;
using CodeKnowledge.Mcp.Tools;
using ModelContextProtocol;

namespace CodeKnowledge.Mcp.Tests;

public sealed class ToolGuardTests
{
    [Fact]
    public void Execute_passes_through_success()
    {
        Assert.Equal(42, ToolGuard.Execute(() => 42));
    }

    [Fact]
    public void Execute_maps_domain_error_to_mcp_exception_with_code()
    {
        var exception = Assert.Throws<McpException>(() => ToolGuard.Execute<int>(
            () => throw new CodeKnowledgeException(
                CodeKnowledgeException.GitRepositoryRequired,
                "The current directory is not inside a usable Git repository.")));
        Assert.StartsWith("git_repository_required: ", exception.Message);
    }

    [Fact]
    public void Execute_maps_unexpected_error_to_internal_error()
    {
        var exception = Assert.Throws<McpException>(() => ToolGuard.Execute<int>(
            () => throw new InvalidOperationException("boom")));
        Assert.StartsWith("internal_error: ", exception.Message);
    }
}
