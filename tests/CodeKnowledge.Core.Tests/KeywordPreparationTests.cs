using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Search;

namespace CodeKnowledge.Core.Tests;

public sealed class KeywordPreparationTests
{
    [Fact]
    public void Prepare_routes_by_unicode_code_point_count() // 要件8.3
    {
        var prepared = KeywordPreparation.Prepare(["メール", "仕様", "M", "OrderCompleted"]);
        Assert.Equal(["メール", "OrderCompleted"], prepared.FtsKeywords);
        Assert.Equal(["仕様", "M"], prepared.LikeKeywords);
    }

    [Fact]
    public void Prepare_quotes_fts_keywords_and_joins_with_or() // AC-21・AC-22
    {
        var prepared = KeywordPreparation.Prepare(["sui-memory", "メール"]);
        Assert.Equal("\"sui-memory\" OR \"メール\"", prepared.FtsMatchExpression);
    }

    [Fact]
    public void Prepare_escapes_double_quotes_inside_keywords()
    {
        var prepared = KeywordPreparation.Prepare(["a\"b\"c"]);
        Assert.Equal("\"a\"\"b\"\"c\"", prepared.FtsMatchExpression);
    }

    [Fact]
    public void Prepare_applies_nfkc_normalization() // 要件8.4: 全角半角の揺れ吸収
    {
        var prepared = KeywordPreparation.Prepare(["ＡＢＣ"]); // 全角英字
        Assert.Equal(["ABC"], prepared.FtsKeywords);
    }

    [Fact]
    public void Prepare_discards_empty_keywords_and_rejects_all_invalid()
    {
        var prepared = KeywordPreparation.Prepare(["メール", "  "]);
        Assert.Single(prepared.FtsKeywords);

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => KeywordPreparation.Prepare(["", "   "]));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Prepare_returns_null_match_expression_without_fts_keywords()
    {
        Assert.Null(KeywordPreparation.Prepare(["仕様"]).FtsMatchExpression);
    }

    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData(@"a\b", @"%a\\b%")]
    public void EscapeLikePattern_escapes_metacharacters(string keyword, string expected) // AC-21
    {
        Assert.Equal(expected, KeywordPreparation.EscapeLikePattern(keyword));
    }

    [Fact]
    public void Prepare_strips_control_characters_from_keywords() // 要件8.4
    {
        var prepared = KeywordPreparation.Prepare(["ab\0cd", "a\tb"]);
        Assert.Equal(["abcd"], prepared.FtsKeywords);
        Assert.Equal(["ab"], prepared.LikeKeywords);
        Assert.NotNull(prepared.FtsMatchExpression);
        Assert.DoesNotContain(prepared.FtsMatchExpression!, ch => char.IsControl(ch));
    }

    [Fact]
    public void Prepare_drops_control_only_keywords_and_rejects_all_control()
    {
        var prepared = KeywordPreparation.Prepare(["メール", "\0"]);
        Assert.Equal(["メール"], prepared.FtsKeywords);
        Assert.Empty(prepared.LikeKeywords);

        var exception = Assert.Throws<CodeKnowledgeException>(
            () => KeywordPreparation.Prepare(["\0", ""]));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }
}
