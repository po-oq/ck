using CodeKnowledge.Infrastructure.Database;

namespace CodeKnowledge.Infrastructure.Tests;

public sealed class DatabasePathResolverTests
{
    [Fact]
    public void Resolve_prefers_environment_variable()
    {
        var expected = Path.Combine(Path.GetTempPath(), "override.db");
        var actual = DatabasePathResolver.Resolve(
            name => name == "CODEKNOWLEDGE_DB_PATH" ? expected : null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_defaults_to_base_directory()
    {
        var actual = DatabasePathResolver.Resolve(_ => null);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "knowledge.db"), actual);
    }
}
