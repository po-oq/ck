namespace CodeKnowledge.Phase0.Tests;

public sealed class SearchProbeTests
{
    [Theory]
    [InlineData("仕様", SearchRoute.Like)]
    [InlineData("メール", SearchRoute.Fts)]
    [InlineData("ｍａｉｌ", SearchRoute.Fts)]
    public void SelectRoute_UsesUnicodeCodePointLength(string term, object expected) =>
        Assert.Equal(expected, SearchProbe.SelectRoute(term));

    [Fact]
    public void Search_MergesFtsAndLikeHits()
    {
        using var db = SearchTestDatabase.Create();
        var ids = SearchProbe.Search(db, ["メール", "仕様"]);
        Assert.Contains(1, ids);
    }

    [Theory]
    [InlineData("sui-memory", 2)]
    [InlineData("%", 2)]
    [InlineData("_", 2)]
    public void Search_TreatsMetaCharactersAsLiterals(string term, long expectedId)
    {
        using var db = SearchTestDatabase.Create();
        Assert.Contains(expectedId, SearchProbe.Search(db, [term]));
    }
}
