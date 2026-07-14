using System.Text.Json;
using CodeKnowledge.Cli;
using CodeKnowledge.Core.Errors;

namespace CodeKnowledge.Cli.Tests;

public sealed class ExitCodesTests
{
    [Theory]
    [InlineData(CodeKnowledgeException.InvalidArguments, 1)]
    [InlineData(CodeKnowledgeException.FactRequiresEvidence, 1)]
    [InlineData(CodeKnowledgeException.KnowledgeNotFound, 1)]
    [InlineData(CodeKnowledgeException.GitRepositoryRequired, 2)]
    [InlineData(CodeKnowledgeException.GitNotFound, 2)]
    [InlineData(CodeKnowledgeException.DatabaseBusy, 2)]
    [InlineData(CodeKnowledgeException.SchemaVersionUnsupported, 2)]
    [InlineData(CodeKnowledgeException.InternalError, 2)]
    public void Domain_errors_map_to_documented_exit_codes(string code, int expected)
    {
        var exception = new CodeKnowledgeException(code, "message");
        Assert.Equal(expected, ExitCodes.ForException(exception));
    }

    [Fact]
    public void Json_parse_failure_is_an_input_error()
    {
        var exception = new JsonException("bad json");
        Assert.Equal(1, ExitCodes.ForException(exception));
    }

    [Fact]
    public void Unexpected_error_is_a_precondition_failure()
    {
        Assert.Equal(2, ExitCodes.ForException(new InvalidOperationException("boom")));
    }
}
