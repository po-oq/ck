using CodeKnowledge.Core.Projects;

namespace CodeKnowledge.Core.Tests;

public sealed class RemoteUrlNormalizerTests
{
    // 要件5.3.2の正規化例とAC-15、AC-16
    [Theory]
    [InlineData("git@github.com:Company/Order-API.git", "github.com/company/order-api")]
    [InlineData("https://github.com/company/order-api", "github.com/company/order-api")]
    [InlineData("https://user:token@github.com/Company/order-api.git", "github.com/company/order-api")]
    [InlineData("ssh://git@github.com/company/order-api", "github.com/company/order-api")]
    [InlineData("http://git.example.local:8443/Team/Order-System/", "git.example.local:8443/team/order-system")]
    [InlineData("git://github.com/a/b.git", "github.com/a/b")]
    [InlineData("https://github.com/a\\b", "github.com/a/b")]
    public void Normalize_applies_all_rules(string input, string expected)
    {
        Assert.Equal(expected, RemoteUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_never_retains_credentials()
    {
        var normalized = RemoteUrlNormalizer.Normalize("https://user:secret@host.example/a/b.git");
        Assert.DoesNotContain("user", normalized);
        Assert.DoesNotContain("secret", normalized);
    }
}
