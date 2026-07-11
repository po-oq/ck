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
        var ids = SearchProbe.Search(db, ["配送完了", "詳細"]);
        Assert.Equal([3, 4], ids.Order());
    }

    [Theory]
    [InlineData("sui-memory", 2)]
    [InlineData("%", 2)]
    [InlineData("_", 2)]
    [InlineData("\\", 6)]
    public void Search_TreatsMetaCharactersAsLiterals(string term, long expectedId)
    {
        using var db = SearchTestDatabase.Create();
        Assert.Equal([expectedId], SearchProbe.Search(db, [term]).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Search_IgnoresEmptyTerms(string term)
    {
        using var db = SearchTestDatabase.Create();
        Assert.Empty(SearchProbe.Search(db, [term]));
    }
}
