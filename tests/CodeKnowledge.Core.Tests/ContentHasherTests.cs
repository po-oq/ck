using CodeKnowledge.Core.Hashing;

namespace CodeKnowledge.Core.Tests;

public sealed class ContentHasherTests
{
    [Fact]
    public void ComputeFileHash_is_deterministic_and_content_sensitive()
    {
        Assert.Equal(ContentHasher.ComputeFileHash("abc"), ContentHasher.ComputeFileHash("abc"));
        Assert.NotEqual(ContentHasher.ComputeFileHash("abc"), ContentHasher.ComputeFileHash("abd"));
    }

    [Fact]
    public void ComputeSymbolHash_extracts_line_range()
    {
        const string content = "line1\nline2\nline3\nline4\n";
        var hash23 = ContentHasher.ComputeSymbolHash(content, 2, 3);
        var hash22 = ContentHasher.ComputeSymbolHash(content, 2, 2);
        Assert.NotEqual(hash23, hash22);
    }

    [Fact]
    public void ComputeSymbolHash_ignores_whitespace_noise() // 要件9.4段階2の正規化
    {
        const string original = "void  Foo()\n{\n    Bar();   \n}\n";
        const string reformatted = "void Foo()\r\n{\r\n  Bar();\r\n}\r\n";
        Assert.Equal(
            ContentHasher.ComputeSymbolHash(original, 1, 4),
            ContentHasher.ComputeSymbolHash(reformatted, 1, 4));
    }

    [Fact]
    public void ComputeSymbolHash_detects_code_change()
    {
        const string before = "void Foo()\n{\n    Bar();\n}\n";
        const string after = "void Foo()\n{\n    Baz();\n}\n";
        Assert.NotEqual(
            ContentHasher.ComputeSymbolHash(before, 1, 4),
            ContentHasher.ComputeSymbolHash(after, 1, 4));
    }

    [Fact]
    public void ComputeSymbolHash_clamps_out_of_range_lines()
    {
        const string content = "line1\nline2\n";
        var hash = ContentHasher.ComputeSymbolHash(content, 1, 999);
        Assert.Equal(ContentHasher.ComputeSymbolHash(content, 1, 2), hash);
    }
}
