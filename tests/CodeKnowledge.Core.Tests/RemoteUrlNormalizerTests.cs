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
    // scp形式（スキームなし）の:は常にパス区切りであり、ポートとして解釈しない
    [InlineData("git@example.com:22/my-repo.git", "example.com/22/my-repo")]
    [InlineData("https://example.com/22/my-repo.git", "example.com/22/my-repo")]
    // IPv6ブラケットホスト: []内の:はホストの一部、]の後の:22はポートとして保持
    [InlineData("ssh://git@[2001:db8::1]:22/repo.git", "[2001:db8::1]:22/repo")]
    public void Normalize_applies_all_rules(string input, string expected)
    {
        Assert.Equal(expected, RemoteUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Scp_style_and_https_form_of_same_repo_yield_same_id() // AC-15
    {
        Assert.Equal(
            RemoteUrlNormalizer.Normalize("https://example.com/22/my-repo.git"),
            RemoteUrlNormalizer.Normalize("git@example.com:22/my-repo.git"));
    }

    [Fact]
    public void Normalize_never_retains_credentials()
    {
        var normalized = RemoteUrlNormalizer.Normalize("https://user:secret@host.example/a/b.git");
        Assert.DoesNotContain("user", normalized);
        Assert.DoesNotContain("secret", normalized);
    }
}
